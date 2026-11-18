using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Models;

namespace CertManager.Infrastructure.Hooks;

public interface IRenewalHookExecutor
{
    Task<HookExecutionResult> ExecuteHookAsync(
        RenewalHookConfig config, 
        string domain, 
        string thumbprint, 
        string? pfxPath = null, 
        DateTime? expires = null, 
        CancellationToken cancellationToken = default);
}

public class RenewalHookExecutor : IRenewalHookExecutor
{
    public async Task<HookExecutionResult> ExecuteHookAsync(
        RenewalHookConfig config, 
        string domain, 
        string thumbprint, 
        string? pfxPath = null, 
        DateTime? expires = null, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var sw = Stopwatch.StartNew();
        var result = new HookExecutionResult
        {
            HookId = config.Id,
            HookName = config.Name
        };

        if (string.IsNullOrWhiteSpace(config.ScriptOrTarget))
        {
            result.Success = false;
            result.StandardError = "Script or target command is not specified.";
            return result;
        }

        string rawArgs = config.Arguments ?? string.Empty;
        string resolvedDomain = domain ?? string.Empty;
        string resolvedThumbprint = thumbprint ?? string.Empty;
        string resolvedPfx = pfxPath ?? string.Empty;
        string resolvedExpires = expires?.ToString("yyyy-MM-dd") ?? string.Empty;

        string resolvedArgs = rawArgs
            .Replace("{Domain}", resolvedDomain, StringComparison.OrdinalIgnoreCase)
            .Replace("{Thumbprint}", resolvedThumbprint, StringComparison.OrdinalIgnoreCase)
            .Replace("{PfxPath}", resolvedPfx, StringComparison.OrdinalIgnoreCase)
            .Replace("{Expires}", resolvedExpires, StringComparison.OrdinalIgnoreCase);

        try
        {
            switch (config.HookType)
            {
                case RenewalHookType.WindowsServiceRestart:
                    await ExecuteServiceRestartAsync(config.ScriptOrTarget.Trim(), result, cancellationToken);
                    break;

                case RenewalHookType.PowerShellScript:
                    if (config.ScriptOrTarget.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) && !File.Exists(config.ScriptOrTarget))
                    {
                        result.Success = false;
                        result.StandardError = $"PowerShell script file '{config.ScriptOrTarget}' not found.";
                        return result;
                    }
                    await ExecuteProcessAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{config.ScriptOrTarget}\" {resolvedArgs}", config.TimeoutSeconds, result, cancellationToken);
                    break;

                case RenewalHookType.BatchScript:
                    if ((config.ScriptOrTarget.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || config.ScriptOrTarget.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) && !File.Exists(config.ScriptOrTarget))
                    {
                        result.Success = false;
                        result.StandardError = $"Batch script file '{config.ScriptOrTarget}' not found.";
                        return result;
                    }
                    await ExecuteProcessAsync("cmd.exe", $"/c \"{config.ScriptOrTarget}\" {resolvedArgs}", config.TimeoutSeconds, result, cancellationToken);
                    break;

                case RenewalHookType.CustomExecutable:
                    await ExecuteProcessAsync(config.ScriptOrTarget, resolvedArgs, config.TimeoutSeconds, result, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.StandardError = $"Hook execution error: {ex.Message}";
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private static async Task ExecuteProcessAsync(
        string fileName, 
        string arguments, 
        int timeoutSeconds, 
        HookExecutionResult result, 
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            process.WaitForExit();
            result.ExitCode = process.ExitCode;
            result.Success = (process.ExitCode == 0);
            result.StandardOutput = stdout.ToString().Trim();
            result.StandardError = stderr.ToString().Trim();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            result.Success = false;
            result.StandardError = "Hook process timed out and was terminated.";
            result.ExitCode = -1;
        }
    }

    private static async Task ExecuteServiceRestartAsync(
        string serviceName, 
        HookExecutionResult result, 
        CancellationToken cancellationToken)
    {
        // Sanitize service name against command injection
        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.IndexOfAny(new[] { '&', '|', ';', '"', '<', '>', '\r', '\n' }) >= 0)
        {
            result.Success = false;
            result.StandardError = "Invalid service name for Windows Service restart.";
            return;
        }

        // Safe command line restart via net.exe
        await ExecuteProcessAsync("cmd.exe", $"/c net stop \"{serviceName}\" & net start \"{serviceName}\"", 30, result, cancellationToken);
    }
}
