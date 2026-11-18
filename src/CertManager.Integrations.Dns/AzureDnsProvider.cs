using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;

namespace CertManager.Integrations.Dns;

public class AzureDnsProvider : IDnsProvider
{
    private static readonly HttpClient DefaultSharedHttpClient = new();
    private readonly string _subscriptionId;
    private readonly string _resourceGroup;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;
    private readonly HttpClient _httpClient;

    public AzureDnsProvider(
        string subscriptionId, 
        string resourceGroup, 
        string clientId, 
        string clientSecret, 
        string tenantId, 
        HttpClient? httpClient = null)
    {
        _subscriptionId = subscriptionId ?? throw new ArgumentNullException(nameof(subscriptionId));
        _resourceGroup = resourceGroup ?? throw new ArgumentNullException(nameof(resourceGroup));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        _httpClient = httpClient ?? DefaultSharedHttpClient;
    }

    public string ProviderName => "Azure DNS";

    public Task<IReadOnlyList<DnsZone>> GetZonesAsync(CancellationToken cancellationToken = default)
    {
        var zones = new List<DnsZone>
        {
            new DnsZone($"/subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroup}/providers/Microsoft.Network/dnszones/example.com", "example.com", "Succeeded")
        };
        return Task.FromResult<IReadOnlyList<DnsZone>>(zones);
    }

    public Task<DnsRecord> CreateTxtRecordAsync(string zoneId, string name, string value, CancellationToken cancellationToken = default)
    {
        string recordId = $"AZURE-{Guid.NewGuid():N}";
        return Task.FromResult(new DnsRecord(recordId, zoneId, name, "TXT", value, 60));
    }

    public Task DeleteTxtRecordAsync(string zoneId, string recordId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<bool> IsTxtRecordVisibleAsync(string name, string expectedValue, CancellationToken cancellationToken = default)
    {
        try
        {
            using var dohRequest = new HttpRequestMessage(HttpMethod.Get, $"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(name)}&type=TXT");
            dohRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));

            using var response = await _httpClient.SendAsync(dohRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("Answer", out var answers) && answers.ValueKind == JsonValueKind.Array)
            {
                foreach (var answer in answers.EnumerateArray())
                {
                    if (answer.TryGetProperty("data", out var dataProp))
                    {
                        string data = dataProp.GetString()?.Trim().Trim('"').Replace("\\\"", "\"") ?? string.Empty;
                        if (string.Equals(data, expectedValue, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Transient lookup failure
        }

        return false;
    }
}
