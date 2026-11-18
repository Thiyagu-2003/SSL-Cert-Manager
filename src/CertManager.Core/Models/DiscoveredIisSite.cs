using System;
using System.Collections.Generic;

namespace CertManager.Core.Models;

public record DiscoveredBinding(
    string Protocol,
    string Hostname,
    int Port,
    string IpAddress,
    bool SslFlagsSni,
    string? CertificateThumbprint);

public record DiscoveredIisSite(
    long SiteId,
    string SiteName,
    string State,
    bool HasHttps,
    IReadOnlyList<DiscoveredBinding> Bindings,
    IReadOnlyList<string> Hostnames,
    bool HasHttpsRedirect = false)
{
    public bool NeedsCertificate => !HasHttps && Bindings.Count > 0;
}
