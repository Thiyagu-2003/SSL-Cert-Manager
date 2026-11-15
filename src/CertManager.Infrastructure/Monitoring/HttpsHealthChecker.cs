using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace CertManager.Infrastructure.Monitoring;

public interface IHttpsHealthChecker
{
    Task<HealthCheckResult> CheckEndpointAsync(
        string hostname,
        int port = 443,
        CancellationToken cancellationToken = default);
}

public class HttpsHealthChecker : IHttpsHealthChecker
{
    private readonly ILogger<HttpsHealthChecker> _logger;

    public HttpsHealthChecker(ILogger<HttpsHealthChecker> logger)
    {
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckEndpointAsync(
        string hostname,
        int port = 443,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return new HealthCheckResult(false, "Hostname cannot be null or empty.");
        }

        string cleanHost = hostname.Trim();
        if (cleanHost.StartsWith("*.", StringComparison.Ordinal))
        {
            cleanHost = cleanHost[2..];
        }

        string url = $"https://{cleanHost}:{port}/";
        _logger.LogInformation("Checking HTTPS health for endpoint {Url}...", url);

        X509Certificate2? capturedCert = null;
        SslPolicyErrors capturedErrors = SslPolicyErrors.None;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                if (cert != null)
                {
                    capturedCert = new X509Certificate2(cert);
                }
                capturedErrors = errors;
                return true; // Continue so we can inspect details
            }
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (capturedCert == null)
            {
                return new HealthCheckResult(false, $"No TLS certificate presented by server at {url}.");
            }

            DateTime notAfter = capturedCert.NotAfter;
            DateTime notBefore = capturedCert.NotBefore;
            var now = DateTime.UtcNow;

            if (now > notAfter)
            {
                return new HealthCheckResult(false, $"TLS Certificate expired on {notAfter:u} (Thumbprint: {capturedCert.Thumbprint}).");
            }

            if (now < notBefore)
            {
                return new HealthCheckResult(false, $"TLS Certificate is not yet valid (valid from {notBefore:u}).");
            }

            var daysRemaining = (notAfter - now).TotalDays;
            if (daysRemaining < 14)
            {
                return new HealthCheckResult(true, $"TLS Certificate is healthy but expiring soon in {daysRemaining:F1} days (expires {notAfter:yyyy-MM-dd}).");
            }

            return new HealthCheckResult(true, $"TLS Certificate is active and valid until {notAfter:yyyy-MM-dd} ({daysRemaining:F0} days remaining, Subject: {capturedCert.Subject}).");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("HTTPS request to {Url} failed: {Message}", url, ex.Message);
            return new HealthCheckResult(false, $"HTTPS endpoint unreachable or connection refused: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error checking HTTPS health for {Url}: {Message}", url, ex.Message);
            return new HealthCheckResult(false, $"Health check error: {ex.Message}");
        }
        finally
        {
            capturedCert?.Dispose();
        }
    }
}
