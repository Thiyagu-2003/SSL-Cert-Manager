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

    [Fact]
    public void FindMatchingCertificate_ShouldMatchDistinguishedNameSubject()
    {
        var certs = new List<CertificateRecord>
        {
            new() { Subject = "CN=secure.company.com, O=Company Inc, C=US", Thumbprint = "TP-DN" }
        };

        var match = SiteAutoDiscoveryEngine.FindMatchingCertificate("secure.company.com", certs);
        match.Should().NotBeNull();
        match!.Thumbprint.Should().Be("TP-DN");
    }

    [Fact]
    public void FindMatchingCertificate_ShouldMatchSubjectAlternativeNames()
    {
        var certs = new List<CertificateRecord>
        {
            new()
            {
                Subject = "CN=primary.example.com",
                Thumbprint = "TP-SAN",
                CertificateDomains = new List<CertificateDomain>
                {
                    new() { Domain = new Domain { DomainName = "alt1.example.com" } },
                    new() { Domain = new Domain { DomainName = "alt2.example.com" } }
                }
            }
        };

        var match = SiteAutoDiscoveryEngine.FindMatchingCertificate("alt2.example.com", certs);
        match.Should().NotBeNull();
        match!.Thumbprint.Should().Be("TP-SAN");
    }
}
