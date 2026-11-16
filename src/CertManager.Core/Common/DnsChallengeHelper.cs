using System;
using System.Security.Cryptography;
using System.Text;

namespace CertManager.Core.Common;

public static class DnsChallengeHelper
{
    /// <summary>
    /// Computes the DNS TXT record name according to RFC 8555 Section 8.4.
    /// Prepends '_acme-challenge.' to the domain, stripping any leading wildcard prefix '*.'
    /// </summary>
    public static string GetRecordName(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Domain cannot be empty.", nameof(domain));
        }

        string cleanDomain = domain.Trim();
        if (cleanDomain.StartsWith("*.", StringComparison.Ordinal))
        {
            cleanDomain = cleanDomain[2..];
        }

        cleanDomain = cleanDomain.Trim().TrimEnd('.');

        if (string.IsNullOrWhiteSpace(cleanDomain) || cleanDomain == "*")
        {
            throw new ArgumentException("Domain cannot be empty or invalid.", nameof(domain));
        }

        return $"_acme-challenge.{cleanDomain}";
    }

    /// <summary>
    /// Computes the DNS TXT record value as the Base64Url-encoded SHA-256 digest
    /// of the ACME key authorization string (RFC 8555 Section 8.4).
    /// </summary>
    public static string ComputeDnsTxtValue(string keyAuthorization)
    {
        if (string.IsNullOrWhiteSpace(keyAuthorization))
        {
            throw new ArgumentException("Key authorization cannot be empty.", nameof(keyAuthorization));
        }

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyAuthorization));
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Encodes bytes to Base64Url string without padding (RFC 7515).
    /// </summary>
    public static string Base64UrlEncode(byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
