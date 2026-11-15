using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Common;
using CertManager.Core.Entities;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using CertManager.Infrastructure.Data;
using CertManager.Integrations.Iis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CertManager.Service.Services;

public record ProposedDeployment(
    long SiteId,
    string SiteName,
    string Hostname,
    string MatchedThumbprint,
    bool AutoDeployPolicyEnabled);

public interface ISiteAutoDiscoveryEngine
{
    Task<IReadOnlyList<ProposedDeployment>> EvaluateNewWebsitesAsync(
        bool autoDeployIfPolicyEnabled,
        CancellationToken cancellationToken = default);
}

public class SiteAutoDiscoveryEngine : ISiteAutoDiscoveryEngine
{
    private readonly CertManagerDbContext _db;
    private readonly IIisServerAdapter _iisAdapter;
    private readonly ILogger<SiteAutoDiscoveryEngine> _logger;

    public SiteAutoDiscoveryEngine(
        CertManagerDbContext db,
        IIisServerAdapter iisAdapter,
        ILogger<SiteAutoDiscoveryEngine> logger)
    {
        _db = db;
        _iisAdapter = iisAdapter;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProposedDeployment>> EvaluateNewWebsitesAsync(
        bool autoDeployIfPolicyEnabled,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scanning for newly added IIS websites for auto-discovery...");
        var discoveredSites = await _iisAdapter.DiscoverDetailedWebsitesAsync(cancellationToken);
        var activeCerts = await _db.Certificates
            .AsNoTracking()
            .Where(c => c.Status == Core.Enums.CertificateStatus.Active)
            .ToListAsync(cancellationToken);

        var proposals = new List<ProposedDeployment>();

        foreach (var site in discoveredSites)
        {
            // Only consider sites that do not currently have a fully valid HTTPS binding
            if (site.HasHttps) continue;

            // Hostnames from bindings
            var hostnames = site.Bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.Hostname))
                .Select(b => b.Hostname.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            if (hostnames.Count == 0 && !string.IsNullOrWhiteSpace(site.SiteName))
            {
                if (site.SiteName.Contains('.'))
                {
                    hostnames.Add(site.SiteName.Trim().ToLowerInvariant());
                }
            }

            foreach (var host in hostnames)
            {
                var matchedCert = FindMatchingCertificate(host, activeCerts);
                if (matchedCert != null)
                {
                    // Check if an explicit auto-deploy policy is configured in profile or database
                    bool policyEnabled = false; // By default requires explicit administrator enablement (Section 10.3)

                    proposals.Add(new ProposedDeployment(
                        site.SiteId,
                        site.SiteName,
                        host,
                        matchedCert.Thumbprint,
                        policyEnabled
                    ));

                    if (autoDeployIfPolicyEnabled && policyEnabled)
                    {
                        _logger.LogInformation("Policy allows auto-deployment: Binding certificate {Thumbprint} to Site {SiteName} for host {Host}",
                            matchedCert.Thumbprint, site.SiteName, host);

                        var certRef = new CertificateReference(matchedCert.Thumbprint, matchedCert.StoreName, matchedCert.StoreLocation);
                        var target = new DeploymentTarget(site.SiteId, host, 443, "*");
                        await _iisAdapter.DeployCertificateAsync(certRef, target, cancellationToken);
                    }
                    else
                    {
                        _logger.LogInformation("Proposed deployment found for {Host} on Site {SiteName} (Pending Admin Approval).", host, site.SiteName);
                    }
                }
            }
        }

        return proposals;
    }

    public static CertificateRecord? FindMatchingCertificate(string hostname, IEnumerable<CertificateRecord> certificates)
    {
        string normalizedHost = hostname.Trim().ToLowerInvariant();

        foreach (var cert in certificates)
        {
            string subjectName = cert.Subject.Replace("CN=", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();

            // Direct exact match
            if (string.Equals(subjectName, normalizedHost, StringComparison.OrdinalIgnoreCase))
            {
                return cert;
            }

            // Wildcard match (e.g. *.example.com matches sub.example.com)
            if (subjectName.StartsWith("*.", StringComparison.Ordinal))
            {
                string suffix = subjectName[2..]; // "example.com"
                if (normalizedHost.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    // Ensure it's a single label subdomain (e.g. foo.example.com, not foo.bar.example.com)
                    string leftPart = normalizedHost[..^(suffix.Length + 1)];
                    if (!leftPart.Contains('.'))
                    {
                        return cert;
                    }
                }
            }
        }

        return null;
    }
}
