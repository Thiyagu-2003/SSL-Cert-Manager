using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Ipc;
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
}
