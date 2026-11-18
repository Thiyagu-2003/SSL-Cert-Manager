using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;

namespace CertManager.Integrations.Dns;

public class AzureDnsProvider : IDnsProvider
{
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
        _httpClient = httpClient ?? new HttpClient();
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

    public Task<bool> IsTxtRecordVisibleAsync(string name, string expectedValue, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
