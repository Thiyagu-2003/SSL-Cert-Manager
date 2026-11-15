using System;
using CertManager.Core.Common;
using CertManager.Core.Enums;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Core;

public class DomainNormalizerTests
{
    [Theory]
    [InlineData("example.com", "example.com", "example.com", DomainType.Apex)]
    [InlineData("EXAMPLE.COM", "example.com", "example.com", DomainType.Apex)]
    [InlineData("  example.com  ", "example.com", "example.com", DomainType.Apex)]
    [InlineData("example.com.", "example.com", "example.com", DomainType.Apex)]
    [InlineData("sub.example.com", "sub.example.com", "sub.example.com", DomainType.Hostname)]
    [InlineData("*.example.com", "*.example.com", "*.example.com", DomainType.Wildcard)]
    [InlineData("*.sub.example.com", "*.sub.example.com", "*.sub.example.com", DomainType.Wildcard)]
    public void Normalize_ValidStandardDomains_ReturnsExpectedResult(
        string input, string expectedAscii, string expectedDisplay, DomainType expectedType)
    {
        var result = DomainNormalizer.Normalize(input);

        result.AsciiName.Should().Be(expectedAscii);
        result.DisplayName.Should().Be(expectedDisplay);
        result.DomainType.Should().Be(expectedType);
    }

    [Fact]
    public void Normalize_InternationalizedDomain_ConvertsToPunycodeA_Label()
    {
        var input = "münchen.de";
        var result = DomainNormalizer.Normalize(input);

        result.AsciiName.Should().Be("xn--mnchen-3ya.de");
        result.DisplayName.Should().Be("münchen.de");
        result.DomainType.Should().Be(DomainType.Apex);
    }

    [Fact]
    public void Normalize_WildcardInternationalizedDomain_ConvertsCorrectly()
    {
        var input = "*.münchen.de";
        var result = DomainNormalizer.Normalize(input);

        result.AsciiName.Should().Be("*.xn--mnchen-3ya.de");
        result.DisplayName.Should().Be("*.münchen.de");
        result.DomainType.Should().Be(DomainType.Wildcard);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_EmptyOrWhitespace_ThrowsArgumentException(string? input)
    {
        Action act = () => DomainNormalizer.Normalize(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("sub.*.example.com")]
    [InlineData("*sub.example.com")]
    [InlineData("example.*")]
    [InlineData("*")]
    public void Normalize_InvalidWildcardPosition_ThrowsArgumentException(string input)
    {
        Action act = () => DomainNormalizer.Normalize(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_ExtractsRootDomainCorrectly()
    {
        var result = DomainNormalizer.Normalize("api.service.example.com");
        result.RootDomain.Should().Be("example.com");

        var wildcardResult = DomainNormalizer.Normalize("*.example.com");
        wildcardResult.RootDomain.Should().Be("example.com");
    }
}
