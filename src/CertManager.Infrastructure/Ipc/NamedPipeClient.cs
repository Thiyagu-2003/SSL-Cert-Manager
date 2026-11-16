using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Ipc;

namespace CertManager.Infrastructure.Ipc;

public interface INamedPipeClient
{
    Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default);
}

public class NamedPipeClient : INamedPipeClient
{
    private readonly string _pipeName;
    private readonly string _serverName;
    private readonly int _timeoutMs;

    public NamedPipeClient(string pipeName, string serverName = ".", int timeoutMs = 15000)
    {
        _pipeName = pipeName;
        _serverName = serverName;
        _timeoutMs = timeoutMs;
    }

    public async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var clientStream = new NamedPipeClientStream(
            _serverName,
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await clientStream.ConnectAsync(_timeoutMs, cancellationToken);

        await IpcMessageFraming.WriteMessageAsync(clientStream, request, cancellationToken);
        var response = await IpcMessageFraming.ReadMessageAsync<IpcResponse>(clientStream, cancellationToken);

        return response ?? IpcResponse.Fail(request.RequestId, "No response received from service.");
    }
}
