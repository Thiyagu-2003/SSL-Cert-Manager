using System;
using System.Collections.Generic;
using CertManager.Core.Entities;
using CertManager.Service.Services;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Discovery;

public class SiteAutoDiscoveryEngineTests
{
    [Fact]
    public void FindMatchingCertificate_ShouldMatchExactHostname()
    {
        var certs = new List<CertificateRecord>
        {
            new() { Subject = "CN=app.example.com", Thumbprint = "TP-EXACT" },
            new() { Subject = "CN=other.org", Thumbprint = "TP-OTHER" }
        };

        var matched = SiteAutoDiscoveryEngine.FindMatchingCertificate("app.example.com", certs);
        matched.Should().NotBeNull();
        matched!.Thumbprint.Should().Be("TP-EXACT");
    }

    [Fact]
    public void FindMatchingCertificate_ShouldMatchWildcardCertificate()
    {
        var certs = new List<CertificateRecord>
        {
            new() { Subject = "CN=*.example.com", Thumbprint = "TP-WILDCARD" }
        };

        var match1 = SiteAutoDiscoveryEngine.FindMatchingCertificate("portal.example.com", certs);
        match1.Should().NotBeNull();
        match1!.Thumbprint.Should().Be("TP-WILDCARD");

        // Should not match completely different domain
        var match2 = SiteAutoDiscoveryEngine.FindMatchingCertificate("portal.different.com", certs);
        match2.Should().BeNull();
    }
}
