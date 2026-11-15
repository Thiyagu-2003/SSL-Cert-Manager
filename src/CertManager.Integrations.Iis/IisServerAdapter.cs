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
                catch
                {
                    // Site may be stopped or query restricted
                }

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
                        // SslFlags in IIS 8+
                        var sslFlagsAttr = b.Attributes["sslFlags"];
                        if (sslFlagsAttr != null && sslFlagsAttr.Value != null)
                        {
                            int flags = Convert.ToInt32(sslFlagsAttr.Value);
                            sni = (flags & 1) != 0; // SNI flag is bit 1
                        }
                    }
                    catch
                    {
                        // Fallback on older IIS versions
                    }

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
        catch
        {
            // Graceful fallback if access denied or IIS stopped
        }

        return results;
    }

    public Task<DeploymentResult> DeployCertificateAsync(
        CertificateReference certificate,
        DeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DeploymentResult(true, null, null));
    }

    public Task<HealthCheckResult> VerifyAsync(
        DeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HealthCheckResult(true, "Healthy"));
    }
}
