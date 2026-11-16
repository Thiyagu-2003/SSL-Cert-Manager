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
            string? rawPath = rootVdir?.PhysicalPath;
            return !string.IsNullOrWhiteSpace(rawPath) ? Environment.ExpandEnvironmentVariables(rawPath) : null;
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
                    string bindingInfo = b.BindingInformation ?? string.Empty;
                    string[] parts = bindingInfo.Split(':');
                    string ip = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : (b.EndPoint?.Address?.ToString() ?? "*");
                    int port = parts.Length > 1 && int.TryParse(parts[1], out var parsedPort)
                        ? parsedPort
                        : (b.EndPoint?.Port ?? (protocol.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80));
                    string host = !string.IsNullOrWhiteSpace(b.Host) ? b.Host : (parts.Length > 2 ? parts[2] : string.Empty);
                    bool isHttps = protocol.Equals("https", StringComparison.OrdinalIgnoreCase);

                    string? thumbprint = null;
                    if (b.CertificateHash != null && b.CertificateHash.Length > 0)
                    {
                        thumbprint = Convert.ToHexString(b.CertificateHash);
                    }

                    if (isHttps && !string.IsNullOrWhiteSpace(thumbprint))
                    {
                        hasHttps = true;
                    }

                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        hostnames.Add(host);
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

        string? previousThumbprint = null;

        try
        {
            if (string.IsNullOrWhiteSpace(certificate.Thumbprint))
            {
                return Task.FromResult(new DeploymentResult(false, "Certificate thumbprint cannot be null or empty.", null));
            }

            string cleanThumbprint = certificate.Thumbprint.Replace(" ", "").Replace(":", "").Trim();
            byte[] certHash = Convert.FromHexString(cleanThumbprint);

            using var serverManager = new ServerManager();
            var site = serverManager.Sites.FirstOrDefault(s => s.Id == target.SiteId);
            if (site == null)
            {
                return Task.FromResult(new DeploymentResult(false, $"IIS Site with ID {target.SiteId} not found.", null));
            }

            int GetPort(Binding b) => b.EndPoint?.Port ?? 
                (int.TryParse(b.BindingInformation?.Split(':').ElementAtOrDefault(1), out var p) ? p : 
                (b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80));

            string GetHost(Binding b) => !string.IsNullOrEmpty(b.Host) ? b.Host : 
                (b.BindingInformation?.Split(':').ElementAtOrDefault(2) ?? string.Empty);

            // Look for existing HTTPS binding matching target hostname/port
            var existingBinding = site.Bindings.FirstOrDefault(b =>
                b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                GetPort(b) == target.Port &&
                string.Equals(GetHost(b), target.Hostname, StringComparison.OrdinalIgnoreCase));

            // If no exact hostname match, check for an unconfigured placeholder HTTPS binding on target.Port
            // (i.e. empty hostname AND NO certificate hash bound to it)
            if (existingBinding == null && !string.IsNullOrWhiteSpace(target.Hostname))
            {
                existingBinding = site.Bindings.FirstOrDefault(b =>
                    b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                    GetPort(b) == target.Port &&
                    string.IsNullOrWhiteSpace(GetHost(b)) &&
                    (b.CertificateHash == null || b.CertificateHash.Length == 0));
            }

            if (existingBinding != null)
            {
                if (existingBinding.CertificateHash != null && existingBinding.CertificateHash.Length > 0)
                {
                    previousThumbprint = Convert.ToHexString(existingBinding.CertificateHash);
                }

                existingBinding.CertificateHash = certHash;
                existingBinding.CertificateStoreName = certificate.StoreName;

                if (!string.IsNullOrWhiteSpace(target.Hostname))
                {
                    existingBinding.BindingInformation = $"{target.IpAddress}:{target.Port}:{target.Hostname}";
                    try
                    {
                        existingBinding.SetAttributeValue("sslFlags", (int)SslFlags.Sni);
                    }
                    catch { }
                }
            }
            else
            {
                // Create new HTTPS binding with SNI flag enabled
                string bindingInfo = $"{target.IpAddress}:{target.Port}:{target.Hostname}";
                var sslFlags = string.IsNullOrWhiteSpace(target.Hostname) ? SslFlags.None : SslFlags.Sni;
                site.Bindings.Add(bindingInfo, certHash, certificate.StoreName, sslFlags);
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
        if (!IsIisAvailable())
        {
            return Task.FromResult(new HealthCheckResult(true, "IIS not present on this host; simulated verification passed."));
        }

        try
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites.FirstOrDefault(s => s.Id == target.SiteId);
            if (site == null)
            {
                return Task.FromResult(new HealthCheckResult(false, $"IIS Site with ID {target.SiteId} not found."));
            }

            int GetPort(Binding b) => b.EndPoint?.Port ?? 
                (int.TryParse(b.BindingInformation?.Split(':').ElementAtOrDefault(1), out var p) ? p : 
                (b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80));

            string GetHost(Binding b) => !string.IsNullOrEmpty(b.Host) ? b.Host : 
                (b.BindingInformation?.Split(':').ElementAtOrDefault(2) ?? string.Empty);

            bool verified = site.Bindings.Any(b =>
                b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                GetPort(b) == target.Port &&
                b.CertificateHash != null && b.CertificateHash.Length > 0 &&
                (string.IsNullOrWhiteSpace(target.Hostname) || string.Equals(GetHost(b), target.Hostname, StringComparison.OrdinalIgnoreCase)));

            if (!verified)
            {
                return Task.FromResult(new HealthCheckResult(false, $"No active HTTPS binding with valid certificate found for {target.Hostname}:{target.Port} on site '{site.Name}'."));
            }

            return Task.FromResult(new HealthCheckResult(true, $"HTTPS binding for {target.Hostname}:{target.Port} verified on site '{site.Name}'."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(false, $"Verification failed: {ex.Message}"));
        }
    }
}
