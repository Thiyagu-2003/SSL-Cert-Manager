using System;
using System.Windows;
using System.Windows.Media;

namespace CertManager.Desktop.Services;

/// <summary>
/// Manages runtime dynamic application theming (Dark Mode and Light Mode).
/// Dynamically updates semantic brush resources in Application.Current.Resources and persists preference.
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
                    // High-Contrast Cybersecurity Dark Mode Palette
                    SetResource("AppBgBrush", "#0B0F19");
                    SetResource("ContentBgBrush", "#0B0F19");
                    SetResource("SidebarBgBrush", "#070B14");
                    SetResource("SidebarNavHoverBrush", "#1E293B");
                    SetResource("CardBgBrush", "#131B2E");
                    SetResource("CardBorderBrush", "#25334D");
                    SetResource("TextPrimaryBrush", "#F8FAFC");       // Crisp white: 15.8:1 contrast
                    SetResource("TextSecondaryBrush", "#CBD5E1");     // Light slate: readable and clean
                    SetResource("TextMutedBrush", "#94A3B8");         // Medium slate: clear hints
                    SetResource("TextHeaderLabelBrush", "#38BDF8");   // Sky blue: vibrant uppercase headers
                    SetResource("InputBgBrush", "#0F172A");           // Dark input surface
                    SetResource("InputBorderBrush", "#334155");       // Defined input border
                    SetResource("MutedBgBrush", "#1A2438");           // Subtle container surface
                    SetResource("SubtleBorderBrush", "#25334D");
                    SetResource("HeaderBgBrush", "#131B2E");
                    SetResource("GanttTrackBgBrush", "#1E293B");
                    SetResource("ButtonSecBgBrush", "#1E293B");
                    SetResource("ButtonSecBorderBrush", "#334155");
                    SetResource("ButtonSecFgBrush", "#F1F5F9");
                    SetResource("ChipBgBrush", "#1E293B");
                    SetResource("ChipBorderBrush", "#334155");
                    SetResource("ChipFgBrush", "#E2E8F0");
                    SetResource("BannerSuccessBgBrush", "#064E3B");
                    SetResource("BannerSuccessBorderBrush", "#059669");
                    SetResource("BannerSuccessTextBrush", "#6EE7B7");
                    SetResource("BannerSuccessSubtextBrush", "#A7F3D0");
                    SetResource("BannerWarningBgBrush", "#451A03");
                    SetResource("BannerWarningBorderBrush", "#B45309");
                    SetResource("BannerWarningTextBrush", "#FDE68A");
                    SetResource("BannerErrorBgBrush", "#450A0A");
                    SetResource("BannerErrorBorderBrush", "#DC2626");
                    SetResource("BannerErrorTextBrush", "#FCA5A5");
                }
                else
                {
                    // Clean Modern Light Mode Enterprise Palette
                    SetResource("AppBgBrush", "#F8FAFC");
                    SetResource("ContentBgBrush", "#F8FAFC");
                    SetResource("SidebarBgBrush", "#0F172A");
                    SetResource("SidebarNavHoverBrush", "#1E293B");
                    SetResource("CardBgBrush", "#FFFFFF");
                    SetResource("CardBorderBrush", "#CBD5E1");
                    SetResource("TextPrimaryBrush", "#0F172A");       // Near black
                    SetResource("TextSecondaryBrush", "#475569");     // Dark slate
                    SetResource("TextMutedBrush", "#64748B");         // Medium slate
                    SetResource("TextHeaderLabelBrush", "#0369A1");   // Ocean blue tag
                    SetResource("InputBgBrush", "#FFFFFF");
                    SetResource("InputBorderBrush", "#CBD5E1");
                    SetResource("MutedBgBrush", "#F1F5F9");
                    SetResource("SubtleBorderBrush", "#E2E8F0");
                    SetResource("HeaderBgBrush", "#FFFFFF");
                    SetResource("GanttTrackBgBrush", "#E2E8F0");
                    SetResource("ButtonSecBgBrush", "#FFFFFF");
                    SetResource("ButtonSecBorderBrush", "#CBD5E1");
                    SetResource("ButtonSecFgBrush", "#334155");
                    SetResource("ChipBgBrush", "#F8FAFC");
                    SetResource("ChipBorderBrush", "#E2E8F0");
                    SetResource("ChipFgBrush", "#475569");
                    SetResource("BannerSuccessBgBrush", "#F0FDF4");
                    SetResource("BannerSuccessBorderBrush", "#86EFAC");
                    SetResource("BannerSuccessTextBrush", "#15803D");
                    SetResource("BannerSuccessSubtextBrush", "#166534");
                    SetResource("BannerWarningBgBrush", "#FFFBEB");
                    SetResource("BannerWarningBorderBrush", "#FDE68A");
                    SetResource("BannerWarningTextBrush", "#B45309");
                    SetResource("BannerErrorBgBrush", "#FEF2F2");
                    SetResource("BannerErrorBorderBrush", "#FECACA");
                    SetResource("BannerErrorTextBrush", "#991B1B");
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
