using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Application.Diagnostics;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Interfaces;
using CertManager.Core.Ipc;
using CertManager.Core.Models;
using CertManager.Infrastructure.Certificates;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private async Task<IpcResponse> HandleIpcRequestAsync(IpcRequest request, WindowsIdentity? callerIdentity)
    {
        _logger.LogDebug("Received IPC action: {Action}", request.Action);

        try
        {
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

                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(status, JsonOptions));
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

                                existing.Bindings.Clear();
                                foreach (var b in d.Bindings)
                                {
                                    existing.Bindings.Add(new IisBinding
                                    {
                                        Protocol = b.Protocol,
                                        Hostname = b.Hostname,
                                        Port = b.Port,
                                        IpAddress = b.IpAddress,
                                        SslFlagsSni = b.SslFlagsSni
                                    });
                                }
                            }
                        }

                        await db.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist discovered IIS sites into database.");
                    }

                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(discovered, JsonOptions));
                }

                case "RequestCertificate":
                {
                    if (string.IsNullOrWhiteSpace(request.PayloadJson))
                    {
                        return IpcResponse.Fail(request.RequestId, "Request payload cannot be empty.");
                    }

                    var requestModel = JsonSerializer.Deserialize<CertificateRequestModel>(request.PayloadJson, JsonOptions);

                    if (requestModel == null)
                    {
                        return IpcResponse.Fail(request.RequestId, "Malformed CertificateRequestModel JSON payload.");
                    }

                    var result = await _issuanceWorkflow.ExecuteAsync(requestModel);
                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(result, JsonOptions));
                }

                case "GetCertificates":
                {
                    var certs = await db.Certificates
                        .AsNoTracking()
                        .ToListAsync();
                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(certs, JsonOptions));
                }

                case "GetNotifications":
                {
                    var logs = await db.Notifications
                        .AsNoTracking()
                        .OrderByDescending(n => n.SentAt)
                        .Take(200)
                        .ToListAsync();
                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(logs, JsonOptions));
                }

                case "ClearNotifications":
                {
                    db.Notifications.RemoveRange(db.Notifications);
                    await db.SaveChangesAsync();
                    CertManager.Infrastructure.Logging.FileLoggerHelper.ClearLogFile();
                    return IpcResponse.Ok(request.RequestId, "true");
                }

                case "GetProfiles":
                {
                    var profiles = await db.CertificateProfiles
                        .AsNoTracking()
                        .ToListAsync();
                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(profiles, JsonOptions));
                }

                case "ToggleHttpsRedirect":
                {
                    if (string.IsNullOrWhiteSpace(request.PayloadJson))
                    {
                        return IpcResponse.Fail(request.RequestId, "Payload required: { siteId, enable }.");
                    }

                    using var doc = JsonDocument.Parse(request.PayloadJson);
                    long siteId = doc.RootElement.GetProperty("siteId").GetInt64();
                    bool enable = doc.RootElement.GetProperty("enable").GetBoolean();

                    bool success = await _iisAdapter.SetHttpsRedirectAsync(siteId, enable);
                    bool current = _iisAdapter.IsHttpsRedirectEnabled(siteId);

                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(new { success, siteId, hasHttpsRedirect = current }, JsonOptions));
                }

                case "ExportCertificate":
                {
                    if (string.IsNullOrWhiteSpace(request.PayloadJson))
                    {
                        return IpcResponse.Fail(request.RequestId, "Payload required: CertificateExportRequest.");
                    }

                    var exportReq = JsonSerializer.Deserialize<CertificateExportRequest>(request.PayloadJson, JsonOptions);
                    if (exportReq == null)
                    {
                        return IpcResponse.Fail(request.RequestId, "Malformed CertificateExportRequest payload.");
                    }

                    var exportService = _serviceProvider.GetService<ICertificateExportService>()
                        ?? new CertificateExportService();

                    var result = await exportService.ExportCertificateAsync(exportReq);
                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(result, JsonOptions));
                }

                case "TestDomainReadiness":
                {
                    string? domain = null;
                    if (!string.IsNullOrWhiteSpace(request.PayloadJson))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(request.PayloadJson);
                            if (doc.RootElement.ValueKind == JsonValueKind.String)
                            {
                                domain = doc.RootElement.GetString();
                            }
                            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                if (doc.RootElement.TryGetProperty("domainName", out var p) ||
                                    doc.RootElement.TryGetProperty("DomainName", out p) ||
                                    doc.RootElement.TryGetProperty("domain", out p) ||
                                    doc.RootElement.TryGetProperty("Domain", out p))
                                {
                                    domain = p.GetString();
                                }
                            }
                        }
                        catch
                        {
                            domain = request.PayloadJson.Trim('"', ' ');
                        }
                    }

                    if (string.IsNullOrWhiteSpace(domain))
                    {
                        return IpcResponse.Fail(request.RequestId, "Domain name is required.");
                    }

                    var readinessChecker = _serviceProvider.GetService<IDomainReadinessChecker>()
                        ?? scope.ServiceProvider.GetService<IDomainReadinessChecker>()
                        ?? new DomainReadinessChecker();

                    var result = await readinessChecker.CheckReadinessAsync(domain);
                    return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(result, JsonOptions));
                }

                default:
                    return IpcResponse.Fail(request.RequestId, $"Unknown action '{request.Action}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing IPC action '{Action}': {Message}", request.Action, ex.Message);
            return IpcResponse.Fail(request.RequestId, $"Internal service error processing '{request.Action}': {ex.Message}");
        }
    }
}
