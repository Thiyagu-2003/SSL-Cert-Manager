using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    private readonly IHttpChallengeServer? _httpChallengeServer;
    private readonly SemaphoreSlim _workflowLock = new(1, 1);

    public CertificateIssuanceWorkflow(
        IAcmeCertificateClient acmeClient,
        IIisServerAdapter iisAdapter,
        IHttp01ChallengeResponder challengeResponder,
        ICertificateInstaller installer,
        IServiceProvider serviceProvider,
        ILogger<CertificateIssuanceWorkflow> logger,
        IHttpChallengeServer? httpChallengeServer = null)
    {
        _acmeClient = acmeClient;
        _iisAdapter = iisAdapter;
        _challengeResponder = challengeResponder;
        _installer = installer;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpChallengeServer = httpChallengeServer;
    }

    public async Task<CertificateRequestStatusResult> ExecuteAsync(
        CertificateRequestModel request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.DomainName))
        {
            return new CertificateRequestStatusResult
            {
                Success = false,
                Step = "Validation",
                ErrorMessage = "Domain name cannot be null or empty."
            };
        }

        await _workflowLock.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteInternalAsync(request, cancellationToken);
        }
        finally
        {
            _workflowLock.Release();
        }
    }

    private async Task<CertificateRequestStatusResult> ExecuteInternalAsync(
        CertificateRequestModel request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting certificate issuance workflow for {Domain} (Validation: {Method})", 
            request.DomainName, request.ValidationMethod);

        // Track created DNS records for safe cleanup
        var dnsRecordsToCleanup = new List<(IDnsProvider Provider, string ZoneId, string RecordId)>();
        var challenges = new List<AcmeChallengeItem>();

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

            bool isSelfSigned = string.Equals(request.ValidationMethod, "SelfSigned", StringComparison.OrdinalIgnoreCase);
            bool isDnsValidation = !isSelfSigned && (string.Equals(request.ValidationMethod, "Dns01", StringComparison.OrdinalIgnoreCase) ||
                                   asciiDomainNames.Any(d => d.StartsWith("*.", StringComparison.Ordinal)));

            string modeStr = isSelfSigned ? "Self-Signed" : (request.IsStaging ? "Let's Encrypt Staging" : "Let's Encrypt Production");
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var notifier = scope.ServiceProvider.GetService<INotificationProvider>();
                if (notifier != null)
                {
                    await notifier.SendAsync(new NotificationMessage(
                        $"Certificate Request Started ({primaryDomain})",
                        $"Initiated {modeStr} certificate request for domain '{primaryDomain}' (IIS Site: {request.SiteName ?? $"ID {request.SiteId}"}, Method: {request.ValidationMethod}).",
                        "Info"), cancellationToken);
                }
            }
            catch { /* non-fatal */ }

            string pfxPassword = Guid.NewGuid().ToString("N");
            byte[] pfxBytes;
            string thumbprint;
            DateTime notAfter;

            if (isSelfSigned)
            {
                _logger.LogInformation("Generating self-signed certificate for {Domain}...", primaryDomain);
                using var rsa = RSA.Create(2048);
                var certReq = new System.Security.Cryptography.X509Certificates.CertificateRequest($"CN={primaryDomain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var sanBuilder = new SubjectAlternativeNameBuilder();
                foreach (var d in asciiDomainNames)
                {
                    sanBuilder.AddDnsName(d);
                }
                certReq.CertificateExtensions.Add(sanBuilder.Build());
                certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // Server Authentication

                using var selfCert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
                pfxBytes = selfCert.Export(X509ContentType.Pfx, pfxPassword);
                thumbprint = selfCert.Thumbprint;
                notAfter = selfCert.NotAfter;
            }
            else
            {
                // Ensure contact email is set
                if (string.IsNullOrWhiteSpace(request.ContactEmail))
                {
                    request.ContactEmail = $"admin@{primaryDomain.TrimStart('*', '.')}";
                }

                // 2. Resolve Directory & Register ACME Account (Multi-CA support)
                var primaryDirectoryUri = WellKnownAcmeDirectories.ResolveDirectoryUri(
                    request.CertificateAuthority,
                    request.IsStaging,
                    request.CustomCaDirectoryUrl);

                string accountUrl = await _acmeClient.InitializeAccountWithDirectoryAsync(request.ContactEmail, primaryDirectoryUri, cancellationToken);
                _logger.LogInformation("ACME account ready on {DirectoryUri}: {AccountUrl}", primaryDirectoryUri, accountUrl);

                // 3. Create Order and retrieve challenges (with Multi-CA automatic failover)
                string valMethod = isDnsValidation ? "Dns01" : "Http01";
                IReadOnlyList<AcmeChallengeItem> acmeChallenges;
                try
                {
                    acmeChallenges = await _acmeClient.CreateOrderAndGetChallengesAsync(asciiDomainNames, valMethod, cancellationToken);
                }
                catch (Exception caEx) when (request.EnableCaFallback && !request.IsStaging && !string.IsNullOrWhiteSpace(request.FallbackCertificateAuthority))
                {
                    _logger.LogWarning(caEx, "Primary CA ({PrimaryCa}) order failed: {Msg}. Initiating Automatic Multi-CA Fallover to {FallbackCa}...", 
                        request.CertificateAuthority, caEx.Message, request.FallbackCertificateAuthority);

                    var fallbackUri = WellKnownAcmeDirectories.ResolveDirectoryUri(request.FallbackCertificateAuthority, false);
                    await _acmeClient.InitializeAccountWithDirectoryAsync(request.ContactEmail, fallbackUri, cancellationToken);
                    acmeChallenges = await _acmeClient.CreateOrderAndGetChallengesAsync(asciiDomainNames, valMethod, cancellationToken);
                }

                // If authorizations are already valid on Let's Encrypt (authz reuse), challenges list is empty
                if (acmeChallenges.Count > 0)
                {
                    challenges.AddRange(acmeChallenges);

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
                        // HTTP-01 Provisioning: Resolve IIS website physical path
                        if (request.SiteId <= 0)
                        {
                            var sites = await _iisAdapter.DiscoverDetailedWebsitesAsync(cancellationToken);
                            var matchingSite = sites.FirstOrDefault(s => s.Hostnames.Any(h => string.Equals(h, primaryDomain, StringComparison.OrdinalIgnoreCase)));
                            if (matchingSite != null)
                            {
                                request.SiteId = matchingSite.SiteId;
                            }
                        }

                        string? webRoot = request.SiteId > 0 ? _iisAdapter.GetWebsitePhysicalPath(request.SiteId) : null;
                        if (string.IsNullOrWhiteSpace(webRoot))
                        {
                            webRoot = Path.Combine(Path.GetTempPath(), $"certflow_fallback_{request.SiteId}");
                            Directory.CreateDirectory(webRoot);
                        }
                        else if (!Directory.Exists(webRoot))
                        {
                            try
                            {
                                Directory.CreateDirectory(webRoot);
                            }
                            catch
                            {
                                webRoot = Path.Combine(Path.GetTempPath(), $"certflow_fallback_{request.SiteId}");
                                Directory.CreateDirectory(webRoot);
                            }
                        }

                        // 4a. Start embedded HTTP.sys challenge server (intercepts /.well-known/acme-challenge/* on port 80)
                        if (_httpChallengeServer != null)
                        {
                            try
                            {
                                await _httpChallengeServer.StartAsync(cancellationToken);
                            }
                            catch (Exception srvEx)
                            {
                                _logger.LogWarning(srvEx, "Embedded HTTP challenge server could not be started: {Message}", srvEx.Message);
                            }
                        }

                        // 4b. Register token in embedded server & provision files on disk
                        foreach (var ch in challenges)
                        {
                            _httpChallengeServer?.RegisterToken(ch.Token, ch.KeyAuthorization);
                            await _challengeResponder.ProvisionChallengeAsync(webRoot, ch.Token, ch.KeyAuthorization, cancellationToken);
                            _logger.LogInformation("Provisioned HTTP-01 challenge token for {Domain}", ch.Domain);
                        }

                        // 4c. Pre-flight verification self-check (local HTTP probe before notifying Let's Encrypt)
                        foreach (var ch in challenges)
                        {
                            await PerformPreflightCheckAsync(ch, cancellationToken);
                        }
                    }

                    // 5. Submit Challenge to Let's Encrypt
                    foreach (var ch in challenges)
                    {
                        await _acmeClient.SubmitChallengeAsync(ch, cancellationToken);
                    }
                }
                else
                {
                    _logger.LogInformation("Domain authorizations are already valid on ACME authority; challenge provisioning not required.");
                }

                // 6. Finalize Order and Generate PFX (RSA or ECDSA)
                string keyAlgo = string.IsNullOrWhiteSpace(request.KeyAlgorithm) ? "RS256" : request.KeyAlgorithm;
                pfxBytes = await _acmeClient.FinalizeOrderAndExportPfxAsync(primaryDomain, pfxPassword, asciiDomainNames, keyAlgo, cancellationToken);

                // Extract thumbprint and validity
                using var x509 = new X509Certificate2(pfxBytes, pfxPassword);
                thumbprint = x509.Thumbprint;
                notAfter = x509.NotAfter;
            }

            // 7. Install into Windows Certificate Store
            var bundle = new CertificateBundle(pfxBytes, pfxPassword, thumbprint);
            var installed = await _installer.InstallAsync(bundle, new InstallOptions("My", "LocalMachine"), cancellationToken);

            // 7b. Automatically prune older superseded certificates for this domain to keep store clean
            if (request.AutoPruneSupersededCerts)
            {
                try
                {
                    int pruned = await _installer.CleanupSupersededCertificatesAsync(primaryDomain, thumbprint, new InstallOptions("My", "LocalMachine"), cancellationToken);
                    if (pruned > 0)
                    {
                        _logger.LogInformation("Auto-pruned {Count} superseded historical certificate(s) for {Domain} from Windows Certificate Store.", pruned, primaryDomain);
                    }
                }
                catch (Exception pruneEx)
                {
                    _logger.LogWarning(pruneEx, "Non-fatal certificate cleanup warning: {Message}", pruneEx.Message);
                }
            }

            // 8. Update IIS Binding if a valid SiteId is provided
            if (request.SiteId > 0)
            {
                var certRef = new CertificateReference(installed.Thumbprint, installed.StoreName, installed.StoreLocation);
                var deployTarget = new DeploymentTarget(request.SiteId, primaryDomain, 443, "*");
                var deployResult = await _iisAdapter.DeployCertificateAsync(certRef, deployTarget, cancellationToken);
                if (!deployResult.Success)
                {
                    _logger.LogError("IIS binding deployment failed: {Error}", deployResult.ErrorMessage);
                    return new CertificateRequestStatusResult
                    {
                        Success = false,
                        Step = "IIS Binding Deployment",
                        ErrorMessage = deployResult.ErrorMessage,
                        Thumbprint = thumbprint,
                        ExpiresAt = notAfter
                    };
                }
            }

            // 9. Cleanup HTTP-01 files if applicable
            if (!isSelfSigned && !isDnsValidation)
            {
                string? webRoot = request.SiteId > 0 ? _iisAdapter.GetWebsitePhysicalPath(request.SiteId) : null;
                if (!string.IsNullOrWhiteSpace(webRoot))
                {
                    foreach (var ch in challenges)
                    {
                        await _challengeResponder.CleanupChallengeAsync(webRoot, ch.Token, cancellationToken);
                    }
                }
            }

            // 10. Persist into SQLite
            var valMethodSaved = isSelfSigned ? ValidationMethod.SelfSigned : (isDnsValidation ? ValidationMethod.Dns01 : ValidationMethod.Http01);
            await PersistCertificateRecordAsync(primaryDomain, thumbprint, notAfter, request.SiteId, valMethodSaved, isSelfSigned, request.IsStaging, cancellationToken);

            _logger.LogInformation("Successfully issued, installed, and configured certificate {Thumbprint}", thumbprint);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var notifier = scope.ServiceProvider.GetService<INotificationProvider>();
                if (notifier != null)
                {
                    string target = request.SiteId > 0 ? $"IIS Site '{request.SiteName ?? request.SiteId.ToString()}'" : "Windows Certificate Store";
                    await notifier.SendAsync(new NotificationMessage(
                        $"Certificate Issued Successfully ({primaryDomain})",
                        $"Issued {modeStr} certificate for '{primaryDomain}' (Thumbprint: {thumbprint}, Expires: {notAfter:yyyy-MM-dd}). Installed to {target} on port 443 with SNI.",
                        "Info"), cancellationToken);
                }
            }
            catch { /* non-fatal */ }

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

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var notifier = scope.ServiceProvider.GetService<INotificationProvider>();
                if (notifier != null)
                {
                    await notifier.SendAsync(new NotificationMessage(
                        $"Certificate Issuance Failed ({request.DomainName})",
                        $"Issuance workflow failed for domain '{request.DomainName}': {ex.Message}",
                        "Error"), CancellationToken.None);
                }
            }
            catch { /* non-fatal */ }

            return new CertificateRequestStatusResult
            {
                Success = false,
                Step = "Failed",
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            // Clean up embedded challenge server tokens and shutdown listener
            if (_httpChallengeServer != null)
            {
                foreach (var ch in challenges)
                {
                    _httpChallengeServer.UnregisterToken(ch.Token);
                }
                try
                {
                    await _httpChallengeServer.StopAsync(CancellationToken.None);
                }
                catch { /* Non-fatal */ }
            }

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
        bool isSelfSigned,
        bool isStaging,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();

            string profileName = $"{domainName} - Automated";
            var profile = await db.CertificateProfiles.FirstOrDefaultAsync(p => p.ProfileName == profileName, cancellationToken);
            if (profile == null)
            {
                profile = new CertificateProfile
                {
                    ProfileName = profileName,
                    AuthorityId = isSelfSigned ? "selfsigned" : "letsencrypt",
                    DefaultValidationMethod = validationMethod,
                    RenewalThresholdPercent = 33,
                    UseAri = true
                };
                db.CertificateProfiles.Add(profile);
            }

            var domain = await db.Domains.FirstOrDefaultAsync(d => d.DomainName == domainName, cancellationToken);
            if (domain == null)
            {
                domain = new Domain
                {
                    DomainName = domainName,
                    DisplayName = domainName,
                    RootDomain = DomainNormalizer.Normalize(domainName).RootDomain,
                    DomainType = domainName.StartsWith("*.", StringComparison.Ordinal) ? DomainType.Wildcard : DomainType.Hostname
                };
                db.Domains.Add(domain);
            }

            string issuerName = isSelfSigned 
                ? "Self-Signed" 
                : (isStaging ? "Let's Encrypt (Staging)" : "Let's Encrypt");

            var certRecord = new CertificateRecord
            {
                Profile = profile,
                Subject = $"CN={domainName}",
                Issuer = issuerName,
                Thumbprint = thumbprint,
                NotBefore = DateTime.UtcNow,
                NotAfter = notAfter,
                Status = CertManager.Core.Enums.CertificateStatus.Active
            };
            db.Certificates.Add(certRecord);

            // If a valid site ID is provided, associate the bound IIS binding in the database
            if (siteId > 0)
            {
                var binding = await db.IisBindings
                    .Include(b => b.Website)
                    .FirstOrDefaultAsync(b => b.Website != null && b.Website.IisSiteId == siteId &&
                                              (string.IsNullOrEmpty(b.Hostname) || string.Equals(b.Hostname, domainName, StringComparison.OrdinalIgnoreCase)), cancellationToken);

                if (binding != null)
                {
                    binding.CertificateId = certRecord.CertificateId;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record issued certificate in database: {Message}", ex.Message);
        }
    }

    private async Task PerformPreflightCheckAsync(AcmeChallengeItem challenge, CancellationToken cancellationToken)
    {
        string token = challenge.Token;
        string expectedAuthz = challenge.KeyAuthorization;
        string domain = challenge.Domain;

        _logger.LogInformation("Starting pre-flight validation self-check for '{Domain}'...", domain);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Test 1: HTTP.sys / IIS local loopback (127.0.0.1 with Host header)
        bool verified = false;
        try
        {
            using var loopbackReq = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1/.well-known/acme-challenge/{token}");
            loopbackReq.Headers.Host = domain;
            var response = await httpClient.SendAsync(loopbackReq, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
                if (body == expectedAuthz || body.StartsWith(token, StringComparison.Ordinal))
                {
                    verified = true;
                    _logger.LogInformation("Pre-flight self-check passed: Local loopback verified HTTP-01 challenge response for '{Domain}' (HTTP 200).", domain);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Local loopback pre-flight probe: {Message}", ex.Message);
        }

        // Test 2: Direct domain probe if loopback was not conclusive
        if (!verified)
        {
            try
            {
                var response = await httpClient.GetAsync($"http://{domain}/.well-known/acme-challenge/{token}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    string body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
                    if (body == expectedAuthz || body.StartsWith(token, StringComparison.Ordinal))
                    {
                        verified = true;
                        _logger.LogInformation("Pre-flight self-check passed: Direct probe verified HTTP-01 challenge response for '{Domain}' (HTTP 200).", domain);
                    }
                }
                else
                {
                    _logger.LogWarning("Pre-flight probe warning for '{Domain}': Received HTTP {StatusCode}. If Let's Encrypt validation fails, verify router port 80 forwarding and IIS bindings.", domain, (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Pre-flight probe warning for '{Domain}': {Message}. Ensure port 80 is forwarded to this machine and publicly accessible.", domain, ex.Message);
            }
        }
    }
}
