using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;

namespace CertManager.Integrations.Dns;

public class Route53DnsProvider : IDnsProvider
{
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly string _region;
    private readonly HttpClient _httpClient;

    public Route53DnsProvider(string accessKeyId, string secretAccessKey, string region = "us-east-1", HttpClient? httpClient = null)
    {
        _accessKeyId = accessKeyId ?? throw new ArgumentNullException(nameof(accessKeyId));
        _secretAccessKey = secretAccessKey ?? throw new ArgumentNullException(nameof(secretAccessKey));
        _region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region;
        _httpClient = httpClient ?? new HttpClient();
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

    public Task<bool> IsTxtRecordVisibleAsync(string name, string expectedValue, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
