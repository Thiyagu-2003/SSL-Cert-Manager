using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CertManager.Integrations.Iis.Challenges;

public interface IHttpChallengeServer : IDisposable, IAsyncDisposable
{
    bool IsListening { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    void RegisterToken(string token, string keyAuthz);
    void UnregisterToken(string token);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Embedded HTTP.sys ACME Challenge Listener.
/// Uses Windows HTTP.sys kernel listener on 'http://+:80/.well-known/acme-challenge/'
/// to answer ACME HTTP-01 challenges directly in memory alongside IIS with zero port conflicts.
/// </summary>
public sealed class HttpChallengeServer : IHttpChallengeServer
{
    private readonly ILogger<HttpChallengeServer> _logger;
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerLoopTask;
    private readonly object _syncLock = new();

    public bool IsListening => _listener?.IsListening ?? false;

    public HttpChallengeServer(ILogger<HttpChallengeServer>? logger = null)
    {
        _logger = logger ?? NullLogger<HttpChallengeServer>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (IsListening)
            {
                return Task.CompletedTask;
            }

            try
            {
                if (!HttpListener.IsSupported)
                {
                    _logger.LogWarning("System.Net.HttpListener is not supported on this platform. Embedded challenge server will remain inactive.");
                    return Task.CompletedTask;
                }

                _listener = new HttpListener();
                // Register HTTP.sys ACME challenge wildcard prefix on port 80
                _listener.Prefixes.Add("http://+:80/.well-known/acme-challenge/");
                _listener.Start();

                _cts = new CancellationTokenSource();
                _listenerLoopTask = Task.Run(() => ProcessRequestsAsync(_cts.Token));
                _logger.LogInformation("Embedded HTTP.sys Challenge Server started on prefix: http://+:80/.well-known/acme-challenge/");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not start embedded HTTP.sys challenge server on port 80: {Message}. File-based challenge provisioning will remain active.", ex.Message);
                try { _listener?.Close(); } catch { }
                _listener = null;
            }

            return Task.CompletedTask;
        }
    }

    public void RegisterToken(string token, string keyAuthz)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        _tokens[token.Trim()] = keyAuthz ?? string.Empty;
        _logger.LogDebug("Registered challenge token in embedded server: {Token}", token);
    }

    public void UnregisterToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        _tokens.TryRemove(token.Trim(), out _);
        _logger.LogDebug("Unregistered challenge token from embedded server: {Token}", token);
    }

    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !(_listener?.IsListening ?? false))
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error accepting HTTP challenge request.");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            string path = request.Url?.AbsolutePath ?? string.Empty;
            string token = path.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;

            // Built-in health check and diagnostic probe support
            if (string.Equals(token, "configcheck", StringComparison.OrdinalIgnoreCase))
            {
                byte[] okBytes = Encoding.UTF8.GetBytes("OK");
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = "text/plain";
                response.ContentLength64 = okBytes.Length;
                response.OutputStream.Write(okBytes, 0, okBytes.Length);
                return;
            }

            if (_tokens.TryGetValue(token, out var keyAuthz))
            {
                byte[] buffer = Encoding.UTF8.GetBytes(keyAuthz);
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = "text/plain";
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                _logger.LogInformation("Embedded HTTP challenge server successfully answered ACME challenge for token: {Token}", token);
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                byte[] notFoundBytes = Encoding.UTF8.GetBytes("Challenge token not found or expired.");
                response.ContentType = "text/plain";
                response.ContentLength64 = notFoundBytes.Length;
                response.OutputStream.Write(notFoundBytes, 0, notFoundBytes.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error handling HTTP challenge response context.");
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
                // Suppress if connection was already closed or reset by remote peer
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (_listener == null) return;

            try
            {
                _cts?.Cancel();
                if (_listener.IsListening)
                {
                    _listener.Stop();
                }
                _listener.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while stopping HTTP challenge server.");
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                _tokens.Clear();
            }
        }

        if (_listenerLoopTask != null)
        {
            try
            {
                await Task.WhenAny(_listenerLoopTask, Task.Delay(500, cancellationToken));
            }
            catch { }
            _listenerLoopTask = null;
        }

        _logger.LogInformation("Embedded HTTP.sys Challenge Server stopped.");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
