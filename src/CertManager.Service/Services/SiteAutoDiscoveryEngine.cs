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
        var now = DateTime.UtcNow;
        var activeCerts = await _db.Certificates
            .AsNoTracking()
            .Include(c => c.CertificateDomains)
                .ThenInclude(cd => cd.Domain)
            .Where(c => c.Status == Core.Enums.CertificateStatus.Active && c.NotAfter > now)
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
        if (string.IsNullOrWhiteSpace(hostname)) return null;
        string normalizedHost = hostname.Trim().ToLowerInvariant();

        foreach (var cert in certificates)
        {
            // Collect all candidate names for this cert (CN + SAN domains)
            var candidateNames = new List<string>();

            // Extract CN from cert.Subject (handles "CN=example.com" or "CN=example.com, O=Org, C=US")
            string subject = cert.Subject ?? string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(subject, @"(?:^|,\s*)CN=([^,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                candidateNames.Add(match.Groups[1].Value.Trim().ToLowerInvariant());
            }
            else
            {
                candidateNames.Add(subject.Replace("CN=", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant());
            }

            // Also add any SAN domains from CertificateDomains
            if (cert.CertificateDomains != null)
            {
                foreach (var cd in cert.CertificateDomains)
                {
                    if (cd.Domain != null && !string.IsNullOrWhiteSpace(cd.Domain.DomainName))
                    {
                        candidateNames.Add(cd.Domain.DomainName.Trim().ToLowerInvariant());
                    }
                }
            }

            foreach (var candidate in candidateNames.Distinct())
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                // Direct exact match
                if (string.Equals(candidate, normalizedHost, StringComparison.OrdinalIgnoreCase))
                {
                    return cert;
                }

                // Wildcard match (e.g. *.example.com matches sub.example.com)
                if (candidate.StartsWith("*.", StringComparison.Ordinal))
                {
                    string suffix = candidate[2..]; // "example.com"
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
        }

        return null;
    }
}
