using System;

namespace CertManager.Core.Models;

public static class WellKnownAcmeDirectories
{
    public const string LetsEncrypt = "https://acme-v02.api.letsencrypt.org/directory";
    public const string LetsEncryptStaging = "https://acme-staging-v02.api.letsencrypt.org/directory";
    public const string ZeroSSL = "https://acme.zerossl.com/v2/DV90";
    public const string GoogleTrustServices = "https://dv.acme-v02.api.pki.goog/directory";
    public const string BuyPass = "https://api.buypass.com/acme/directory";

    public static Uri ResolveDirectoryUri(string? caName, bool isStaging, string? customUrl = null)
    {
        if (isStaging)
        {
            return new Uri(LetsEncryptStaging);
        }

        if (string.Equals(caName, "ZeroSSL", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(ZeroSSL);
        }
        if (string.Equals(caName, "GoogleTrustServices", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(caName, "Google", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(GoogleTrustServices);
        }
        if (string.Equals(caName, "BuyPass", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(BuyPass);
        }
        if (string.Equals(caName, "Custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(customUrl))
        {
            return new Uri(customUrl);
        }

        return new Uri(LetsEncrypt);
    }
}

public class CertificateRequestModel
{
    public long SiteId { get; set; }
    public string? SiteName { get; set; }
    public string DomainName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public bool IsStaging { get; set; } = true;
    public string CertificateAuthority { get; set; } = "LetsEncrypt"; // LetsEncrypt, ZeroSSL, GoogleTrustServices, BuyPass, Custom, SelfSigned
    public string? CustomCaDirectoryUrl { get; set; }
    public bool EnableCaFallback { get; set; } = true;
    public string FallbackCertificateAuthority { get; set; } = "ZeroSSL";
    public string KeyAlgorithm { get; set; } = "RS256"; // RS256, ES256, ES384
    public bool AutoPruneSupersededCerts { get; set; } = true;
    public string ValidationMethod { get; set; } = "Http01";
    public string? DnsProvider { get; set; } // "Cloudflare", "Manual"
    public string? DnsApiToken { get; set; }
    public string? DnsZoneId { get; set; }
}

public class CertificateRequestStatusResult
{
    public bool Success { get; set; }
    public string Step { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? BoundSiteName { get; set; }
}
