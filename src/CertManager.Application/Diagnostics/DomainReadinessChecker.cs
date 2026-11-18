using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CertManager.Core.Models;

namespace CertManager.Application.Diagnostics;

public class DomainReadinessResult : CertManager.Core.Models.DomainReadinessResult
{
}

public interface IDomainReadinessChecker
{
    Task<DomainReadinessResult> CheckReadinessAsync(string domainName, CancellationToken cancellationToken = default);
}

public interface IDomainReadinessProbe
{
    Task<IPAddress[]> ResolveDnsAsync(string domainName, CancellationToken cancellationToken);
    Task<string?> GetPublicIpAsync(CancellationToken cancellationToken);
    Task<bool> CheckPort80Async(string host, int port, CancellationToken cancellationToken);
    Task<(bool Success, int StatusCode, string StatusMessage)> ProbeChallengeEndpointAsync(string requestUri, string hostHeader, CancellationToken cancellationToken);
    IEnumerable<string> GetLocalIpAddresses();
}

public class DefaultDomainReadinessProbe : IDomainReadinessProbe, IDisposable
{
    private static readonly HttpClient DefaultSharedHttpClient = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public DefaultDomainReadinessProbe(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient == null;
        _httpClient = httpClient ?? DefaultSharedHttpClient;
    }

    public async Task<IPAddress[]> ResolveDnsAsync(string domainName, CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(domainName, cancellationToken);
    }

    public async Task<string?> GetPublicIpAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        // Try api.ipify.org
        try
        {
            var ip = (await _httpClient.GetStringAsync("https://api.ipify.org", cts.Token)).Trim();
            if (IPAddress.TryParse(ip, out _))
            {
                return ip;
            }
        }
        catch
        {
            // Fallback to icanhazip
        }

        // Fallback to icanhazip.com
        try
        {
            var ip = (await _httpClient.GetStringAsync("https://icanhazip.com", cts.Token)).Trim();
            if (IPAddress.TryParse(ip, out _))
            {
                return ip;
            }
        }
        catch
        {
            // Offline or unreachable
        }

        return null;
    }

    public async Task<bool> CheckPort80Async(string host, int port, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await tcpClient.ConnectAsync(host, port, cts.Token);
            return tcpClient.Connected;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool Success, int StatusCode, string StatusMessage)> ProbeChallengeEndpointAsync(
        string requestUri,
        string hostHeader,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!string.IsNullOrWhiteSpace(hostHeader))
            {
                request.Headers.Host = hostHeader;
            }

            using var response = await _httpClient.SendAsync(request, cts.Token);
            bool ok = response.IsSuccessStatusCode;
            return (ok, (int)response.StatusCode, response.ReasonPhrase ?? (ok ? "OK" : "Error"));
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    public IEnumerable<string> GetLocalIpAddresses()
    {
        var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "::1" };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address != null)
                    {
                        list.Add(addr.Address.ToString());
                    }
                }
            }
        }
        catch
        {
            // Ignore network enumeration issues
        }

        return list;
    }

    public void Dispose()
    {
        if (_ownsClient && _httpClient != DefaultSharedHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

public class DomainReadinessChecker : IDomainReadinessChecker
{
    private readonly IDomainReadinessProbe _probe;
    private readonly ILogger<DomainReadinessChecker> _logger;

    public DomainReadinessChecker()
        : this(new DefaultDomainReadinessProbe(), null)
    {
    }

    public DomainReadinessChecker(ILogger<DomainReadinessChecker>? logger)
        : this(new DefaultDomainReadinessProbe(), logger)
    {
    }

    public DomainReadinessChecker(IDomainReadinessProbe probe, ILogger<DomainReadinessChecker>? logger = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger ?? NullLogger<DomainReadinessChecker>.Instance;
    }

    public async Task<DomainReadinessResult> CheckReadinessAsync(string domainName, CancellationToken cancellationToken = default)
    {
        var result = new DomainReadinessResult();

        if (string.IsNullOrWhiteSpace(domainName))
        {
            result.OverallReady = false;
            result.DomainName = string.Empty;
            result.DnsStatusMessage = "Domain name cannot be empty.";
            result.Port80StatusMessage = "Not checked.";
            result.ChallengeStatusMessage = "Not checked.";
            result.Recommendations.Add("Please enter a valid domain name.");
            return result;
        }

        // Normalize domain input
        string cleanDomain = domainName.Trim();
        if (cleanDomain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            cleanDomain = cleanDomain["http://".Length..];
        }
        else if (cleanDomain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            cleanDomain = cleanDomain["https://".Length..];
        }

        int slashIdx = cleanDomain.IndexOf('/');
        if (slashIdx >= 0)
        {
            cleanDomain = cleanDomain[..slashIdx];
        }

        int colonIdx = cleanDomain.IndexOf(':');
        if (colonIdx >= 0)
        {
            cleanDomain = cleanDomain[..colonIdx];
        }

        cleanDomain = cleanDomain.TrimEnd('.');
        result.DomainName = cleanDomain;

        bool isWildcard = cleanDomain.StartsWith("*.", StringComparison.Ordinal);
        string dnsLookupHost = isWildcard ? cleanDomain[2..] : cleanDomain;

        if (isWildcard)
        {
            result.Recommendations.Add("Wildcard domains (*.example.com) require DNS-01 validation. Let's Encrypt HTTP-01 challenge cannot validate wildcard domains.");
        }

        // 1. Discover public IP (lightweight query with 3-second timeout, falling back gracefully if offline)
        try
        {
            var publicIp = await _probe.GetPublicIpAsync(cancellationToken);
            result.PublicIp = publicIp ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Public IP lookup encountered an error.");
            result.PublicIp = string.Empty;
        }

        // 2. Resolve DNS for domainName
        try
        {
            var addresses = await _probe.ResolveDnsAsync(dnsLookupHost, cancellationToken);
            result.ResolvedIps = addresses.Select(a => a.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS resolution failed for host {Host}", dnsLookupHost);
            result.DnsStatusMessage = $"DNS resolution failed for '{cleanDomain}': {ex.Message}";
            result.Recommendations.Add($"Verify that '{cleanDomain}' is registered and has valid DNS A/AAAA records created at your DNS provider.");
        }

        // 3. Check if any resolved IP matches the public IP or local network interfaces
        if (result.ResolvedIps.Count > 0)
        {
            var localIps = _probe.GetLocalIpAddresses().ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool matchesPublic = !string.IsNullOrEmpty(result.PublicIp) &&
                result.ResolvedIps.Any(ip => string.Equals(ip, result.PublicIp, StringComparison.OrdinalIgnoreCase));
            bool matchesLocal = result.ResolvedIps.Any(ip => localIps.Contains(ip));

            if (matchesPublic)
            {
                result.DnsMatchesPublicIp = true;
                result.DnsStatusMessage = $"Domain '{cleanDomain}' resolves to public IP {result.PublicIp}.";
            }
            else if (matchesLocal)
            {
                result.DnsMatchesPublicIp = true;
                result.DnsStatusMessage = $"Domain '{cleanDomain}' resolves to a local server interface IP ({string.Join(", ", result.ResolvedIps)}).";
            }
            else if (!string.IsNullOrEmpty(result.PublicIp))
            {
                result.DnsMatchesPublicIp = false;
                result.DnsStatusMessage = $"Domain resolves to {string.Join(", ", result.ResolvedIps)}, which does not match public IP ({result.PublicIp}) or local server interfaces.";
                result.Recommendations.Add($"Update your DNS A record for '{cleanDomain}' to point to your current public IP ({result.PublicIp}).");
            }
            else
            {
                result.DnsMatchesPublicIp = false;
                result.DnsStatusMessage = $"Domain resolves to {string.Join(", ", result.ResolvedIps)}, but public IP could not be determined.";
                result.Recommendations.Add($"Ensure your public IP matches the domain's resolved IP ({string.Join(", ", result.ResolvedIps)}).");
            }
        }
        else if (string.IsNullOrEmpty(result.DnsStatusMessage))
        {
            result.DnsMatchesPublicIp = false;
            result.DnsStatusMessage = $"No IP addresses were resolved for '{cleanDomain}'.";
            result.Recommendations.Add("Configure DNS A/AAAA records pointing to your server's public IP address.");
        }

        // 4. Check if port 80 is listening on localhost (TcpClient connection to 127.0.0.1:80 with 2-second timeout)
        try
        {
            bool port80Open = await _probe.CheckPort80Async("127.0.0.1", 80, cancellationToken);
            result.Port80Open = port80Open;
            if (port80Open)
            {
                result.Port80StatusMessage = "Port 80 is open and listening on localhost (127.0.0.1:80).";
            }
            else
            {
                result.Port80StatusMessage = "Port 80 is closed or not accepting connections on localhost.";
                result.Recommendations.Add("Ensure IIS or the CertFlow Challenge Server is running and port 80 binding is active.");
            }
        }
        catch (Exception ex)
        {
            result.Port80Open = false;
            result.Port80StatusMessage = $"Port 80 check failed: {ex.Message}";
            result.Recommendations.Add("Ensure IIS or the CertFlow Challenge Server is running and port 80 binding is active.");
        }

        // 5. Check challenge handler: simulate a GET probe to http://127.0.0.1/.well-known/acme-challenge/configcheck with Host: domainName
        try
        {
            var probeResult = await _probe.ProbeChallengeEndpointAsync(
                "http://127.0.0.1/.well-known/acme-challenge/configcheck",
                cleanDomain,
                cancellationToken);

            result.ChallengeEndpointResponsive = probeResult.Success;
            if (probeResult.Success)
            {
                result.ChallengeStatusMessage = "Challenge diagnostic probe responded successfully (HTTP 200 OK).";
            }
            else
            {
                result.ChallengeStatusMessage = probeResult.StatusCode > 0
                    ? $"Challenge probe returned HTTP {probeResult.StatusCode} ({probeResult.StatusMessage})."
                    : $"Challenge probe failed to connect: {probeResult.StatusMessage}";
                result.Recommendations.Add("Verify that the ACME HTTP-01 challenge responder is enabled and requests to '/.well-known/acme-challenge/' are not blocked or redirected.");
            }
        }
        catch (Exception ex)
        {
            result.ChallengeEndpointResponsive = false;
            result.ChallengeStatusMessage = $"Challenge probe failed: {ex.Message}";
            result.Recommendations.Add("Ensure port 80 challenge handler is configured and accessible locally.");
        }

        // 6. Compute OverallReady and populate helpful recommendations
        result.OverallReady = (result.ResolvedIps.Count > 0)
            && result.DnsMatchesPublicIp
            && result.Port80Open
            && result.ChallengeEndpointResponsive;

        if (!result.OverallReady)
        {
            if (result.Port80Open && !result.Recommendations.Any(r => r.Contains("Forward port 80", StringComparison.OrdinalIgnoreCase)))
            {
                result.Recommendations.Add("Forward port 80 in your router/firewall to this server's local IP address so external Let's Encrypt validation requests can reach your server.");
            }
        }

        result.Recommendations = result.Recommendations.Distinct().ToList();

        return result;
    }
}
