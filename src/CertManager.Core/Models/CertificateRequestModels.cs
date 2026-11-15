using System;

namespace CertManager.Core.Models;

public class CertificateRequestModel
{
    public long SiteId { get; set; }
    public string DomainName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public bool IsStaging { get; set; } = true;
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
