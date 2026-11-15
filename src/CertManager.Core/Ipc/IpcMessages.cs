using System;

namespace CertManager.Core.Ipc;

public class IpcRequest
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public string Action { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
}

public class IpcResponse
{
    public Guid RequestId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PayloadJson { get; set; }

    public static IpcResponse Ok(Guid requestId, string? payloadJson = null) =>
        new() { RequestId = requestId, Success = true, PayloadJson = payloadJson };

    public static IpcResponse Fail(Guid requestId, string errorMessage) =>
        new() { RequestId = requestId, Success = false, ErrorMessage = errorMessage };
}

public class ServiceStatusResponse
{
    public bool IsRunning { get; set; } = true;
    public string Version { get; set; } = "2.0.0";
    public double UptimeSeconds { get; set; }
    public int TotalCertificates { get; set; }
    public int ExpiringSoonCount { get; set; }
    public int ActiveOrdersCount { get; set; }
    public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
}
