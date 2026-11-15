using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Ipc;
using CertManager.Core.Models;
using CertManager.Infrastructure.Data;
using CertManager.Infrastructure.Ipc;
using CertManager.Integrations.Iis;
using CertManager.Service.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CertManager.Service.Services;

public class IpcHostService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IIisServerAdapter _iisAdapter;
    private readonly ICertificateIssuanceWorkflow _issuanceWorkflow;
    private readonly ILogger<IpcHostService> _logger;
    private readonly DateTime _startTimeUtc = DateTime.UtcNow;

    public const string PipeName = "CertManager.Ipc";

    public IpcHostService(
        IServiceProvider serviceProvider,
        IIisServerAdapter iisAdapter,
        ICertificateIssuanceWorkflow issuanceWorkflow,
        ILogger<IpcHostService> logger)
    {
        _serviceProvider = serviceProvider;
        _iisAdapter = iisAdapter;
        _issuanceWorkflow = issuanceWorkflow;
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
        _logger.LogDebug("Received IPC action: {Action}", request.Action);

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

            case "GetWebsites":
            {
                var discovered = await _iisAdapter.DiscoverDetailedWebsitesAsync();

                try
                {
                    foreach (var d in discovered)
                    {
                        var existing = await db.IisWebsites
                            .Include(w => w.Bindings)
                            .FirstOrDefaultAsync(w => w.IisSiteId == d.SiteId);

                        if (existing == null)
                        {
                            var newSite = new IisWebsite
                            {
                                IisSiteId = d.SiteId,
                                WebsiteName = d.SiteName,
                                State = d.State,
                                LastDiscoveredAt = DateTime.UtcNow
                            };

                            foreach (var b in d.Bindings)
                            {
                                newSite.Bindings.Add(new IisBinding
                                {
                                    Protocol = b.Protocol,
                                    Hostname = b.Hostname,
                                    Port = b.Port,
                                    IpAddress = b.IpAddress,
                                    SslFlagsSni = b.SslFlagsSni
                                });
                            }

                            db.IisWebsites.Add(newSite);
                        }
                        else
                        {
                            existing.WebsiteName = d.SiteName;
                            existing.State = d.State;
                            existing.LastDiscoveredAt = DateTime.UtcNow;
                        }
                    }

                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist discovered IIS sites into database.");
                }

                return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(discovered));
            }

            case "RequestCertificate":
            {
                if (string.IsNullOrWhiteSpace(request.PayloadJson))
                {
                    return IpcResponse.Fail(request.RequestId, "Request payload cannot be empty.");
                }

                var requestModel = JsonSerializer.Deserialize<CertificateRequestModel>(request.PayloadJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (requestModel == null)
                {
                    return IpcResponse.Fail(request.RequestId, "Malformed CertificateRequestModel JSON payload.");
                }

                var result = await _issuanceWorkflow.ExecuteAsync(requestModel);
                return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(result));
            }

            case "GetCertificates":
            {
                var certs = await db.Certificates
                    .AsNoTracking()
                    .ToListAsync();
                return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(certs));
            }

            default:
                return IpcResponse.Fail(request.RequestId, $"Unknown action '{request.Action}'.");
        }
    }
}
