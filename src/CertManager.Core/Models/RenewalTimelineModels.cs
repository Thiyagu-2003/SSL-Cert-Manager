using System;
using CertManager.Core.Entities;

namespace CertManager.Core.Models;

/// <summary>
/// Lifecycle zone for certificate renewal tracking based on ACME Renewal Info (ARI) and PKI standards.
/// </summary>
public enum RenewalTimelineZone
{
    Active = 0,          // Healthy active window (e.g. Days 0-60 of 90)
    AriRenewalWindow = 1,// Recommended ACME ARI renewal window (e.g. Days 60-76 of 90)
    Warning = 2,         // Warning window with less than 14 days remaining
    Critical = 3,        // Critical emergency window with less than 3 days remaining
    Expired = 4          // Past expiration timestamp
}

/// <summary>
/// Detailed metrics and timeline state for 90-day renewal horizon and ACME ARI scheduling.
/// </summary>
public class RenewalTimelineInfo
{
    public int TotalLifetimeDays { get; init; } = 90;
    public int ElapsedDays { get; init; }
    public int RemainingDays { get; init; }
    public double ProgressPercent { get; init; }
    public int AriWindowStartDay { get; init; }
    public int AriWindowEndDay { get; init; }
    public int WarningThresholdDay { get; init; }
    public int CriticalThresholdDay { get; init; }

    public RenewalTimelineZone CurrentZone { get; init; }
    public string ZoneLabel { get; init; } = string.Empty;
    public string ZoneBadgeBackground { get; init; } = string.Empty;
    public string ZoneBadgeForeground { get; init; } = string.Empty;
    public string ZoneColorHex { get; init; } = string.Empty;
    public string StatusDescription { get; init; } = string.Empty;
    public bool IsInsideAriWindow { get; init; }
    public int DaysUntilAriWindow { get; init; }

    /// <summary>
    /// Computes full timeline metrics and ARI window classification for a given certificate record.
    /// </summary>
    public static RenewalTimelineInfo FromCertificate(CertificateRecord cert, DateTime? referenceTime = null)
    {
        ArgumentNullException.ThrowIfNull(cert);
        DateTime now = referenceTime ?? DateTime.UtcNow;

        int totalDays = (int)Math.Max(1, Math.Round((cert.NotAfter - cert.NotBefore).TotalDays));
        
        // Standard Web PKI / ARI thresholds:
        // ARI Window starts at 66.7% of lifetime (Day 60 of 90)
        // Warning Window starts 14 days before expiry (Day 76 of 90)
        // Critical Window starts 3 days before expiry (Day 87 of 90)
        int ariStart = (int)Math.Round(totalDays * 0.667);
        int warningStart = Math.Max(ariStart + 1, totalDays - 14);
        int criticalStart = Math.Max(warningStart + 1, totalDays - 3);

        int elapsed = (int)Math.Round((now - cert.NotBefore).TotalDays);
        int remaining = (int)Math.Max(0, Math.Round((cert.NotAfter - now).TotalDays));

        double progress;
        if (now <= cert.NotBefore)
        {
            progress = 0;
            elapsed = 0;
        }
        else if (now >= cert.NotAfter)
        {
            progress = 100;
            elapsed = totalDays;
            remaining = 0;
        }
        else
        {
            progress = Math.Clamp(((now - cert.NotBefore).TotalSeconds / (cert.NotAfter - cert.NotBefore).TotalSeconds) * 100.0, 0.0, 100.0);
        }

        RenewalTimelineZone zone;
        string label;
        string bg;
        string fg;
        string color;
        string desc;
        bool inAri = false;
        int daysUntilAri = Math.Max(0, ariStart - elapsed);

        if (now >= cert.NotAfter)
        {
            zone = RenewalTimelineZone.Expired;
            label = "EXPIRED";
            bg = "#FEE2E2";
            fg = "#991B1B";
            color = "#EF4444";
            desc = $"Expired • Immediate reissue required";
        }
        else if (elapsed >= criticalStart || remaining <= 3)
        {
            zone = RenewalTimelineZone.Critical;
            label = "CRITICAL (<3d)";
            bg = "#FEE2E2";
            fg = "#DC2626";
            color = "#EF4444";
            desc = $"Day {elapsed} of {totalDays} • EMERGENCY: {remaining} days until expiry";
        }
        else if (elapsed >= warningStart || remaining <= 14)
        {
            zone = RenewalTimelineZone.Warning;
            label = "WARNING (<14d)";
            bg = "#FEF3C7";
            fg = "#B45309";
            color = "#F59E0B";
            desc = $"Day {elapsed} of {totalDays} • Warning phase: {remaining} days remaining";
        }
        else if (elapsed >= ariStart)
        {
            zone = RenewalTimelineZone.AriRenewalWindow;
            inAri = true;
            label = "ARI RENEWAL WINDOW";
            bg = "#E0F2FE";
            fg = "#0369A1";
            color = "#0284C7";
            desc = $"Day {elapsed} of {totalDays} • In optimal ACME ARI window (Auto-Renewal scheduled)";
        }
        else
        {
            zone = RenewalTimelineZone.Active;
            label = "ACTIVE & HEALTHY";
            bg = "#DCFCE7";
            fg = "#166534";
            color = "#10B981";
            desc = $"Day {elapsed} of {totalDays} • Optimal active phase (ARI window in {daysUntilAri} days)";
        }

        return new RenewalTimelineInfo
        {
            TotalLifetimeDays = totalDays,
            ElapsedDays = Math.Clamp(elapsed, 0, totalDays),
            RemainingDays = remaining,
            ProgressPercent = Math.Round(progress, 1),
            AriWindowStartDay = ariStart,
            AriWindowEndDay = warningStart,
            WarningThresholdDay = warningStart,
            CriticalThresholdDay = criticalStart,
            CurrentZone = zone,
            ZoneLabel = label,
            ZoneBadgeBackground = bg,
            ZoneBadgeForeground = fg,
            ZoneColorHex = color,
            StatusDescription = desc,
            IsInsideAriWindow = inAri,
            DaysUntilAriWindow = daysUntilAri
        };
    }
}
