using System;
using System.IO;
using System.Text.Json;
using CertManager.Core.Models;

namespace CertManager.Infrastructure.Notifications;

public static class NotificationSettingsManager
{
    private static readonly object LockObj = new();
    private static NotificationSettings? _cachedSettings;

    public static string GetSettingsFilePath()
    {
        string primaryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CertManager");

        if (!Directory.Exists(primaryDir))
        {
            try { Directory.CreateDirectory(primaryDir); }
            catch
            {
                try
                {
                    primaryDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CertManager");
                    Directory.CreateDirectory(primaryDir);
                }
                catch
                {
                    primaryDir = AppContext.BaseDirectory;
                }
            }
        }

        return Path.Combine(primaryDir, "notification_settings.json");
    }

    public static NotificationSettings LoadSettings()
    {
        lock (LockObj)
        {
            try
            {
                string path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<NotificationSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (settings != null)
                    {
                        _cachedSettings = settings;
                        return settings;
                    }
                }
            }
            catch
            {
                // Fall back to default settings
            }

            _cachedSettings = new NotificationSettings();
            return _cachedSettings;
        }
    }

    public static NotificationSettings CurrentSettings => _cachedSettings ?? LoadSettings();

    public static void SaveSettings(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (LockObj)
        {
            _cachedSettings = settings;
            try
            {
                string path = GetSettingsFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Silently ignore write failures if permissions restricted
            }
        }
    }
}
