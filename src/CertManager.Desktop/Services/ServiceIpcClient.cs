using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Entities;
using CertManager.Core.Ipc;
using CertManager.Core.Models;
using CertManager.Infrastructure.Ipc;

namespace CertManager.Desktop.Services;

public class ServiceIpcClient
{
    private const string DefaultPipeName = "CertManager.Ipc";
    private readonly NamedPipeClient _client;

    public ServiceIpcClient(string pipeName = DefaultPipeName)
    {
        _client = new NamedPipeClient(pipeName);
    }

    public async Task<ServiceStatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new IpcRequest { Action = "GetStatus" };
            var response = await _client.SendAsync(request, cancellationToken);

            if (response.Success && !string.IsNullOrEmpty(response.PayloadJson))
            {
                return JsonSerializer.Deserialize<ServiceStatusResponse>(
                    response.PayloadJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch
        {
            // Service not running or unreachable
        }

        return null;
    }

    public async Task<IReadOnlyList<DiscoveredIisSite>> GetWebsitesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new IpcRequest { Action = "GetWebsites" };
            var response = await _client.SendAsync(request, cancellationToken);

            if (response.Success && !string.IsNullOrEmpty(response.PayloadJson))
            {
                return JsonSerializer.Deserialize<List<DiscoveredIisSite>>(
                    response.PayloadJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<DiscoveredIisSite>();
            }
        }
        catch
        {
            // Service offline or error
        }

        return Array.Empty<DiscoveredIisSite>();
    }

    public async Task<IReadOnlyList<CertificateRecord>> GetCertificatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new IpcRequest { Action = "GetCertificates" };
            var response = await _client.SendAsync(request, cancellationToken);

            if (response.Success && !string.IsNullOrEmpty(response.PayloadJson))
            {
                return JsonSerializer.Deserialize<List<CertificateRecord>>(
                    response.PayloadJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<CertificateRecord>();
            }
        }
        catch
        {
            // Service offline or error
        }

        return Array.Empty<CertificateRecord>();
    }

    public async Task<IReadOnlyList<NotificationRecord>> GetNotificationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new IpcRequest { Action = "GetNotifications" };
            var response = await _client.SendAsync(request, cancellationToken);

            if (response.Success && !string.IsNullOrEmpty(response.PayloadJson))
            {
                return JsonSerializer.Deserialize<List<NotificationRecord>>(
                    response.PayloadJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<NotificationRecord>();
            }
        }
        catch
        {
            // Service offline or error
        }

        return Array.Empty<NotificationRecord>();
    }

    public async Task<CertificateRequestStatusResult?> RequestCertificateAsync(
        CertificateRequestModel requestModel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new IpcRequest
            {
                Action = "RequestCertificate",
                PayloadJson = JsonSerializer.Serialize(requestModel)
            };

            var response = await _client.SendAsync(request, cancellationToken);
            if (response.Success && !string.IsNullOrEmpty(response.PayloadJson))
            {
                return JsonSerializer.Deserialize<CertificateRequestStatusResult>(
                    response.PayloadJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            return new CertificateRequestStatusResult
            {
                Success = false,
                Step = "IPC Communication",
                ErrorMessage = response.ErrorMessage ?? "Unknown service error."
            };
        }
        catch (Exception ex)
        {
            return new CertificateRequestStatusResult
            {
                Success = false,
                Step = "Connection",
                ErrorMessage = $"Failed to communicate with CertFlow background service: {ex.Message}"
            };
        }
    }
}
