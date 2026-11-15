using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;

namespace CertManager.Integrations.Dns;

public class CloudflareDnsProvider : IDnsProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private const string BaseUrl = "https://api.cloudflare.com/client/v4";

    public CloudflareDnsProvider(string apiToken, HttpClient? httpClient = null)
    {
        _apiToken = apiToken ?? throw new ArgumentNullException(nameof(apiToken));
        _httpClient = httpClient ?? new HttpClient();
    }

    public string ProviderName => "Cloudflare";

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var request = new HttpRequestMessage(method, $"{BaseUrl}{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public async Task<IReadOnlyList<DnsZone>> GetZonesAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/zones");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Cloudflare API error retrieving zones ({response.StatusCode}): {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var zones = new List<DnsZone>();

        if (root.TryGetProperty("result", out var resultArr) && resultArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultArr.EnumerateArray())
            {
                string id = item.GetProperty("id").GetString() ?? string.Empty;
                string name = item.GetProperty("name").GetString() ?? string.Empty;
                string status = item.GetProperty("status").GetString() ?? "unknown";
                zones.Add(new DnsZone(id, name, status));
            }
        }

        return zones;
    }

    public async Task<DnsRecord> CreateTxtRecordAsync(
        string zoneId,
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/zones/{zoneId}/dns_records");
        var payload = new
        {
            type = "TXT",
            name = name,
            content = value,
            ttl = 120
        };
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to create Cloudflare TXT record for {name} ({response.StatusCode}): {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var result = doc.RootElement.GetProperty("result");
        string recordId = result.GetProperty("id").GetString() ?? string.Empty;
        string recordName = result.GetProperty("name").GetString() ?? name;
        int ttl = result.TryGetProperty("ttl", out var ttlProp) ? ttlProp.GetInt32() : 120;

        return new DnsRecord(recordId, zoneId, recordName, "TXT", value, ttl);
    }

    public async Task DeleteTxtRecordAsync(
        string zoneId,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/zones/{zoneId}/dns_records/{recordId}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to delete Cloudflare TXT record {recordId} ({response.StatusCode}): {content}");
        }
    }

    public async Task<bool> IsTxtRecordVisibleAsync(
        string name,
        string expectedValue,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Query Cloudflare 1.1.1.1 DNS over HTTPS (DoH) API
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
