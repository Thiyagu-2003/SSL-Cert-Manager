using System;
using System.Collections.Generic;

namespace CertManager.Core.Models;

public enum RenewalHookType
{
    PowerShellScript = 0,
    BatchScript = 1,
    WindowsServiceRestart = 2,
    CustomExecutable = 3
}

public class RenewalHookConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Post-Renewal Hook";
    public bool Enabled { get; set; } = true;
    public RenewalHookType HookType { get; set; } = RenewalHookType.PowerShellScript;
    public string ScriptOrTarget { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public bool RunOnSuccessOnly { get; set; } = true;
}

public class HookExecutionResult
{
    public string HookId { get; set; } = string.Empty;
    public string HookName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
}
