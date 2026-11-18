using System;
using System.Collections.Generic;

namespace CertManager.Core.Models;

public class LiveTlsInspectionResult
{
    public string Domain { get; set; } = string.Empty;
    public int Port { get; set; } = 443;
    public bool IsReachable { get; set; }
    public bool HandshakeSuccess { get; set; }
    public string NegotiatedProtocol { get; set; } = "None";
    public string CipherSuite { get; set; } = "Unknown";
    public string CertificateSubject { get; set; } = string.Empty;
    public string CertificateIssuer { get; set; } = string.Empty;
    public string CertificateThumbprint { get; set; } = string.Empty;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public int DaysRemaining { get; set; }
    public bool IsExpired { get; set; }
    public bool IsSelfSigned { get; set; }
    public int ChainElementsCount { get; set; }
    public string ChainStatus { get; set; } = "Valid";
    public bool HstsHeaderPresent { get; set; }
    public string? HstsHeaderValue { get; set; }
    public string OverallGrade { get; set; } = "F";
    public string GradeExplanation { get; set; } = string.Empty;
    public List<string> Findings { get; set; } = new();
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}
