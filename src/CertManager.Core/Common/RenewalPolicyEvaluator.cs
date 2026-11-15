using System;
using CertManager.Core.Entities;
using CertManager.Core.Enums;

namespace CertManager.Core.Common;

public static class RenewalPolicyEvaluator
{
    public static bool ShouldRenew(CertificateRecord cert, CertificateProfile? profile, DateTime? referenceTime = null)
    {
        if (cert.Status == CertificateStatus.Revoked || cert.Status == CertificateStatus.Archived)
        {
            return false;
        }

        DateTime now = referenceTime ?? DateTime.UtcNow;

        // Immediately renew if already expired
        if (now >= cert.NotAfter)
        {
            return true;
        }

        // Calculate total lifetime and remaining lifetime
        TimeSpan totalLifetime = cert.NotAfter - cert.NotBefore;
        if (totalLifetime <= TimeSpan.Zero)
        {
            return true;
        }

        TimeSpan remainingLifetime = cert.NotAfter - now;
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return true;
        }

        // Percentage threshold from profile (default 33% per Spec Section 9.3)
        int thresholdPercent = profile?.RenewalThresholdPercent ?? 33;
        double remainingPercent = (remainingLifetime.TotalSeconds / totalLifetime.TotalSeconds) * 100.0;

        return remainingPercent <= thresholdPercent;
    }

    public static int CalculateLifetimeProgressPercent(CertificateRecord cert, DateTime? referenceTime = null)
    {
        DateTime now = referenceTime ?? DateTime.UtcNow;

        if (now <= cert.NotBefore) return 0;
        if (now >= cert.NotAfter) return 100;

        TimeSpan totalLifetime = cert.NotAfter - cert.NotBefore;
        if (totalLifetime <= TimeSpan.Zero) return 100;

        TimeSpan elapsed = now - cert.NotBefore;
        double percent = (elapsed.TotalSeconds / totalLifetime.TotalSeconds) * 100.0;

        return Math.Clamp((int)Math.Round(percent), 0, 100);
    }

    public static double GetRemainingDays(CertificateRecord cert, DateTime? referenceTime = null)
    {
        DateTime now = referenceTime ?? DateTime.UtcNow;
        var diff = cert.NotAfter - now;
        return Math.Max(0, diff.TotalDays);
    }
}
