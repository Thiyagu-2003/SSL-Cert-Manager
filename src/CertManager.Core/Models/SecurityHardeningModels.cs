using System.Collections.Generic;

namespace CertManager.Core.Models;

public class ProtocolStatus
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsSecure { get; set; }
    public bool IsRecommended { get; set; }
}

public class CipherStatus
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsSecure { get; set; }
}

public class SchannelAuditReport
{
    public string OverallGrade { get; set; } = "B";
    public bool IsPciDssCompliant { get; set; }
    public bool Tls13Supported { get; set; }
    public bool Tls12Enabled { get; set; }
    public bool LegacyProtocolsDisabled { get; set; }
    public bool InsecureCiphersDisabled { get; set; }
    public bool HstsConfiguredGlobally { get; set; }
    public List<ProtocolStatus> Protocols { get; set; } = new();
    public List<CipherStatus> Ciphers { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public bool RequiresReboot { get; set; }
}

public class ApplyHardeningOptions
{
    public bool EnableTls13 { get; set; } = true;
    public bool EnableTls12 { get; set; } = true;
    public bool DisableTls11 { get; set; } = true;
    public bool DisableTls10 { get; set; } = true;
    public bool DisableSsl3 { get; set; } = true;
    public bool DisableSsl2 { get; set; } = true;
    public bool DisableWeakCiphers { get; set; } = true; // 3DES, RC4, NULL, DES
    public bool DisableWeakHashes { get; set; } = true;  // MD5
}
