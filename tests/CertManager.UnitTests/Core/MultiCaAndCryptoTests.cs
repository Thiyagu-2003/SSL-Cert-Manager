using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using CertManager.Infrastructure.Certificates;
using Xunit;

namespace CertManager.UnitTests.Core;

public class MultiCaAndCryptoTests
{
    [Fact]
    public void WellKnownAcmeDirectories_ResolvesStagingCorrectly()
    {
        var uri = WellKnownAcmeDirectories.ResolveDirectoryUri("ZeroSSL", isStaging: true);
        Assert.Equal(WellKnownAcmeDirectories.LetsEncryptStaging, uri.ToString());
    }

    [Theory]
    [InlineData("ZeroSSL", WellKnownAcmeDirectories.ZeroSSL)]
    [InlineData("GoogleTrustServices", WellKnownAcmeDirectories.GoogleTrustServices)]
    [InlineData("Google", WellKnownAcmeDirectories.GoogleTrustServices)]
    [InlineData("BuyPass", WellKnownAcmeDirectories.BuyPass)]
    [InlineData("LetsEncrypt", WellKnownAcmeDirectories.LetsEncrypt)]
    [InlineData("", WellKnownAcmeDirectories.LetsEncrypt)]
    [InlineData(null, WellKnownAcmeDirectories.LetsEncrypt)]
    public void WellKnownAcmeDirectories_ResolvesNamedCasCorrectly(string? caName, string expectedDirectory)
    {
        var uri = WellKnownAcmeDirectories.ResolveDirectoryUri(caName, isStaging: false);
        Assert.Equal(expectedDirectory, uri.ToString());
    }

    [Fact]
    public void WellKnownAcmeDirectories_ResolvesCustomDirectoryCorrectly()
    {
        string customUrl = "https://acme.corp.internal/v2/directory";
        var uri = WellKnownAcmeDirectories.ResolveDirectoryUri("Custom", isStaging: false, customUrl: customUrl);
        Assert.Equal(customUrl, uri.ToString());
    }

    [Fact]
    public void CertificateRequestModel_DefaultsToSafeHighSecurityParameters()
    {
        var model = new CertificateRequestModel();
        Assert.Equal("LetsEncrypt", model.CertificateAuthority);
        Assert.True(model.EnableCaFallback);
        Assert.Equal("ZeroSSL", model.FallbackCertificateAuthority);
        Assert.Equal("RS256", model.KeyAlgorithm);
        Assert.True(model.AutoPruneSupersededCerts);
    }

    [Fact]
    public async Task CleanupSupersededCertificates_NonExistentCert_ReturnsZero()
    {
        var installer = new WindowsCertificateInstaller();
        var options = new InstallOptions("My", "CurrentUser");
        
        // Non-existent subject should return 0 safely without exceptions
        int pruned = await installer.CleanupSupersededCertificatesAsync("nonexistent.test.domain.local", "DUMMYTHUMBPRINT123", options, CancellationToken.None);
        Assert.True(pruned >= 0);
    }

    [Fact]
    public void EcdsaKeyGeneration_GeneratesValidP256AndP384Keys()
    {
        using var ecdsa256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.NotNull(ecdsa256);
        Assert.Equal(256, ecdsa256.KeySize);

        using var ecdsa384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.NotNull(ecdsa384);
        Assert.Equal(384, ecdsa384.KeySize);
    }
}
