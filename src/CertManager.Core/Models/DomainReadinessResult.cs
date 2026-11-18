using System.Collections.Generic;

namespace CertManager.Core.Models;

public class DomainReadinessResult
{
    public bool OverallReady { get; set; }
    public string DomainName { get; set; } = string.Empty;
    public string PublicIp { get; set; } = string.Empty;
    public List<string> ResolvedIps { get; set; } = new();
    public bool DnsMatchesPublicIp { get; set; }
    public string DnsStatusMessage { get; set; } = string.Empty;
    public bool Port80Open { get; set; }
    public string Port80StatusMessage { get; set; } = string.Empty;
    public bool ChallengeEndpointResponsive { get; set; }
    public string ChallengeStatusMessage { get; set; } = string.Empty;
    public List<string> Recommendations { get; set; } = new();
}
