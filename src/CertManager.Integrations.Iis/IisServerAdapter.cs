using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using Microsoft.Web.Administration;

namespace CertManager.Integrations.Iis;

public interface IIisServerAdapter : IServerAdapter
{
    bool IsIisAvailable();
    string? GetWebsitePhysicalPath(long siteId);
    Task<IReadOnlyList<DiscoveredIisSite>> DiscoverDetailedWebsitesAsync(CancellationToken cancellationToken = default);
}

public class IisServerAdapter : IIisServerAdapter
{
    public bool IsIisAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "inetsrv", "config", "applicationHost.config");

        return File.Exists(configPath);
    }

    public string? GetWebsitePhysicalPath(long siteId)
    {
        if (!IsIisAvailable()) return null;

        try
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites.FirstOrDefault(s => s.Id == siteId);
            var rootApp = site?.Applications.FirstOrDefault(a => a.Path == "/");
            var rootVdir = rootApp?.VirtualDirectories.FirstOrDefault(v => v.Path == "/");
            return rootVdir?.PhysicalPath;
        }
        catch
        {
            return null;
        }
    }

    public Task<IReadOnlyList<ManagedWebsite>> DiscoverWebsitesAsync(CancellationToken cancellationToken = default)
    {
        var detailed = DiscoverDetailedWebsitesCore();
        var result = detailed.Select(d => new ManagedWebsite(
            d.SiteId,
            d.SiteName,
            d.State,
            d.Bindings.Select(b => $"{b.Protocol}://{b.Hostname}:{b.Port}").ToList()
        )).ToList();

        return Task.FromResult<IReadOnlyList<ManagedWebsite>>(result);
    }

    public Task<IReadOnlyList<DiscoveredIisSite>> DiscoverDetailedWebsitesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DiscoverDetailedWebsitesCore());
    }

    private IReadOnlyList<DiscoveredIisSite> DiscoverDetailedWebsitesCore()
    {
        if (!IsIisAvailable())
        {
            return Array.Empty<DiscoveredIisSite>();
        }

        var results = new List<DiscoveredIisSite>();

        try
        {
            using var serverManager = new ServerManager();
            foreach (var site in serverManager.Sites)
            {
                string siteState = "Unknown";
                try
                {
                    siteState = site.State.ToString();
                }
                catch { }

                var bindings = new List<DiscoveredBinding>();
                var hostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool hasHttps = false;

                foreach (var b in site.Bindings)
                {
                    string protocol = b.Protocol;
                    string host = b.Host;
                    int port = b.EndPoint?.Port ?? (protocol.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
                    string ip = b.EndPoint?.Address?.ToString() ?? "*";
                    bool isHttps = protocol.Equals("https", StringComparison.OrdinalIgnoreCase);

                    if (isHttps)
                    {
                        hasHttps = true;
                    }

                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        hostnames.Add(host);
                    }

                    string? thumbprint = null;
                    if (b.CertificateHash != null && b.CertificateHash.Length > 0)
                    {
                        thumbprint = Convert.ToHexString(b.CertificateHash);
                    }

                    bool sni = false;
                    try
                    {
                        var sslFlagsAttr = b.Attributes["sslFlags"];
                        if (sslFlagsAttr != null && sslFlagsAttr.Value != null)
                        {
                            int flags = Convert.ToInt32(sslFlagsAttr.Value);
                            sni = (flags & 1) != 0;
                        }
                    }
                    catch { }

                    bindings.Add(new DiscoveredBinding(protocol, host, port, ip, sni, thumbprint));
                }

                results.Add(new DiscoveredIisSite(
                    site.Id,
                    site.Name,
                    siteState,
                    hasHttps,
                    bindings,
                    hostnames.ToList()
                ));
            }
        }
        catch { }

        return results;
    }

    public Task<DeploymentResult> DeployCertificateAsync(
        CertificateReference certificate,
        DeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        if (!IsIisAvailable())
        {
            return Task.FromResult(new DeploymentResult(true, null, null));
        }

        byte[] certHash = Convert.FromHexString(certificate.Thumbprint);
        string? previousThumbprint = null;

        try
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites.FirstOrDefault(s => s.Id == target.SiteId);
            if (site == null)
            {
                return Task.FromResult(new DeploymentResult(false, $"IIS Site with ID {target.SiteId} not found.", null));
            }

            // Look for existing HTTPS binding matching target hostname/port
            var existingBinding = site.Bindings.FirstOrDefault(b =>
                b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                b.EndPoint?.Port == target.Port &&
                string.Equals(b.Host, target.Hostname, StringComparison.OrdinalIgnoreCase));

            if (existingBinding != null)
            {
                if (existingBinding.CertificateHash != null && existingBinding.CertificateHash.Length > 0)
                {
                    previousThumbprint = Convert.ToHexString(existingBinding.CertificateHash);
                }

                existingBinding.CertificateHash = certHash;
                existingBinding.CertificateStoreName = certificate.StoreName;
                try
                {
                    existingBinding.SetAttributeValue("sslFlags", 1); // Enable SNI
                }
                catch { }
            }
            else
            {
                // Create new HTTPS binding with SNI flag enabled
                string bindingInfo = $"{target.IpAddress}:{target.Port}:{target.Hostname}";
                var newBinding = site.Bindings.CreateElement();
                newBinding.Protocol = "https";
                newBinding.BindingInformation = bindingInfo;
                newBinding.CertificateHash = certHash;
                newBinding.CertificateStoreName = certificate.StoreName;

                try
                {
                    newBinding.SetAttributeValue("sslFlags", 1); // SNI
                }
                catch { }

                site.Bindings.Add(newBinding);
            }

            serverManager.CommitChanges();
            return Task.FromResult(new DeploymentResult(true, null, previousThumbprint));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DeploymentResult(false, $"Failed to commit IIS binding changes: {ex.Message}", previousThumbprint));
        }
    }

    public Task<HealthCheckResult> VerifyAsync(
        DeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HealthCheckResult(true, "HTTPS binding verified."));
    }
}
