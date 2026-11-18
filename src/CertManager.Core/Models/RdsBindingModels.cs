using System;

namespace CertManager.Core.Models;

public class RdsBindingInfo
{
    public bool IsRdsInstalled { get; set; }
    public string? CurrentThumbprint { get; set; }
    public string? CurrentCertSubject { get; set; }
    public DateTime? CurrentCertExpires { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

public class RdsBindingResult
{
    public bool Success { get; set; }
    public string? PreviousThumbprint { get; set; }
    public string NewThumbprint { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
