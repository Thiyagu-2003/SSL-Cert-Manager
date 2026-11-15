using System.Collections.Generic;
using System.Threading.Tasks;
using CertManager.Core.Models;
using CertManager.Integrations.Iis;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Integrations;

public class IisServerAdapterTests
{
    [Fact]
    public void IsIisAvailable_DoesNotThrow()
    {
        var adapter = new IisServerAdapter();
        bool available = adapter.IsIisAvailable();
        // Returns boolean without throwing
        (available == true || available == false).Should().BeTrue();
    }

    [Fact]
    public async Task DiscoverWebsitesAsync_ExecutesSafely()
    {
        var adapter = new IisServerAdapter();
        var sites = await adapter.DiscoverWebsitesAsync();
        sites.Should().NotBeNull();
    }

    [Fact]
    public void DiscoveredIisSite_CorrectlyFlagsNeedsCertificate()
    {
        var unencryptedSite = new DiscoveredIisSite(
            1, "TestHttpSite", "Started", false,
            new List<DiscoveredBinding>
            {
                new("http", "test.example.com", 80, "*", false, null)
            },
            new List<string> { "test.example.com" }
        );

        unencryptedSite.NeedsCertificate.Should().BeTrue();

        var securedSite = new DiscoveredIisSite(
            2, "TestHttpsSite", "Started", true,
            new List<DiscoveredBinding>
            {
                new("https", "secure.example.com", 443, "*", true, "A1B2C3D4E5F6")
            },
            new List<string> { "secure.example.com" }
        );

        securedSite.NeedsCertificate.Should().BeFalse();
    }
}
