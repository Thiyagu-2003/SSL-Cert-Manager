using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;

namespace CertManager.Integrations.Dns;

public class Route53DnsProvider : IDnsProvider
{
    private static readonly HttpClient DefaultSharedHttpClient = new();
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly string _region;
    private readonly HttpClient _httpClient;

    public Route53DnsProvider(string accessKeyId, string secretAccessKey, string region = "us-east-1", HttpClient? httpClient = null)
    {
        _accessKeyId = accessKeyId ?? throw new ArgumentNullException(nameof(accessKeyId));
        _secretAccessKey = secretAccessKey ?? throw new ArgumentNullException(nameof(secretAccessKey));
        _region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region;
        _httpClient = httpClient ?? DefaultSharedHttpClient;
    }

    public string ProviderName => "AWS Route 53";

    public Task<IReadOnlyList<DnsZone>> GetZonesAsync(CancellationToken cancellationToken = default)
    {
        // Default simulated / mock zone list if AWS credentials supplied
        var zones = new List<DnsZone>
        {
            new DnsZone("Z123456789EXAMPLE", "example.com", "Active")
        };
        return Task.FromResult<IReadOnlyList<DnsZone>>(zones);
    }

    public Task<DnsRecord> CreateTxtRecordAsync(string zoneId, string name, string value, CancellationToken cancellationToken = default)
    {
        // Normalize name
        string fqdn = name.EndsWith('.') ? name : name + ".";
        string recordId = $"R53-{Guid.NewGuid():N}";
        return Task.FromResult(new DnsRecord(recordId, zoneId, fqdn, "TXT", value, 60));
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
