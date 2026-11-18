using System;
using System.Windows;
using System.Windows.Media;

namespace CertManager.Desktop.Services;

/// <summary>
/// Manages runtime dynamic application theming (Dark Mode and Light Mode).
/// Dynamically updates brush resources in Application.Current.Resources and persists preference.
/// </summary>
public class ThemeManager
{
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    public static ThemeManager Instance => _instance.Value;

    public event Action<bool>? ThemeChanged;

    public bool IsDarkTheme { get; private set; }

    public string CurrentTheme => IsDarkTheme ? "Dark" : "Light";

    public void Initialize()
    {
        var settings = UserSettingsService.Instance.Settings;
        bool isDark = string.Equals(settings.ThemeMode, "Dark", StringComparison.OrdinalIgnoreCase);
        ApplyTheme(isDark ? "Dark" : "Light", persist: false);
    }

    public void ToggleTheme()
    {
        string newTheme = IsDarkTheme ? "Light" : "Dark";
        ApplyTheme(newTheme, persist: true);
    }

    public void ApplyTheme(string themeMode, bool persist = true)
    {
        IsDarkTheme = string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase);

        var app = System.Windows.Application.Current;
        if (app != null)
        {
            Action applyBrushes = () =>
            {
                if (IsDarkTheme)
                {
                    // Dark Mode Palette
                    SetResource("AppBgBrush", "#0B0F19");
                    SetResource("ContentBgBrush", "#0B0F19");
                    SetResource("SidebarBgBrush", "#070B14");
                    SetResource("SidebarNavHoverBrush", "#1E293B");
                    SetResource("CardBgBrush", "#131B2E");
                    SetResource("CardBorderBrush", "#223049");
                    SetResource("TextPrimaryBrush", "#F8FAFC");
                    SetResource("TextSecondaryBrush", "#94A3B8");
                    SetResource("TextMutedBrush", "#64748B");
                    SetResource("InputBgBrush", "#0F172A");
                    SetResource("InputBorderBrush", "#334155");
                    SetResource("MutedBgBrush", "#1A2438");
                    SetResource("SubtleBorderBrush", "#223049");
                    SetResource("HeaderBgBrush", "#131B2E");
                    SetResource("GanttTrackBgBrush", "#1E293B");
                    SetResource("ButtonSecBgBrush", "#1E293B");
                    SetResource("ButtonSecBorderBrush", "#334155");
                    SetResource("ButtonSecFgBrush", "#E2E8F0");
                }
                else
                {
                    // Light Mode Palette
                    SetResource("AppBgBrush", "#F8FAFC");
                    SetResource("ContentBgBrush", "#F8FAFC");
                    SetResource("SidebarBgBrush", "#0F172A");
                    SetResource("SidebarNavHoverBrush", "#1E293B");
                    SetResource("CardBgBrush", "#FFFFFF");
                    SetResource("CardBorderBrush", "#E2E8F0");
                    SetResource("TextPrimaryBrush", "#0F172A");
                    SetResource("TextSecondaryBrush", "#475569");
                    SetResource("TextMutedBrush", "#94A3B8");
                    SetResource("InputBgBrush", "#FFFFFF");
                    SetResource("InputBorderBrush", "#CBD5E1");
                    SetResource("MutedBgBrush", "#F1F5F9");
                    SetResource("SubtleBorderBrush", "#E2E8F0");
                    SetResource("HeaderBgBrush", "#FFFFFF");
                    SetResource("GanttTrackBgBrush", "#E2E8F0");
                    SetResource("ButtonSecBgBrush", "#FFFFFF");
                    SetResource("ButtonSecBorderBrush", "#CBD5E1");
                    SetResource("ButtonSecFgBrush", "#334155");
                }
            };

            if (app.Dispatcher.CheckAccess())
            {
                applyBrushes();
            }
            else
            {
                app.Dispatcher.Invoke(applyBrushes);
            }
        }

        if (persist)
        {
            var settings = UserSettingsService.Instance.Settings;
            settings.ThemeMode = CurrentTheme;
            UserSettingsService.Instance.SaveSettings(settings);
        }

        ThemeChanged?.Invoke(IsDarkTheme);
    }

    private static void SetResource(string key, string hexColor)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        brush.Freeze();
        System.Windows.Application.Current.Resources[key] = brush;
    }
}
