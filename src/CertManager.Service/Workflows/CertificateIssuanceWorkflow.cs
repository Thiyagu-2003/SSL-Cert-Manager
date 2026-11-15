using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Acme.Clients;
using CertManager.Core.Common;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using CertManager.Infrastructure.Data;
using CertManager.Integrations.Dns;
using CertManager.Integrations.Iis;
using CertManager.Integrations.Iis.Challenges;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CertManager.Service.Workflows;

public interface ICertificateIssuanceWorkflow
{
    Task<CertificateRequestStatusResult> ExecuteAsync(
        CertificateRequestModel request,
        CancellationToken cancellationToken = default);
}

public class CertificateIssuanceWorkflow : ICertificateIssuanceWorkflow
{
    private readonly IAcmeCertificateClient _acmeClient;
    private readonly IIisServerAdapter _iisAdapter;
    private readonly IHttp01ChallengeResponder _challengeResponder;
    private readonly ICertificateInstaller _installer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CertificateIssuanceWorkflow> _logger;

    public CertificateIssuanceWorkflow(
        IAcmeCertificateClient acmeClient,
        IIisServerAdapter iisAdapter,
        IHttp01ChallengeResponder challengeResponder,
        ICertificateInstaller installer,
        IServiceProvider serviceProvider,
        ILogger<CertificateIssuanceWorkflow> logger)
    {
        _acmeClient = acmeClient;
        _iisAdapter = iisAdapter;
        _challengeResponder = challengeResponder;
        _installer = installer;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<CertificateRequestStatusResult> ExecuteAsync(
        CertificateRequestModel request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting certificate issuance workflow for {Domain} (Validation: {Method})", 
            request.DomainName, request.ValidationMethod);

        // Track created DNS records for safe cleanup
        var dnsRecordsToCleanup = new List<(IDnsProvider Provider, string ZoneId, string RecordId)>();

        try
        {
            // 1. Normalize domain(s) — supporting single, wildcard, and apex + wildcard combinations
            var rawDomains = request.DomainName.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rawDomains.Length == 0)
            {
                rawDomains = new[] { request.DomainName.Trim() };
            }

            var normalizedDomains = rawDomains.Select(DomainNormalizer.Normalize).ToList();
            var asciiDomainNames = normalizedDomains.Select(d => d.AsciiName).Distinct().ToList();
            string primaryDomain = asciiDomainNames[0];

            bool isDnsValidation = string.Equals(request.ValidationMethod, "Dns01", StringComparison.OrdinalIgnoreCase) ||
                                   asciiDomainNames.Any(d => d.StartsWith("*.", StringComparison.Ordinal));

            // 2. Register ACME Account
            string accountUrl = await _acmeClient.InitializeAccountAsync(request.ContactEmail, request.IsStaging, cancellationToken);
            _logger.LogInformation("ACME account ready: {AccountUrl}", accountUrl);

            // 3. Create Order and retrieve challenges
            string valMethod = isDnsValidation ? "Dns01" : "Http01";
            var challenges = await _acmeClient.CreateOrderAndGetChallengesAsync(asciiDomainNames, valMethod, cancellationToken);
            if (challenges.Count == 0)
            {
                return new CertificateRequestStatusResult
                {
                    Success = false,
                    Step = "Order Creation",
                    ErrorMessage = $"No {valMethod} challenges returned by ACME authority."
                };
            }

            // 4. Provision Challenges
            if (isDnsValidation)
            {
                _logger.LogInformation("Provisioning DNS-01 challenge records...");
                IDnsProvider dnsProvider = string.Equals(request.DnsProvider, "Cloudflare", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.DnsApiToken)
                    ? new CloudflareDnsProvider(request.DnsApiToken)
                    : new ManualDnsProvider();

                foreach (var ch in challenges)
                {
                    string recordName = DnsChallengeHelper.GetRecordName(ch.Domain);
                    string recordValue = ch.DnsTxtValue;

                    // Determine Zone ID
                    string zoneId = request.DnsZoneId ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(zoneId))
                    {
                        var zones = await dnsProvider.GetZonesAsync(cancellationToken);
                        string rootDomain = DomainNormalizer.Normalize(ch.Domain).RootDomain;
                        var matchedZone = zones.FirstOrDefault(z => string.Equals(z.Name, rootDomain, StringComparison.OrdinalIgnoreCase)) ?? zones.FirstOrDefault();
                        zoneId = matchedZone?.Id ?? "default-zone";
                    }

                    _logger.LogInformation("Creating TXT record: {Name} = {Value} in zone {ZoneId}", recordName, recordValue, zoneId);
                    var record = await dnsProvider.CreateTxtRecordAsync(zoneId, recordName, recordValue, cancellationToken);
                    dnsRecordsToCleanup.Add((dnsProvider, zoneId, record.Id));

                    // Wait for DNS propagation check (up to 30 seconds)
                    _logger.LogInformation("Verifying DNS propagation for {RecordName}...", recordName);
                    for (int i = 0; i < 6; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        bool visible = await dnsProvider.IsTxtRecordVisibleAsync(recordName, recordValue, cancellationToken);
                        if (visible)
                        {
                            _logger.LogInformation("DNS TXT record is publicly visible!");
                            break;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                }
            }
            else
            {
                // HTTP-01 Provisioning
                string? webRoot = _iisAdapter.GetWebsitePhysicalPath(request.SiteId);
                if (string.IsNullOrWhiteSpace(webRoot))
                {
                    webRoot = Path.Combine(Path.GetTempPath(), $"certflow_fallback_{request.SiteId}");
                    Directory.CreateDirectory(webRoot);
                }

                foreach (var ch in challenges)
                {
                    await _challengeResponder.ProvisionChallengeAsync(webRoot, ch.Token, ch.KeyAuthorization, cancellationToken);
                    _logger.LogInformation("Provisioned HTTP-01 challenge token for {Domain}", ch.Domain);
                }
            }

            // 5. Submit Challenge to Let's Encrypt
            foreach (var ch in challenges)
            {
                await _acmeClient.SubmitChallengeAsync(ch, cancellationToken);
            }

            // 6. Finalize Order and Generate PFX
            string pfxPassword = Guid.NewGuid().ToString("N");
            byte[] pfxBytes = await _acmeClient.FinalizeOrderAndExportPfxAsync(primaryDomain, pfxPassword, cancellationToken);

            // Extract thumbprint and validity
            using var x509 = new X509Certificate2(pfxBytes, pfxPassword);
            string thumbprint = x509.Thumbprint;
            DateTime notAfter = x509.NotAfter;

            // 7. Install into Windows Certificate Store
            var bundle = new CertificateBundle(pfxBytes, pfxPassword, thumbprint);
            var installed = await _installer.InstallAsync(bundle, new InstallOptions("My", "LocalMachine"), cancellationToken);

            // 8. Update IIS Binding if a valid SiteId is provided
            if (request.SiteId > 0)
            {
                var certRef = new CertificateReference(installed.Thumbprint, installed.StoreName, installed.StoreLocation);
                var deployTarget = new DeploymentTarget(request.SiteId, primaryDomain, 443, "*");
                await _iisAdapter.DeployCertificateAsync(certRef, deployTarget, cancellationToken);
            }

            // 9. Cleanup HTTP-01 files if applicable
            if (!isDnsValidation)
            {
                string? webRoot = _iisAdapter.GetWebsitePhysicalPath(request.SiteId);
                if (!string.IsNullOrWhiteSpace(webRoot))
                {
                    foreach (var ch in challenges)
                    {
                        await _challengeResponder.CleanupChallengeAsync(webRoot, ch.Token, cancellationToken);
                    }
                }
            }

            // 10. Persist into SQLite
            await PersistCertificateRecordAsync(primaryDomain, thumbprint, notAfter, request.SiteId, isDnsValidation ? ValidationMethod.Dns01 : ValidationMethod.Http01, cancellationToken);

            _logger.LogInformation("Successfully issued, installed, and configured certificate {Thumbprint}", thumbprint);

            return new CertificateRequestStatusResult
            {
                Success = true,
                Step = "Completed",
                Thumbprint = thumbprint,
                ExpiresAt = notAfter,
                BoundSiteName = request.SiteId > 0 ? $"IIS Site {request.SiteId}" : "Certificate Store"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Certificate issuance failed: {Message}", ex.Message);
            return new CertificateRequestStatusResult
            {
                Success = false,
                Step = "Failed",
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            // Clean up any DNS records created during the challenge so only owned challenge records are cleaned up
            foreach (var (provider, zoneId, recordId) in dnsRecordsToCleanup)
            {
                try
                {
                    _logger.LogInformation("Cleaning up DNS challenge record {RecordId} in zone {ZoneId}...", recordId, zoneId);
                    await provider.DeleteTxtRecordAsync(zoneId, recordId, CancellationToken.None);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to clean up DNS record {RecordId}: {Message}", recordId, cleanupEx.Message);
                }
            }
        }
    }

    private async Task PersistCertificateRecordAsync(
        string domainName,
        string thumbprint,
        DateTime notAfter,
        long siteId,
        ValidationMethod validationMethod,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();

            var profile = new CertificateProfile
            {
                ProfileName = $"{domainName} - Automated",
                AuthorityId = "letsencrypt",
                DefaultValidationMethod = validationMethod,
                RenewalThresholdPercent = 33,
                UseAri = true
            };
            db.CertificateProfiles.Add(profile);

            var domain = await db.Domains.FirstOrDefaultAsync(d => d.DomainName == domainName, cancellationToken);
            if (domain == null)
            {
                domain = new Domain
                {
                    DomainName = domainName,
                    DisplayName = domainName,
                    RootDomain = domainName,
                    DomainType = domainName.StartsWith("*.", StringComparison.Ordinal) ? DomainType.Wildcard : DomainType.Hostname
                };
                db.Domains.Add(domain);
            }

            var certRecord = new CertificateRecord
            {
                Profile = profile,
                Subject = $"CN={domainName}",
                Issuer = "Let's Encrypt",
                Thumbprint = thumbprint,
                NotBefore = DateTime.UtcNow,
                NotAfter = notAfter,
                Status = CertManager.Core.Enums.CertificateStatus.Active
            };
            db.Certificates.Add(certRecord);

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record issued certificate in database: {Message}", ex.Message);
        }
    }
}
