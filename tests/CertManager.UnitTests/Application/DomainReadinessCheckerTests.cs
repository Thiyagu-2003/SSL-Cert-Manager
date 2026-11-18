using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Application.Diagnostics;
using CertManager.Core.Ipc;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Application;

public class DomainReadinessCheckerTests
{
    private class FakeDomainReadinessProbe : IDomainReadinessProbe
    {
        public Func<string, CancellationToken, Task<IPAddress[]>>? ResolveDnsFunc { get; set; }
        public Func<CancellationToken, Task<string?>>? GetPublicIpFunc { get; set; }
        public Func<string, int, CancellationToken, Task<bool>>? CheckPort80Func { get; set; }
        public Func<string, string, CancellationToken, Task<(bool Success, int StatusCode, string StatusMessage)>>? ProbeChallengeFunc { get; set; }
        public Func<IEnumerable<string>>? GetLocalIpAddressesFunc { get; set; }

        public Task<IPAddress[]> ResolveDnsAsync(string domainName, CancellationToken cancellationToken)
        {
            if (ResolveDnsFunc != null)
            {
                return ResolveDnsFunc(domainName, cancellationToken);
            }
            return Task.FromResult(new[] { IPAddress.Parse("203.0.113.1") });
        }

        public Task<string?> GetPublicIpAsync(CancellationToken cancellationToken)
        {
            if (GetPublicIpFunc != null)
            {
                return GetPublicIpFunc(cancellationToken);
            }
            return Task.FromResult<string?>("203.0.113.1");
        }

        public Task<bool> CheckPort80Async(string host, int port, CancellationToken cancellationToken)
        {
            if (CheckPort80Func != null)
            {
                return CheckPort80Func(host, port, cancellationToken);
            }
            return Task.FromResult(true);
        }

        public Task<(bool Success, int StatusCode, string StatusMessage)> ProbeChallengeEndpointAsync(
            string requestUri,
            string hostHeader,
            CancellationToken cancellationToken)
        {
            if (ProbeChallengeFunc != null)
            {
                return ProbeChallengeFunc(requestUri, hostHeader, cancellationToken);
            }
            return Task.FromResult((true, 200, "OK"));
        }

        public IEnumerable<string> GetLocalIpAddresses()
        {
            if (GetLocalIpAddressesFunc != null)
            {
                return GetLocalIpAddressesFunc();
            }
            return new[] { "127.0.0.1", "::1" };
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CheckReadinessAsync_ReturnsNotReady_WhenDomainIsNullOrWhitespace(string? domain)
    {
        var probe = new FakeDomainReadinessProbe();
        var checker = new DomainReadinessChecker(probe);

        var result = await checker.CheckReadinessAsync(domain!);

        result.Should().NotBeNull();
        result.OverallReady.Should().BeFalse();
        result.DnsStatusMessage.Should().Contain("Domain name");
        result.Recommendations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CheckReadinessAsync_Success_WhenDnsMatchesPublicIp_Port80Open_AndChallengeResponsive()
    {
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.42") }),
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.42"),
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((true, 200, "OK"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("test.example.com");

        result.OverallReady.Should().BeTrue();
        result.DomainName.Should().Be("test.example.com");
        result.PublicIp.Should().Be("203.0.113.42");
        result.ResolvedIps.Should().ContainSingle().Which.Should().Be("203.0.113.42");
        result.DnsMatchesPublicIp.Should().BeTrue();
        result.DnsStatusMessage.Should().Contain("203.0.113.42");
        result.Port80Open.Should().BeTrue();
        result.Port80StatusMessage.Should().Contain("Port 80 is open");
        result.ChallengeEndpointResponsive.Should().BeTrue();
        result.ChallengeStatusMessage.Should().Contain("HTTP 200");
    }

    [Fact]
    public async Task CheckReadinessAsync_Success_WhenDnsMatchesLocalServerInterface()
    {
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) => Task.FromResult(new[] { IPAddress.Parse("192.168.1.150") }),
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.99"),
            GetLocalIpAddressesFunc = () => new[] { "127.0.0.1", "192.168.1.150" },
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((true, 200, "OK"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("internal.example.local");

        result.OverallReady.Should().BeTrue();
        result.DnsMatchesPublicIp.Should().BeTrue();
        result.DnsStatusMessage.Should().Contain("local server interface");
    }

    [Fact]
    public async Task CheckReadinessAsync_Fails_WhenDnsDoesNotMatchPublicIpOrLocalInterfaces()
    {
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) => Task.FromResult(new[] { IPAddress.Parse("198.51.100.22") }),
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.50"),
            GetLocalIpAddressesFunc = () => new[] { "127.0.0.1" },
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((true, 200, "OK"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("mismatch.example.com");

        result.OverallReady.Should().BeFalse();
        result.DnsMatchesPublicIp.Should().BeFalse();
        result.DnsStatusMessage.Should().Contain("does not match");
        result.Recommendations.Should().Contain(r => r.Contains("Update your DNS A record") && r.Contains("203.0.113.50"));
    }

    [Fact]
    public async Task CheckReadinessAsync_Fails_WhenDnsResolutionThrowsException()
    {
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) => throw new SocketException((int)SocketError.HostNotFound),
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.1"),
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((true, 200, "OK"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("nonexistent.example.org");

        result.OverallReady.Should().BeFalse();
        result.ResolvedIps.Should().BeEmpty();
        result.DnsMatchesPublicIp.Should().BeFalse();
        result.DnsStatusMessage.Should().Contain("DNS resolution failed");
        result.Recommendations.Should().Contain(r => r.Contains("registered and has valid DNS A/AAAA records"));
    }

    [Fact]
    public async Task CheckReadinessAsync_Fails_WhenPort80IsClosed()
    {
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.1") }),
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.1"),
            CheckPort80Func = (h, p, ct) => Task.FromResult(false),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((true, 200, "OK"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("closedport.example.com");

        result.OverallReady.Should().BeFalse();
        result.Port80Open.Should().BeFalse();
        result.Port80StatusMessage.Should().Contain("closed");
        result.Recommendations.Should().Contain(r => r.Contains("Ensure IIS or the CertFlow Challenge Server is running"));
    }

    [Fact]
    public async Task CheckReadinessAsync_Fails_WhenChallengeEndpointFails()
    {
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.1") }),
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.1"),
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((false, 404, "Not Found"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("badchallenge.example.com");

        result.OverallReady.Should().BeFalse();
        result.ChallengeEndpointResponsive.Should().BeFalse();
        result.ChallengeStatusMessage.Should().Contain("404");
        result.Recommendations.Should().Contain(r => r.Contains("Verify that the ACME HTTP-01 challenge responder is enabled"));
    }

    [Fact]
    public async Task CheckReadinessAsync_Fails_WhenChallengeEndpointConnectionFails()
    {
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.1") }),
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.1"),
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((false, 0, "No connection could be made"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("refused.example.com");

        result.OverallReady.Should().BeFalse();
        result.ChallengeEndpointResponsive.Should().BeFalse();
        result.ChallengeStatusMessage.Should().Contain("No connection could be made");
    }

    [Fact]
    public async Task CheckReadinessAsync_NormalizesUrl_PrefixesAndPorts()
    {
        string? passedHostHeader = null;
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) =>
            {
                d.Should().Be("sub.example.com");
                return Task.FromResult(new[] { IPAddress.Parse("203.0.113.1") });
            },
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.1"),
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) =>
            {
                passedHostHeader = h;
                return Task.FromResult((true, 200, "OK"));
            }
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("https://sub.example.com:443/some/path");

        result.OverallReady.Should().BeTrue();
        result.DomainName.Should().Be("sub.example.com");
        passedHostHeader.Should().Be("sub.example.com");
    }

    [Fact]
    public async Task CheckReadinessAsync_WildcardDomain_AddsWildcardRecommendation()
    {
        string? dnsQueriedHost = null;
        var probe = new FakeDomainReadinessProbe
        {
            ResolveDnsFunc = (d, ct) =>
            {
                dnsQueriedHost = d;
                return Task.FromResult(new[] { IPAddress.Parse("203.0.113.1") });
            },
            GetPublicIpFunc = ct => Task.FromResult<string?>("203.0.113.1"),
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((true, 200, "OK"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("*.wildcard.example.com");

        dnsQueriedHost.Should().Be("wildcard.example.com");
        result.DomainName.Should().Be("*.wildcard.example.com");
        result.Recommendations.Should().Contain(r => r.Contains("Wildcard domains") && r.Contains("DNS-01"));
    }

    [Fact]
    public async Task CheckReadinessAsync_GracefullyHandlesOfflinePublicIp()
    {
        var probe = new FakeDomainReadinessProbe
        {
            GetPublicIpFunc = ct => Task.FromResult<string?>(null),
            ResolveDnsFunc = (d, ct) => Task.FromResult(new[] { IPAddress.Parse("198.51.100.1") }),
            CheckPort80Func = (h, p, ct) => Task.FromResult(true),
            ProbeChallengeFunc = (u, h, ct) => Task.FromResult((true, 200, "OK"))
        };

        var checker = new DomainReadinessChecker(probe);
        var result = await checker.CheckReadinessAsync("offline-ip.example.com");

        result.Should().NotBeNull();
        result.PublicIp.Should().BeEmpty();
        result.DnsMatchesPublicIp.Should().BeFalse();
        result.OverallReady.Should().BeFalse();
        result.DnsStatusMessage.Should().Contain("public IP could not be determined");
    }

    [Fact]
    public void DomainReadinessResult_SerializesAndDeserializes_Correctly()
    {
        var original = new DomainReadinessResult
        {
            OverallReady = true,
            DomainName = "my.domain.com",
            PublicIp = "1.2.3.4",
            ResolvedIps = new List<string> { "1.2.3.4" },
            DnsMatchesPublicIp = true,
            DnsStatusMessage = "DNS OK",
            Port80Open = true,
            Port80StatusMessage = "Port 80 OK",
            ChallengeEndpointResponsive = true,
            ChallengeStatusMessage = "Challenge OK",
            Recommendations = new List<string> { "None" }
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DomainReadinessResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        deserialized.Should().NotBeNull();
        deserialized!.OverallReady.Should().Be(original.OverallReady);
        deserialized.DomainName.Should().Be(original.DomainName);
        deserialized.PublicIp.Should().Be(original.PublicIp);
        deserialized.ResolvedIps.Should().Equal(original.ResolvedIps);
        deserialized.DnsMatchesPublicIp.Should().Be(original.DnsMatchesPublicIp);
        deserialized.DnsStatusMessage.Should().Be(original.DnsStatusMessage);
        deserialized.Port80Open.Should().Be(original.Port80Open);
        deserialized.Port80StatusMessage.Should().Be(original.Port80StatusMessage);
        deserialized.ChallengeEndpointResponsive.Should().Be(original.ChallengeEndpointResponsive);
        deserialized.ChallengeStatusMessage.Should().Be(original.ChallengeStatusMessage);
        deserialized.Recommendations.Should().Equal(original.Recommendations);
    }
}
