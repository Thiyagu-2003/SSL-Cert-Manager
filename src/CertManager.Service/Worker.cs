using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Common;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using CertManager.Infrastructure.Data;
using CertManager.Infrastructure.Notifications;
using CertManager.Service.Services;
using CertManager.Service.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CertManager.Service;

public class Worker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Worker> _logger;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _lastExpiryAlertSent = new();

    public Worker(IServiceProvider serviceProvider, ILogger<Worker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CertFlow Background Worker started.");

        // First, ensure DB schema is initialized
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await initializer.InitializeAsync(stoppingToken);
            _logger.LogInformation("Database initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database on startup.");
        }

        // Periodic maintenance loop (renewal checks, monitoring & site discovery)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunMaintenanceSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during maintenance sweep.");
            }

            // Run check every 60 seconds
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("CertFlow Background Worker stopped.");
    }

    private async Task RunMaintenanceSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<ICertificateIssuanceWorkflow>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationProvider>();
        var discoveryEngine = scope.ServiceProvider.GetRequiredService<ISiteAutoDiscoveryEngine>();

        var now = DateTime.UtcNow;

        // 1. Reclaim expired workflow locks (stale locks left after an abrupt crash)
        var staleLocks = await db.WorkflowLocks
            .Where(l => l.LeaseExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (staleLocks.Count > 0)
        {
            _logger.LogWarning("Reclaiming {Count} expired workflow locks.", staleLocks.Count);
            db.WorkflowLocks.RemoveRange(staleLocks);
            await db.SaveChangesAsync(cancellationToken);
        }

        // 2. Scan active certificates for automated background renewal & expiry monitoring
        var activeCerts = await db.Certificates
            .Include(c => c.Profile)
            .Where(c => c.Status == CertManager.Core.Enums.CertificateStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var cert in activeCerts)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string domain = cert.Subject.Replace("CN=", "").Trim();
            if (string.IsNullOrEmpty(domain))
            {
                domain = cert.Profile?.ProfileName ?? "example.com";
            }

            // Expiry alert: notify if expiring in less than configured threshold (default 14 days), throttled to once every 24 hours per certificate
            var notifSettings = NotificationSettingsManager.CurrentSettings;
            int warningDays = notifSettings.ExpiryWarningDays > 0 ? notifSettings.ExpiryWarningDays : 14;

            if ((cert.NotAfter - now).TotalDays <= warningDays)
            {
                if (!_lastExpiryAlertSent.TryGetValue(cert.CertificateId, out var lastSent) || (now - lastSent).TotalHours >= 24)
                {
                    _lastExpiryAlertSent[cert.CertificateId] = now;
                    bool isAlreadyExpired = cert.NotAfter <= now;
                    string alertSubject = isAlreadyExpired
                        ? $"Certificate Expired ({domain})"
                        : $"Certificate Expiring Soon ({domain})";
                    string alertLevel = isAlreadyExpired ? "Error" : "Warning";
                    string alertBody = isAlreadyExpired
                        ? $"Certificate for {cert.Subject} expired on {cert.NotAfter:yyyy-MM-dd HH:mm UTC}. Automated renewal attempt is underway."
                        : $"Certificate for {cert.Subject} expires on {cert.NotAfter:yyyy-MM-dd HH:mm UTC} (in {(cert.NotAfter - now).TotalDays:F1} days). Automated renewal scheduled.";

                    await notifier.SendAsync(new NotificationMessage(alertSubject, alertBody, alertLevel), cancellationToken);
                }
            }

            bool isDue = RenewalPolicyEvaluator.ShouldRenew(cert, cert.Profile, now);
            if (!isDue) continue;

            string lockKey = $"renew:cert:{cert.CertificateId}";
            var existingLock = await db.WorkflowLocks.FindAsync(new object[] { lockKey }, cancellationToken);

            if (existingLock != null && existingLock.LeaseExpiresAt > now)
            {
                // Lock currently held by another runner
                continue;
            }

            // Acquire 20-minute lease lock (update existing tracked entity in-place to avoid EF Core key conflicts)
            if (existingLock != null)
            {
                existingLock.OwnerOperationId = Guid.NewGuid();
                existingLock.AcquiredAt = now;
                existingLock.LeaseExpiresAt = now.AddMinutes(20);
            }
            else
            {
                db.WorkflowLocks.Add(new WorkflowLock
                {
                    LockKey = lockKey,
                    OwnerOperationId = Guid.NewGuid(),
                    AcquiredAt = now,
                    LeaseExpiresAt = now.AddMinutes(20)
                });
            }
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                _logger.LogInformation("Initiating autonomous background renewal for certificate {Thumbprint} (expires {NotAfter})", cert.Thumbprint, cert.NotAfter);

                // Find associated IIS site if any (default to 0 so non-IIS certificates aren't mistakenly forced to Site 1)
                var boundSite = await db.IisBindings
                    .Include(b => b.Website)
                    .FirstOrDefaultAsync(b => b.CertificateId == cert.CertificateId, cancellationToken);

                long siteId = boundSite?.Website?.IisSiteId ?? 0;

                var renewalRequest = new CertificateRequestModel
                {
                    SiteId = siteId,
                    DomainName = domain,
                    ContactEmail = $"admin@{domain.TrimStart('*', '.')}",
                    IsStaging = cert.Issuer.Contains("Staging", StringComparison.OrdinalIgnoreCase),
                    ValidationMethod = cert.Profile?.DefaultValidationMethod == ValidationMethod.Dns01 ? "Dns01" : "Http01"
                };

                var result = await workflow.ExecuteAsync(renewalRequest, cancellationToken);

                if (result.Success)
                {
                    _logger.LogInformation("Autonomous background renewal succeeded! New thumbprint: {NewThumbprint}", result.Thumbprint);
                    cert.Status = CertManager.Core.Enums.CertificateStatus.Archived;
                    cert.RetainUntil = now.AddDays(14); // Keep old certificate record archived for 14 days safety period

                    // If an IIS binding was bound to the old cert, re-point it to the new cert record
                    if (boundSite != null && !string.IsNullOrWhiteSpace(result.Thumbprint))
                    {
                        var newCert = await db.Certificates.FirstOrDefaultAsync(c => c.Thumbprint == result.Thumbprint, cancellationToken);
                        if (newCert != null)
                        {
                            boundSite.CertificateId = newCert.CertificateId;
                        }
                    }

                    await db.SaveChangesAsync(cancellationToken);

                    await notifier.SendAsync(new NotificationMessage(
                        "Certificate Renewed Successfully",
                        $"Autonomous renewal completed for {domain}. New Thumbprint: {result.Thumbprint}, Valid until {result.ExpiresAt:yyyy-MM-dd}.",
                        "Info"
                    ), cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Autonomous background renewal failed for {Domain}: {Error}", domain, result.ErrorMessage);

                    await notifier.SendAsync(new NotificationMessage(
                        "Certificate Renewal Failed",
                        $"Automated renewal attempt failed for {domain}: {result.ErrorMessage}. Will retry on next schedule cycle.",
                        "Error"
                    ), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during background renewal for certificate {CertificateId}", cert.CertificateId);
            }
            finally
            {
                // Release lock using CancellationToken.None so shutdown/cancellation doesn't leave locks stuck
                try
                {
                    var currentLock = await db.WorkflowLocks.FindAsync(new object[] { lockKey }, CancellationToken.None);
                    if (currentLock != null)
                    {
                        db.WorkflowLocks.Remove(currentLock);
                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception lockEx)
                {
                    _logger.LogWarning(lockEx, "Failed to release workflow lock {LockKey}", lockKey);
                }
            }
        }

        // 3. Site Auto-Discovery: Check for newly added IIS websites
        try
        {
            var proposals = await discoveryEngine.EvaluateNewWebsitesAsync(autoDeployIfPolicyEnabled: true, cancellationToken);
            if (proposals.Count > 0)
            {
                _logger.LogInformation("Discovered {Count} newly created or unconfigured IIS websites matching active certificates.", proposals.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error occurred during IIS site auto-discovery sweep.");
        }
    }
}
