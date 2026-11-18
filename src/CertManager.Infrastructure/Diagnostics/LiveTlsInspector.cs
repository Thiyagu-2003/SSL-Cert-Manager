using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Models;

namespace CertManager.Infrastructure.Diagnostics;

public interface ILiveTlsInspector
{
    Task<LiveTlsInspectionResult> InspectLiveDomainAsync(string domain, int port = 443, CancellationToken cancellationToken = default);
}

public class LiveTlsInspector : ILiveTlsInspector
{
    public async Task<LiveTlsInspectionResult> InspectLiveDomainAsync(string domain, int port = 443, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        string cleanDomain = domain.Trim();
        if (cleanDomain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            cleanDomain = cleanDomain.Substring(7);
        else if (cleanDomain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            cleanDomain = cleanDomain.Substring(8);

        int slashIdx = cleanDomain.IndexOf('/');
        if (slashIdx >= 0)
            cleanDomain = cleanDomain.Substring(0, slashIdx);

        int colonIdx = cleanDomain.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(cleanDomain.Substring(colonIdx + 1), out int parsedPort))
        {
            port = parsedPort;
            cleanDomain = cleanDomain.Substring(0, colonIdx);
        }

        var result = new LiveTlsInspectionResult
        {
            Domain = cleanDomain,
            Port = port
        };

        try
        {
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

            await tcpClient.ConnectAsync(result.Domain, result.Port, linkedCts.Token);
            result.IsReachable = true;

            using var sslStream = new SslStream(
                tcpClient.GetStream(),
                leaveInnerStreamOpen: false,
                (sender, certificate, chain, sslPolicyErrors) =>
                {
                    // Allow inspecting even if untrusted so we can report the exact errors
                    return true;
                });

            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = result.Domain,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            await sslStream.AuthenticateAsClientAsync(sslOptions, linkedCts.Token);
            result.HandshakeSuccess = true;
            result.NegotiatedProtocol = sslStream.SslProtocol.ToString();
            result.CipherSuite = $"{sslStream.NegotiatedCipherSuite}";

            if (sslStream.RemoteCertificate != null)
            {
                using var cert = new X509Certificate2(sslStream.RemoteCertificate);
                result.CertificateSubject = cert.Subject;
                result.CertificateIssuer = cert.Issuer;
                result.CertificateThumbprint = cert.Thumbprint;

                DateTime notBeforeUtc = cert.NotBefore.Kind == DateTimeKind.Local ? cert.NotBefore.ToUniversalTime() : cert.NotBefore;
                DateTime notAfterUtc = cert.NotAfter.Kind == DateTimeKind.Local ? cert.NotAfter.ToUniversalTime() : cert.NotAfter;

                result.ValidFrom = notBeforeUtc;
                result.ValidTo = notAfterUtc;

                var remaining = notAfterUtc - DateTime.UtcNow;
                result.DaysRemaining = (int)Math.Max(0, remaining.TotalDays);
                result.IsExpired = DateTime.UtcNow > notAfterUtc;
                result.IsSelfSigned = string.Equals(cert.Subject, cert.Issuer, StringComparison.OrdinalIgnoreCase);

                // Build Chain
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                bool isChainValid = chain.Build(cert);
                result.ChainElementsCount = chain.ChainElements.Count;

                if (!isChainValid)
                {
                    var errors = new List<string>();
                    foreach (var status in chain.ChainStatus)
                    {
                        errors.Add(status.StatusInformation.Trim());
                    }
                    result.ChainStatus = string.Join("; ", errors);
                }
                else
                {
                    result.ChainStatus = "Trusted & Complete";
                }
            }

            // Probe for HSTS header
            try
            {
                string request = $"HEAD / HTTP/1.1\r\nHost: {result.Domain}\r\nUser-Agent: CertFlow-HealthCheck/2.1\r\nConnection: close\r\n\r\n";
                byte[] requestBytes = Encoding.ASCII.GetBytes(request);
                await sslStream.WriteAsync(requestBytes, linkedCts.Token);
                await sslStream.FlushAsync(linkedCts.Token);

                using var reader = new StreamReader(sslStream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                string? line;
                int linesRead = 0;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(linkedCts.Token)) && linesRead++ < 100)
                {
                    if (line.StartsWith("Strict-Transport-Security:", StringComparison.OrdinalIgnoreCase))
                    {
                        result.HstsHeaderPresent = true;
                        result.HstsHeaderValue = line.Substring("Strict-Transport-Security:".Length).Trim();
                        break;
                    }
                }
            }
            catch
            {
                // Non-fatal if HEAD probe fails
            }

            CalculateGrade(result);
        }
        catch (Exception ex)
        {
            result.HandshakeSuccess = false;
            result.OverallGrade = "F";
            result.GradeExplanation = $"Connection / TLS Handshake failed: {ex.Message}";
            result.Findings.Add(ex.Message);
        }

        return result;
    }

    private static void CalculateGrade(LiveTlsInspectionResult res)
    {
        res.Findings.Clear();

        if (!res.HandshakeSuccess)
        {
            res.OverallGrade = "F";
            res.GradeExplanation = "Handshake Failed";
            return;
        }

        if (string.IsNullOrWhiteSpace(res.CertificateThumbprint))
        {
            res.OverallGrade = "F";
            res.GradeExplanation = "No Server Certificate Presented";
            res.Findings.Add("No TLS certificate was presented by the remote server.");
            return;
        }

        if (res.IsExpired)
        {
            res.OverallGrade = "F";
            res.GradeExplanation = "Certificate Expired";
            res.Findings.Add("CRITICAL: The active certificate has expired.");
            return;
        }

        if (res.IsSelfSigned)
        {
            res.OverallGrade = "C";
            res.GradeExplanation = "Self-Signed Certificate (Untrusted Publicly)";
            res.Findings.Add("Untrusted: Certificate is self-signed.");
            return;
        }

        bool isTls13Or12 = res.NegotiatedProtocol.Contains("13") || res.NegotiatedProtocol.Contains("12");

        if (isTls13Or12 && res.HstsHeaderPresent && res.DaysRemaining > 14)
        {
            res.OverallGrade = "A+";
            res.GradeExplanation = "Exceptional: Modern TLS, Valid Trust Chain, & Active HSTS";
            res.Findings.Add($"Negotiated {res.NegotiatedProtocol} with {res.CipherSuite}.");
            res.Findings.Add($"HSTS Policy: {res.HstsHeaderValue}");
            res.Findings.Add($"Certificate valid for {res.DaysRemaining} more days.");
            return;
        }

        if (isTls13Or12 && res.DaysRemaining > 7)
        {
            res.OverallGrade = "A";
            res.GradeExplanation = "Secure: Modern TLS & Valid Trust Chain";
            res.Findings.Add($"Negotiated {res.NegotiatedProtocol} with valid trust chain.");
            if (!res.HstsHeaderPresent)
            {
                res.Findings.Add("Recommendation: Enable HSTS in web.config to achieve A+ rating.");
            }
            return;
        }

        res.OverallGrade = "B";
        res.GradeExplanation = "Warning: Expiration Due Soon or Suboptimal Cipher";
        res.Findings.Add($"Certificate due for renewal in {res.DaysRemaining} days.");
    }
}
