using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Enums;
using CertManager.Core.Ipc;
using CertManager.Infrastructure.Data;
using CertManager.Infrastructure.Ipc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CertManager.Service.Services;

public class IpcHostService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IpcHostService> _logger;
    private readonly DateTime _startTimeUtc = DateTime.UtcNow;

    public const string PipeName = "CertManager.Ipc";

    public IpcHostService(IServiceProvider serviceProvider, ILogger<IpcHostService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting IPC Named Pipe Server on pipe '{PipeName}'...", PipeName);

        var server = new NamedPipeServer(PipeName, HandleIpcRequestAsync);
        await server.StartAsync(stoppingToken);

        _logger.LogInformation("IPC Named Pipe Server stopped.");
    }

    private async Task<IpcResponse> HandleIpcRequestAsync(IpcRequest request, WindowsIdentity? callerIdentity)
    {
        _logger.LogDebug("Received IPC action: {Action} from {Identity}", request.Action, callerIdentity?.Name ?? "Anonymous");

        // Role authorization check (Operator or higher for mutations, Auditor for reads)
        var callerRole = WindowsIdentityAuthorizer.ResolveRole(callerIdentity);
        if (callerRole == UserRole.Anonymous)
        {
            return IpcResponse.Fail(request.RequestId, "Access denied: Unauthenticated caller.");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();

        switch (request.Action)
        {
            case "GetStatus":
            {
                var totalCerts = await db.Certificates.CountAsync();
                var expiringSoonThreshold = DateTime.UtcNow.AddDays(30);
                var expiringSoon = await db.Certificates
                    .CountAsync(c => c.Status == CertificateStatus.Active && c.NotAfter <= expiringSoonThreshold);
                var activeOrders = await db.AcmeOrders
                    .CountAsync(o => o.Status == AcmeOrderStatus.Pending || o.Status == AcmeOrderStatus.Processing);

                var status = new ServiceStatusResponse
                {
                    IsRunning = true,
                    Version = "2.0.0",
                    UptimeSeconds = (DateTime.UtcNow - _startTimeUtc).TotalSeconds,
                    TotalCertificates = totalCerts,
                    ExpiringSoonCount = expiringSoon,
                    ActiveOrdersCount = activeOrders,
                    ServerTimeUtc = DateTime.UtcNow
                };

                return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(status));
            }

            case "GetCertificates":
            {
                var certs = await db.Certificates
                    .AsNoTracking()
                    .ToListAsync();
                return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(certs));
            }

            case "GetWebsites":
            {
                var sites = await db.IisWebsites
                    .Include(w => w.Bindings)
                    .AsNoTracking()
                    .ToListAsync();
                return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(sites));
            }

            default:
                return IpcResponse.Fail(request.RequestId, $"Unknown action '{request.Action}'.");
        }
    }
}
