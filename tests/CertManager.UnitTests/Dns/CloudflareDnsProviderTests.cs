using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Integrations.Dns;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Dns;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}

public class CloudflareDnsProviderTests
{
    [Fact]
    public async Task GetZonesAsync_ShouldParseActiveZonesCorrectly()
    {
        var mockResponse = @"{
            ""success"": true,
            ""result"": [
                {
                    ""id"": ""zone-123"",
                    ""name"": ""example.com"",
                    ""status"": ""active""
                }
            ]
        }";

        var handler = new MockHttpMessageHandler(req =>
        {
            req.Headers.Authorization?.Parameter.Should().Be("test-token");
            req.RequestUri?.AbsolutePath.Should().EndWith("/zones");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockResponse, Encoding.UTF8, "application/json")
            };
        });

        var provider = new CloudflareDnsProvider("test-token", new HttpClient(handler));
        var zones = await provider.GetZonesAsync();

        zones.Should().HaveCount(1);
        zones[0].Id.Should().Be("zone-123");
        zones[0].Name.Should().Be("example.com");
        zones[0].Status.Should().Be("active");
    }

    [Fact]
    public async Task CreateTxtRecordAsync_ShouldSendCorrectPayloadAndReturnRecord()
    {
        var mockResponse = @"{
            ""success"": true,
            ""result"": {
                ""id"": ""rec-456"",
                ""name"": ""_acme-challenge.example.com"",
                ""content"": ""challenge-hash-123"",
                ""ttl"": 120
            }
        }";

        var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri?.AbsolutePath.Should().EndWith("/zones/zone-123/dns_records");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockResponse, Encoding.UTF8, "application/json")
            };
        });

        var provider = new CloudflareDnsProvider("test-token", new HttpClient(handler));
        var record = await provider.CreateTxtRecordAsync("zone-123", "_acme-challenge.example.com", "challenge-hash-123");

        record.Id.Should().Be("rec-456");
        record.ZoneId.Should().Be("zone-123");
        record.Name.Should().Be("_acme-challenge.example.com");
        record.Value.Should().Be("challenge-hash-123");
        record.Ttl.Should().Be(120);
    }

    [Fact]
    public async Task DeleteTxtRecordAsync_ShouldSendDeleteRequest()
    {
        bool deleteInvoked = false;
        var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Delete);
            req.RequestUri?.AbsolutePath.Should().EndWith("/zones/zone-123/dns_records/rec-456");
            deleteInvoked = true;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""success"":true}", Encoding.UTF8, "application/json")
            };
        });

        var provider = new CloudflareDnsProvider("test-token", new HttpClient(handler));
        await provider.DeleteTxtRecordAsync("zone-123", "rec-456");

        deleteInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task IsTxtRecordVisibleAsync_ShouldReturnTrueWhenRecordMatchesInDoH()
    {
        var dohResponse = @"{
            ""Status"": 0,
            ""Answer"": [
                {
                    ""name"": ""_acme-challenge.example.com"",
                    ""type"": 16,
                    ""TTL"": 60,
                    ""data"": ""\""expected-digest-value\""""
                }
            ]
        }";

        var handler = new MockHttpMessageHandler(req =>
        {
            req.RequestUri?.Host.Should().Be("cloudflare-dns.com");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(dohResponse, Encoding.UTF8, "application/json")
            };
        });

        var provider = new CloudflareDnsProvider("test-token", new HttpClient(handler));
        bool isVisible = await provider.IsTxtRecordVisibleAsync("_acme-challenge.example.com", "expected-digest-value");

        isVisible.Should().BeTrue();
    }
}
