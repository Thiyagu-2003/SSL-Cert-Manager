using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using CertManager.Core.Common;

namespace CertManager.Acme.Clients;

public record AcmeChallengeItem(
    string Domain,
    string Token,
    string KeyAuthorization,
    string DnsTxtValue,
    string ChallengeType, // "http-01" or "dns-01"
    IChallengeContext ChallengeContext);

public record AriRenewalWindow(DateTime Start, DateTime End);
public record AriRenewalInfo(AriRenewalWindow SuggestedWindow, string? ExplanationUrl);

public interface IAcmeCertificateClient
{
    Task<string> InitializeAccountAsync(string email, bool isStaging, CancellationToken cancellationToken = default);
    Task<string> InitializeAccountWithDirectoryAsync(string email, Uri directoryUri, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcmeChallengeItem>> CreateOrderAndGetChallengesAsync(
        IReadOnlyList<string> domains, 
        string validationMethod = "Http01", 
        CancellationToken cancellationToken = default);
    Task<bool> SubmitChallengeAsync(AcmeChallengeItem challengeItem, CancellationToken cancellationToken = default);
    Task<byte[]> FinalizeOrderAndExportPfxAsync(string friendlyName, string password, IReadOnlyList<string>? sanDomains = null, string keyAlgorithm = "RS256", CancellationToken cancellationToken = default);
    Task<AriRenewalInfo?> GetRenewalInfoAsync(string certificateSerialOrId, CancellationToken cancellationToken = default) => Task.FromResult<AriRenewalInfo?>(null);
}

public class CertesAcmeClient : IAcmeCertificateClient
{
    private static readonly HttpClient SharedHttpClient = new();
    private AcmeContext? _context;
    private IOrderContext? _currentOrder;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly ConcurrentDictionary<string, (AcmeContext Context, string Location)> _accountCache = new();

    public Task<string> InitializeAccountAsync(string email, bool isStaging, CancellationToken cancellationToken = default)
    {
        var directoryUri = isStaging 
            ? WellKnownServers.LetsEncryptStagingV2 
            : WellKnownServers.LetsEncryptV2;
        return InitializeAccountWithDirectoryAsync(email, directoryUri, cancellationToken);
    }

    public async Task<string> InitializeAccountWithDirectoryAsync(string email, Uri directoryUri, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            string cacheKey = $"{email.Trim().ToLowerInvariant()}@{directoryUri}";
            if (_accountCache.TryGetValue(cacheKey, out var cached))
            {
                _context = cached.Context;
                return cached.Location;
            }

            _context = new AcmeContext(directoryUri);
            var account = await _context.NewAccount(email, true);
            string location = account.Location.ToString();

            _accountCache[cacheKey] = (_context, location);
            return location;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<IReadOnlyList<AcmeChallengeItem>> CreateOrderAndGetChallengesAsync(
        IReadOnlyList<string> domains,
        string validationMethod = "Http01",
        CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            if (_context == null)
            {
                throw new InvalidOperationException("ACME client account is not initialized. Call InitializeAccountAsync first.");
            }

            _currentOrder = await _context.NewOrder(domains.ToList());
            var authorizations = await _currentOrder.Authorizations();
            var challengeItems = new List<AcmeChallengeItem>();
            bool isDns = string.Equals(validationMethod, "Dns01", StringComparison.OrdinalIgnoreCase) ||
                         domains.Any(d => d.StartsWith("*.", StringComparison.Ordinal));

            foreach (var authz in authorizations)
            {
                var authzResource = await authz.Resource();
                string domain = authzResource.Identifier.Value;

                // RFC 8555: If authorization is already valid (authz reuse), no challenge needed
                if (authzResource.Status == AuthorizationStatus.Valid)
                {
                    continue;
                }

                if (isDns)
                {
                    var dnsChallenge = await authz.Dns();
                    if (dnsChallenge != null)
                    {
                        string dnsTxt = DnsChallengeHelper.ComputeDnsTxtValue(dnsChallenge.KeyAuthz);
                        challengeItems.Add(new AcmeChallengeItem(
                            domain,
                            dnsChallenge.Token,
                            dnsChallenge.KeyAuthz,
                            dnsTxt,
                            "dns-01",
                            dnsChallenge
                        ));
                    }
                }
                else
                {
                    var httpChallenge = await authz.Http();
                    if (httpChallenge != null)
                    {
                        challengeItems.Add(new AcmeChallengeItem(
                            domain,
                            httpChallenge.Token,
                            httpChallenge.KeyAuthz,
                            string.Empty,
                            "http-01",
                            httpChallenge
                        ));
                    }
                }
            }

            return challengeItems;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<bool> SubmitChallengeAsync(
        AcmeChallengeItem challengeItem,
        CancellationToken cancellationToken = default)
    {
        // RFC 8555 Section 7.5.1: Verify challenge is in "pending" status before calling validate.
        // If already Valid, return immediately without re-validating.
        var initialResource = await challengeItem.ChallengeContext.Resource();
        if (initialResource.Status == ChallengeStatus.Valid)
        {
            return true;
        }

        if (initialResource.Status == ChallengeStatus.Invalid)
        {
            var detail = FormatAcmeError(initialResource.Error, "Challenge is already in invalid state.");
            throw new InvalidOperationException($"ACME challenge for '{challengeItem.Domain}' failed: {detail}");
        }

        var challengeResult = await challengeItem.ChallengeContext.Validate();
        if (challengeResult.Status == ChallengeStatus.Valid)
        {
            return true;
        }

        if (challengeResult.Status == ChallengeStatus.Invalid)
        {
            var detail = FormatAcmeError(challengeResult.Error, "Challenge immediately rejected by ACME server.");
            throw new InvalidOperationException($"ACME challenge for '{challengeItem.Domain}' failed: {detail}");
        }

        // Poll challenge until it transitions to Valid or Invalid
        int maxAttempts = 30; // 30 * 2s = 60s timeout
        while (maxAttempts-- > 0 && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(2000, cancellationToken);
            var resource = await challengeItem.ChallengeContext.Resource();

            if (resource.Status == ChallengeStatus.Valid)
            {
                return true;
            }

            if (resource.Status == ChallengeStatus.Invalid)
            {
                var detail = FormatAcmeError(resource.Error, "Challenge validation failed.");
                throw new InvalidOperationException($"ACME challenge for '{challengeItem.Domain}' failed: {detail}");
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new TimeoutException($"ACME challenge validation for '{challengeItem.Domain}' timed out.");
    }

    public async Task<byte[]> FinalizeOrderAndExportPfxAsync(
        string friendlyName,
        string password,
        IReadOnlyList<string>? sanDomains = null,
        string keyAlgorithm = "RS256",
        CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            if (_currentOrder == null)
            {
                throw new InvalidOperationException("No active ACME order exists.");
            }

            // Wait for order to be in Ready or Valid state before attempting finalization
            int orderAttempts = 20; // 20 * 1.5s = 30s timeout
            var orderRes = await _currentOrder.Resource();
            while ((orderRes.Status == OrderStatus.Pending || orderRes.Status == OrderStatus.Processing) &&
                   orderAttempts-- > 0 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1500, cancellationToken);
                orderRes = await _currentOrder.Resource();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (orderRes.Status == OrderStatus.Invalid)
            {
                var detail = orderRes.Error != null ? orderRes.Error.ToString() : "ACME order is invalid.";
                throw new InvalidOperationException($"ACME order failed: {detail}");
            }

            if (orderRes.Status != OrderStatus.Ready && orderRes.Status != OrderStatus.Valid)
            {
                throw new TimeoutException($"ACME order did not transition to Ready or Valid status in time (current status: {orderRes.Status}).");
            }

            // Determine CommonName: RFC 8555 requires CSR CN to match an identifier in the order
            string commonName = friendlyName;
            if (sanDomains != null && sanDomains.Count > 0)
            {
                commonName = sanDomains.Contains(friendlyName, StringComparer.OrdinalIgnoreCase)
                    ? friendlyName
                    : sanDomains[0];
            }
            else if (orderRes.Identifiers != null && orderRes.Identifiers.Count > 0)
            {
                var firstIdentifier = orderRes.Identifiers[0].Value;
                if (!orderRes.Identifiers.Any(i => string.Equals(i.Value, friendlyName, StringComparison.OrdinalIgnoreCase)))
                {
                    commonName = firstIdentifier;
                }
            }

            // Generate cryptographic key (RSA 2048, or ECDSA P-256 / P-384)
            KeyAlgorithm algo = keyAlgorithm?.ToUpperInvariant() switch
            {
                "ES256" or "ECDSA256" or "P256" => KeyAlgorithm.ES256,
                "ES384" or "ECDSA384" or "P384" => KeyAlgorithm.ES384,
                "ES512" or "ECDSA521" => KeyAlgorithm.ES512,
                _ => KeyAlgorithm.RS256
            };

            var key = KeyFactory.NewKey(algo);
            var csr = new CsrInfo
            {
                CommonName = commonName
            };

            var cert = await _currentOrder.Generate(csr, key);
            var pfxBuilder = cert.ToPfx(key);
            return pfxBuilder.Build(friendlyName, password);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<AriRenewalInfo?> GetRenewalInfoAsync(string certificateSerialOrId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(certificateSerialOrId) || _context == null)
        {
            return null;
        }

        try
        {
            // 1. Query ACME directory to locate renewalInfo endpoint (RFC draft-ietf-acme-ari)
            using var dirReq = new HttpRequestMessage(HttpMethod.Get, _context.DirectoryUri);
            dirReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var dirResp = await SharedHttpClient.SendAsync(dirReq, cancellationToken);
            if (!dirResp.IsSuccessStatusCode)
            {
                return null;
            }

            var dirContent = await dirResp.Content.ReadAsStringAsync(cancellationToken);
            using var dirDoc = JsonDocument.Parse(dirContent);
            if (!dirDoc.RootElement.TryGetProperty("renewalInfo", out var renewalInfoProp))
            {
                return null;
            }

            string? renewalInfoEndpoint = renewalInfoProp.GetString();
            if (string.IsNullOrWhiteSpace(renewalInfoEndpoint))
            {
                return null;
            }

            // Clean cert ID (remove colons or spaces)
            string cleanId = certificateSerialOrId.Replace(":", "").Replace(" ", "").Trim();
            string ariUrl = $"{renewalInfoEndpoint.TrimEnd('/')}/{cleanId}";

            using var ariReq = new HttpRequestMessage(HttpMethod.Get, ariUrl);
            ariReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var ariResp = await SharedHttpClient.SendAsync(ariReq, cancellationToken);
            if (!ariResp.IsSuccessStatusCode)
            {
                return null;
            }

            var ariContent = await ariResp.Content.ReadAsStringAsync(cancellationToken);
            using var ariDoc = JsonDocument.Parse(ariContent);
            var root = ariDoc.RootElement;

            if (root.TryGetProperty("suggestedWindow", out var windowProp))
            {
                if (windowProp.TryGetProperty("start", out var startProp) &&
                    windowProp.TryGetProperty("end", out var endProp) &&
                    startProp.TryGetDateTime(out var start) &&
                    endProp.TryGetDateTime(out var end))
                {
                    string? explanationUrl = root.TryGetProperty("explanationURL", out var expProp)
                        ? expProp.GetString()
                        : null;

                    return new AriRenewalInfo(new AriRenewalWindow(start, end), explanationUrl);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Non-fatal if CA does not implement ARI or cert not found
        }

        return null;
    }

    private static string FormatAcmeError(AcmeError? error, string defaultMessage)
    {
        if (error == null) return defaultMessage;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(error.Detail))
        {
            parts.Add(error.Detail);
        }
        if (error.Type != null)
        {
            parts.Add($"Type: {error.Type}");
        }
        if (error.Subproblems != null && error.Subproblems.Count > 0)
        {
            var subs = error.Subproblems
                .Where(s => !string.IsNullOrWhiteSpace(s.Detail))
                .Select(s => s.Detail);
            parts.Add($"Subproblems: {string.Join("; ", subs)}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : (error.ToString() ?? defaultMessage);
    }
}
