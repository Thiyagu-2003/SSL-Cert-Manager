using System;
using CertManager.Core.Common;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Models;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Application;

public class RenewalPolicyEvaluatorTests
{
    [Fact]
    public void ShouldRenew_ReturnsFalse_WhenCertificateIsFresh()
    {
        // 90-day certificate issued today
        var cert = new CertificateRecord
        {
            NotBefore = DateTime.UtcNow,
            NotAfter = DateTime.UtcNow.AddDays(90),
            Status = CertificateStatus.Active
        };

        var profile = new CertificateProfile
        {
            RenewalThresholdPercent = 33 // Renew when <= 33% of lifetime remains (i.e. <= 29.7 days)
        };

        bool shouldRenew = RenewalPolicyEvaluator.ShouldRenew(cert, profile, DateTime.UtcNow);
        shouldRenew.Should().BeFalse();
    }

    [Fact]
    public void ShouldRenew_ReturnsTrue_WhenRemainingLifetimeIsBelowThreshold()
    {
        // 90-day certificate issued 65 days ago (25 days remaining, which is ~27.7% of lifetime <= 33%)
        var cert = new CertificateRecord
        {
            NotBefore = DateTime.UtcNow.AddDays(-65),
            NotAfter = DateTime.UtcNow.AddDays(25),
            Status = CertificateStatus.Active
        };

        var profile = new CertificateProfile
        {
            RenewalThresholdPercent = 33
        };

        bool shouldRenew = RenewalPolicyEvaluator.ShouldRenew(cert, profile, DateTime.UtcNow);
        shouldRenew.Should().BeTrue();
    }

    [Fact]
    public void ShouldRenew_ReturnsTrue_WhenCertificateIsAlreadyExpired()
    {
        var cert = new CertificateRecord
        {
            NotBefore = DateTime.UtcNow.AddDays(-100),
            NotAfter = DateTime.UtcNow.AddDays(-10),
            Status = CertificateStatus.Active
        };

        var profile = new CertificateProfile
        {
            RenewalThresholdPercent = 33
        };

        bool shouldRenew = RenewalPolicyEvaluator.ShouldRenew(cert, profile, DateTime.UtcNow);
        shouldRenew.Should().BeTrue();
    }

    [Fact]
    public void CalculateLifetimeProgressPercent_ReturnsAccurateFraction()
    {
        // 100 days total lifetime, 50 days elapsed -> 50%
        var cert = new CertificateRecord
        {
            NotBefore = DateTime.UtcNow.AddDays(-50),
            NotAfter = DateTime.UtcNow.AddDays(50)
        };

        int progress = RenewalPolicyEvaluator.CalculateLifetimeProgressPercent(cert, DateTime.UtcNow);
        progress.Should().Be(50);
    }

    [Fact]
    public void Methods_ThrowArgumentNullException_WhenCertIsNull()
    {
        Action act1 = () => RenewalPolicyEvaluator.ShouldRenew(null!, null);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => RenewalPolicyEvaluator.CalculateLifetimeProgressPercent(null!);
        act2.Should().Throw<ArgumentNullException>();

        Action act3 = () => RenewalPolicyEvaluator.GetRemainingDays(null!);
        act3.Should().Throw<ArgumentNullException>();
    }
}
