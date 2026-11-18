using System;
using System.IO;
using System.Text.Json;

namespace CertManager.Desktop.Services;

public class UserSettings
{
    public string GlobalContactEmail { get; set; } = string.Empty;
    public string PreferredEnvironment { get; set; } = "Staging";
    public bool RememberEmailGlobally { get; set; } = true;
    public bool IsInitialSetupCompleted { get; set; } = false;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool EnableWebhooks { get; set; } = false;
}

public class UserSettingsService
{
    private static readonly Lazy<UserSettingsService> _instance = new(() => new UserSettingsService());
    public static UserSettingsService Instance => _instance.Value;

    private readonly string _settingsFilePath;
    private UserSettings _settings;

    public UserSettingsService()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CertManager");
        _settingsFilePath = Path.Combine(dir, "user_settings.json");
        _settings = LoadSettings();
    }

    public UserSettings Settings => _settings;

    private UserSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Silently fall back to fresh settings
        }
        return new UserSettings();
    }

    public void SaveSettings(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            _settings = settings;
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Silently ignore if unable to write
        }
    }
}
