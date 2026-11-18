using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Ipc;
using CertManager.Core.Models;
using CertManager.Infrastructure.Ipc;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Ipc;

public class DomainReadinessIpcTests
{
    [Fact]
    public async Task IpcPipe_TestDomainReadiness_ReturnsExpectedResult()
    {
        string pipeName = $"CertManager_TestPipe_Readiness_{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var expectedResult = new DomainReadinessResult
        {
            OverallReady = true,
            DomainName = "sample.example.com",
            PublicIp = "203.0.113.1",
            ResolvedIps = new List<string> { "203.0.113.1" },
            DnsMatchesPublicIp = true,
            DnsStatusMessage = "DNS OK",
            Port80Open = true,
            Port80StatusMessage = "Port 80 OK",
            ChallengeEndpointResponsive = true,
            ChallengeStatusMessage = "Challenge OK",
            Recommendations = new List<string>()
        };

        var server = new NamedPipeServer(pipeName, async (req, callerIdentity) =>
        {
            if (req.Action == "TestDomainReadiness")
            {
                return new IpcResponse
                {
                    RequestId = req.RequestId,
                    Success = true,
                    PayloadJson = JsonSerializer.Serialize(expectedResult)
                };
            }

            return new IpcResponse
            {
                RequestId = req.RequestId,
                Success = false,
                ErrorMessage = "Unknown action"
            };
        });

        var serverTask = server.StartAsync(cts.Token);
        await Task.Delay(100);

        var client = new NamedPipeClient(pipeName);
        var request = new IpcRequest
        {
            Action = "TestDomainReadiness",
            PayloadJson = JsonSerializer.Serialize("sample.example.com")
        };

        var response = await client.SendAsync(request, cts.Token);
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.PayloadJson.Should().NotBeNullOrEmpty();

        var result = JsonSerializer.Deserialize<DomainReadinessResult>(response.PayloadJson!, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.OverallReady.Should().BeTrue();
        result.DomainName.Should().Be("sample.example.com");
        result.PublicIp.Should().Be("203.0.113.1");
        result.DnsMatchesPublicIp.Should().BeTrue();
        result.Port80Open.Should().BeTrue();
        result.ChallengeEndpointResponsive.Should().BeTrue();

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
}
