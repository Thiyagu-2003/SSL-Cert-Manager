using System;
using System.IO;

namespace CertManager.Infrastructure.Logging;

public static class FileLoggerHelper
{
    private static readonly object _fileLock = new();

    public static string GetLogsDirectory()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CertManager",
            "Logs");

        try
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
        catch
        {
            try
            {
                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CertManager",
                    "Logs");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
            catch
            {
                return AppContext.BaseDirectory;
            }
        }
    }

    public static string GetLogFilePath()
    {
        return Path.Combine(GetLogsDirectory(), "certflow.log");
    }

    public static void AppendLog(string level, string subject, string body)
    {
        try
        {
            lock (_fileLock)
            {
                string path = GetLogFilePath();

                // Rotate file if larger than 10MB
                if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024)
                {
                    string oldPath = Path.Combine(GetLogsDirectory(), $"certflow_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                    try { File.Move(path, oldPath); } catch { /* ignore */ }
                }

                string safeLevel = (level ?? "INFO").ToUpperInvariant();
                string safeSubject = subject ?? string.Empty;
                string safeBody = body ?? string.Empty;

                string entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] [{safeLevel}] {safeSubject}: {safeBody}{Environment.NewLine}";
                File.AppendAllText(path, entry);
            }
        }
        catch
        {
            // Silently ignore if locked or inaccessible
        }
    }

    public static void ClearLogFile()
    {
        try
        {
            lock (_fileLock)
            {
                string path = GetLogFilePath();
                if (File.Exists(path))
                {
                    File.WriteAllText(path, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] [INFO] Log file cleared by administrator.{Environment.NewLine}");
                }
            }
        }
        catch
        {
            // Silently ignore
        }
    }
}
