using System;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Ipc;

namespace CertManager.Infrastructure.Ipc;

public delegate Task<IpcResponse> IpcRequestHandler(IpcRequest request, WindowsIdentity? callerIdentity);

public interface INamedPipeServer
{
    Task StartAsync(CancellationToken cancellationToken);
}

public class NamedPipeServer : INamedPipeServer
{
    private readonly string _pipeName;
    private readonly IpcRequestHandler _handler;

    public NamedPipeServer(string pipeName, IpcRequestHandler handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                serverStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await serverStream.WaitForConnectionAsync(cancellationToken);

                // Handle client in background task so server can immediately accept next connection
                _ = HandleClientConnectionAsync(serverStream, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                serverStream?.Dispose();
                break;
            }
            catch
            {
                serverStream?.Dispose();
                // Brief throttle to avoid tight loop on persistent OS pipe error
                await Task.Delay(200, cancellationToken);
            }
        }
    }

    private async Task HandleClientConnectionAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        using (stream)
        {
            try
            {
                WindowsIdentity? callerIdentity = null;
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        stream.RunAsClient(() =>
                        {
                            callerIdentity = WindowsIdentity.GetCurrent();
                        });
                    }
                }
                catch
                {
                    // If client impersonation is unavailable, callerIdentity remains null
                }

                var request = await IpcMessageFraming.ReadMessageAsync<IpcRequest>(stream, cancellationToken);
                if (request == null) return;

                IpcResponse response;
                try
                {
                    response = await _handler(request, callerIdentity);
                }
                catch (Exception ex)
                {
                    response = IpcResponse.Fail(request.RequestId, $"Internal server error: {ex.Message}");
                }

                await IpcMessageFraming.WriteMessageAsync(stream, response, cancellationToken);
            }
            catch
            {
                // Connection or communication error with client
            }
        }
    }
}
