using System;
using System.IO;
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
using CertManager.Integrations.Iis;
using CertManager.Integrations.Iis.Challenges;
using Microsoft.EntityFrameworkCore;
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
        _logger.LogInformation("Starting certificate issuance workflow for domain {Domain} on IIS Site {SiteId}", request.DomainName, request.SiteId);

        try
        {
            // 1. Normalize domain
            var normalized = DomainNormalizer.Normalize(request.DomainName);

            // 2. Resolve IIS Physical Path
            string? webRoot = _iisAdapter.GetWebsitePhysicalPath(request.SiteId);
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(Path.GetTempPath(), $"certflow_fallback_{request.SiteId}");
                Directory.CreateDirectory(webRoot);
            }

            // 3. Register ACME Account
            string accountUrl = await _acmeClient.InitializeAccountAsync(request.ContactEmail, request.IsStaging, cancellationToken);
            _logger.LogInformation("ACME account ready: {AccountUrl}", accountUrl);

            // 4. Create Order and retrieve challenges
            var challenges = await _acmeClient.CreateOrderAndGetChallengesAsync(new[] { normalized.AsciiName }, cancellationToken);
            if (challenges.Count == 0)
            {
                return new CertificateRequestStatusResult
                {
                    Success = false,
                    Step = "Order Creation",
                    ErrorMessage = "No HTTP-01 challenges returned by ACME authority."
                };
            }

            // 5. Provision HTTP-01 Challenges
            foreach (var ch in challenges)
            {
                await _challengeResponder.ProvisionChallengeAsync(webRoot, ch.Token, ch.KeyAuthorization, cancellationToken);
                _logger.LogInformation("Provisioned HTTP-01 challenge token for {Domain}", ch.Domain);
            }

            // 6. Submit Challenge to Let's Encrypt
            foreach (var ch in challenges)
            {
                await _acmeClient.SubmitChallengeAsync(ch, cancellationToken);
            }

            // 7. Finalize Order and Generate PFX
            string pfxPassword = Guid.NewGuid().ToString("N");
            byte[] pfxBytes = await _acmeClient.FinalizeOrderAndExportPfxAsync(normalized.AsciiName, pfxPassword, cancellationToken);

            // Extract thumbprint and validity
            using var x509 = new X509Certificate2(pfxBytes, pfxPassword);
            string thumbprint = x509.Thumbprint;
            DateTime notAfter = x509.NotAfter;

            // 8. Install into Windows Certificate Store
            var bundle = new CertificateBundle(pfxBytes, pfxPassword, thumbprint);
            var installed = await _installer.InstallAsync(bundle, new InstallOptions("My", "LocalMachine"), cancellationToken);

            // 9. Update IIS Binding
            var certRef = new CertificateReference(installed.Thumbprint, installed.StoreName, installed.StoreLocation);
            var deployTarget = new DeploymentTarget(request.SiteId, normalized.AsciiName, 443, "*");
            var deployResult = await _iisAdapter.DeployCertificateAsync(certRef, deployTarget, cancellationToken);

            // 10. Cleanup HTTP-01 Challenge files
            foreach (var ch in challenges)
            {
                await _challengeResponder.CleanupChallengeAsync(webRoot, ch.Token, cancellationToken);
            }

            // 11. Persist into SQLite
            await PersistCertificateRecordAsync(normalized.AsciiName, thumbprint, notAfter, request.SiteId, cancellationToken);

            _logger.LogInformation("Successfully issued, installed, and bound certificate {Thumbprint} to IIS Site {SiteId}", thumbprint, request.SiteId);

            return new CertificateRequestStatusResult
            {
                Success = true,
                Step = "Completed",
                Thumbprint = thumbprint,
                ExpiresAt = notAfter,
                BoundSiteName = $"IIS Site {request.SiteId}"
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
    }

    private async Task PersistCertificateRecordAsync(
        string domainName,
        string thumbprint,
        DateTime notAfter,
        long siteId,
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
                DefaultValidationMethod = ValidationMethod.Http01,
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
                    DomainType = DomainType.Hostname
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
