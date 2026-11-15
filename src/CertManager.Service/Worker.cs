using System;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Infrastructure.Data;
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
        _logger.LogInformation("CertManager Background Worker started.");

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

            // Run check every 60 seconds (or on scheduled cadence)
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }

        _logger.LogInformation("CertManager Background Worker stopped.");
    }

    private async Task RunMaintenanceSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();

        // Reclaim expired workflow locks (stale locks left after an abrupt crash)
        var now = DateTime.UtcNow;
        var staleLocks = await db.WorkflowLocks
            .Where(l => l.LeaseExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (staleLocks.Count > 0)
        {
            _logger.LogWarning("Reclaiming {Count} expired workflow locks.", staleLocks.Count);
            db.WorkflowLocks.RemoveRange(staleLocks);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
