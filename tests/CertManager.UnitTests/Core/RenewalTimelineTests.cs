using System;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Models;
using Xunit;

namespace CertManager.UnitTests.Core;

public class RenewalTimelineTests
{
    private static CertificateRecord CreateTestCert(DateTime notBefore, DateTime notAfter)
    {
        return new CertificateRecord
        {
            CertificateId = Guid.NewGuid(),
            Subject = "CN=test.example.com",
            Issuer = "CN=Let's Encrypt Authority X3",
            Thumbprint = "11223344556677889900AABBCCDDEEFF11223344",
            SerialNumber = "1234567890",
            NotBefore = notBefore,
            NotAfter = notAfter,
            Status = CertificateStatus.Active
        };
    }

    [Fact]
    public void FromCertificate_Day10Of90_ReturnsActiveZone()
    {
        var start = DateTime.UtcNow.AddDays(-10);
        var end = DateTime.UtcNow.AddDays(80);
        var cert = CreateTestCert(start, end);

        var timeline = RenewalTimelineInfo.FromCertificate(cert);

        Assert.Equal(90, timeline.TotalLifetimeDays);
        Assert.Equal(10, timeline.ElapsedDays);
        Assert.Equal(80, timeline.RemainingDays);
        Assert.Equal(RenewalTimelineZone.Active, timeline.CurrentZone);
        Assert.Equal("ACTIVE & HEALTHY", timeline.ZoneLabel);
        Assert.False(timeline.IsInsideAriWindow);
        Assert.Equal(50, timeline.DaysUntilAriWindow); // 60 - 10 = 50
    }

    [Fact]
    public void FromCertificate_Day65Of90_ReturnsAriRenewalWindow()
    {
        var start = DateTime.UtcNow.AddDays(-65);
        var end = DateTime.UtcNow.AddDays(25);
        var cert = CreateTestCert(start, end);

        var timeline = RenewalTimelineInfo.FromCertificate(cert);

        Assert.Equal(90, timeline.TotalLifetimeDays);
        Assert.Equal(65, timeline.ElapsedDays);
        Assert.Equal(25, timeline.RemainingDays);
        Assert.Equal(RenewalTimelineZone.AriRenewalWindow, timeline.CurrentZone);
        Assert.True(timeline.IsInsideAriWindow);
        Assert.Equal(0, timeline.DaysUntilAriWindow);
    }

    [Fact]
    public void FromCertificate_Day80Of90_ReturnsWarningZone()
    {
        var start = DateTime.UtcNow.AddDays(-80);
        var end = DateTime.UtcNow.AddDays(10); // 10 days left <= 14 days
        var cert = CreateTestCert(start, end);

        var timeline = RenewalTimelineInfo.FromCertificate(cert);

        Assert.Equal(90, timeline.TotalLifetimeDays);
        Assert.Equal(80, timeline.ElapsedDays);
        Assert.Equal(10, timeline.RemainingDays);
        Assert.Equal(RenewalTimelineZone.Warning, timeline.CurrentZone);
        Assert.Contains("WARNING", timeline.ZoneLabel);
    }

    [Fact]
    public void FromCertificate_Day89Of90_ReturnsCriticalZone()
    {
        var start = DateTime.UtcNow.AddDays(-88);
        var end = DateTime.UtcNow.AddDays(2); // 2 days left <= 3 days
        var cert = CreateTestCert(start, end);

        var timeline = RenewalTimelineInfo.FromCertificate(cert);

        Assert.Equal(RenewalTimelineZone.Critical, timeline.CurrentZone);
        Assert.Contains("CRITICAL", timeline.ZoneLabel);
    }

    [Fact]
    public void FromCertificate_PastNotAfter_ReturnsExpiredZone()
    {
        var start = DateTime.UtcNow.AddDays(-100);
        var end = DateTime.UtcNow.AddDays(-10);
        var cert = CreateTestCert(start, end);

        var timeline = RenewalTimelineInfo.FromCertificate(cert);

        Assert.Equal(RenewalTimelineZone.Expired, timeline.CurrentZone);
        Assert.Equal("EXPIRED", timeline.ZoneLabel);
        Assert.Equal(100, timeline.ProgressPercent);
        Assert.Equal(0, timeline.RemainingDays);
    }

    [Fact]
    public void FromCertificate_NullCert_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RenewalTimelineInfo.FromCertificate(null!));
    }
}
