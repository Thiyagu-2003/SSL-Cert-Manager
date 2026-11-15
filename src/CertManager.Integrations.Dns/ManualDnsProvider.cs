using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;

namespace CertManager.Integrations.Dns;

public class ManualDnsProvider : IDnsProvider
{
    private readonly HttpClient _httpClient;

    public ManualDnsProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public string ProviderName => "Manual";

    public Task<IReadOnlyList<DnsZone>> GetZonesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DnsZone>>(new List<DnsZone>
        {
            new("manual-zone", "Manual DNS Zone", "active")
        });
    }

    public Task<DnsRecord> CreateTxtRecordAsync(
        string zoneId,
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        // For manual DNS, we generate a virtual record ID
        var recordId = $"manual-{Guid.NewGuid():N}";
        return Task.FromResult(new DnsRecord(recordId, zoneId, name, "TXT", value, 300));
    }

    public Task DeleteTxtRecordAsync(
        string zoneId,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        // No-op for manual DNS cleanup
        return Task.CompletedTask;
    }

    public async Task<bool> IsTxtRecordVisibleAsync(
        string name,
        string expectedValue,
        CancellationToken cancellationToken = default)
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
                        string data = dataProp.GetString()?.Trim('"') ?? string.Empty;
                        if (string.Equals(data, expectedValue, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            // Transient lookup failure
        }

        return false;
    }
}
