using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Common;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Models;
using CertManager.Infrastructure.Data;
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

        // Periodic maintenance loop (renewal checks & expired lock reclamation)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunMaintenanceSweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during maintenance sweep.");
            }

            // Run check every 60 seconds
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }

        _logger.LogInformation("CertFlow Background Worker stopped.");
    }

    private async Task RunMaintenanceSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<ICertificateIssuanceWorkflow>();

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

        // 2. Scan active certificates for automated background renewal
        var activeCerts = await db.Certificates
            .Include(c => c.Profile)
            .Where(c => c.Status == CertManager.Core.Enums.CertificateStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var cert in activeCerts)
        {
            if (cancellationToken.IsCancellationRequested) break;

            bool isDue = RenewalPolicyEvaluator.ShouldRenew(cert, cert.Profile, now);
            if (!isDue) continue;

            string lockKey = $"renew:cert:{cert.CertificateId}";
            var existingLock = await db.WorkflowLocks.FindAsync(new object[] { lockKey }, cancellationToken);

            if (existingLock != null && existingLock.LeaseExpiresAt > now)
            {
                // Lock currently held by another runner
                continue;
            }

            // Acquire 20-minute lease lock
            var renewalLock = new WorkflowLock
            {
                LockKey = lockKey,
                OwnerOperationId = Guid.NewGuid(),
                AcquiredAt = now,
                LeaseExpiresAt = now.AddMinutes(20)
            };

            if (existingLock != null)
            {
                db.WorkflowLocks.Remove(existingLock);
            }
            db.WorkflowLocks.Add(renewalLock);
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                _logger.LogInformation("Initiating autonomous background renewal for certificate {Thumbprint} (expires {NotAfter})", cert.Thumbprint, cert.NotAfter);

                // Determine target domain from subject or profile
                string domain = cert.Subject.Replace("CN=", "").Trim();
                if (string.IsNullOrEmpty(domain))
                {
                    domain = cert.Profile?.ProfileName ?? "example.com";
                }

                // Find associated IIS site if any
                var boundSite = await db.IisBindings
                    .Include(b => b.Website)
                    .FirstOrDefaultAsync(b => b.CertificateId == cert.CertificateId, cancellationToken);

                long siteId = boundSite?.Website?.IisSiteId ?? 1;

                var renewalRequest = new CertificateRequestModel
                {
                    SiteId = siteId,
                    DomainName = domain,
                    ContactEmail = $"admin@{domain.TrimStart('*', '.')}",
                    IsStaging = cert.Issuer.Contains("Staging", StringComparison.OrdinalIgnoreCase),
                    ValidationMethod = "Http01"
                };

                var result = await workflow.ExecuteAsync(renewalRequest, cancellationToken);

                if (result.Success)
                {
                    _logger.LogInformation("Autonomous background renewal succeeded! New thumbprint: {NewThumbprint}", result.Thumbprint);
                    cert.Status = CertManager.Core.Enums.CertificateStatus.Archived;
                    cert.RetainUntil = now.AddDays(14); // Keep old certificate record archived for 14 days safety period
                    await db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Autonomous background renewal failed for {Domain}: {Error}", domain, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during background renewal for certificate {CertificateId}", cert.CertificateId);
            }
            finally
            {
                // Release lock
                var currentLock = await db.WorkflowLocks.FindAsync(new object[] { lockKey }, cancellationToken);
                if (currentLock != null)
                {
                    db.WorkflowLocks.Remove(currentLock);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
