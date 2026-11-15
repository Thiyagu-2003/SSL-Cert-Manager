using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Ipc;
using CertManager.Infrastructure.Ipc;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Ipc;

public class NamedPipeIpcTests
{
    [Fact]
    public async Task Framing_CanWriteAndRead_ValidMessage()
    {
        using var stream = new MemoryStream();
        var request = new IpcRequest
        {
            RequestId = Guid.NewGuid(),
            Action = "GetStatus",
            PayloadJson = "{\"test\":true}"
        };

        await IpcMessageFraming.WriteMessageAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        var readBack = await IpcMessageFraming.ReadMessageAsync<IpcRequest>(stream, CancellationToken.None);

        readBack.Should().NotBeNull();
        readBack!.RequestId.Should().Be(request.RequestId);
        readBack.Action.Should().Be("GetStatus");
        readBack.PayloadJson.Should().Be("{\"test\":true}");
    }

    [Fact]
    public async Task Framing_Rejects_OversizedMessage()
    {
        using var stream = new MemoryStream();
        // Write a 4-byte length header claiming 10 MB payload (> 4MB limit)
        byte[] fakeHeader = BitConverter.GetBytes(10 * 1024 * 1024);
        await stream.WriteAsync(fakeHeader);
        stream.Position = 0;

        Func<Task> act = async () => await IpcMessageFraming.ReadMessageAsync<IpcRequest>(stream, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*exceeds maximum allowed size*");
    }

    [Fact]
    public async Task ClientServer_CanExchangeRequestAndResponse()
    {
        string pipeName = $"CertManager_TestPipe_{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var server = new NamedPipeServer(pipeName, async (req, callerIdentity) =>
        {
            return new IpcResponse
            {
                RequestId = req.RequestId,
                Success = true,
                PayloadJson = "{\"status\":\"healthy\"}"
            };
        });

        var serverRunTask = server.StartAsync(cts.Token);

        // Give the server a brief moment to initialize the pipe
        await Task.Delay(100);

        var client = new NamedPipeClient(pipeName);
        var request = new IpcRequest
        {
            RequestId = Guid.NewGuid(),
            Action = "Ping"
        };

        var response = await client.SendAsync(request, cts.Token);

        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.PayloadJson.Should().Contain("healthy");

        cts.Cancel();
        try { await serverRunTask; } catch (OperationCanceledException) { }
    }
}
