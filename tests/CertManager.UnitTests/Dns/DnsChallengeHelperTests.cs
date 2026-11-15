using System;
using CertManager.Core.Common;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Dns;

public class DnsChallengeHelperTests
{
    [Theory]
    [InlineData("example.com", "_acme-challenge.example.com")]
    [InlineData("*.example.com", "_acme-challenge.example.com")]
    [InlineData("sub.domain.org", "_acme-challenge.sub.domain.org")]
    [InlineData("*.sub.domain.org", "_acme-challenge.sub.domain.org")]
    [InlineData("foo.bar.co.uk", "_acme-challenge.foo.bar.co.uk")]
    [InlineData("*.foo.bar.co.uk", "_acme-challenge.foo.bar.co.uk")]
    public void GetRecordName_ShouldDeriveCorrectAcmeChallengeTxtName(string input, string expected)
    {
        string recordName = DnsChallengeHelper.GetRecordName(input);
        recordName.Should().Be(expected);
    }

    [Fact]
    public void ComputeDnsTxtValue_ShouldProduceValidBase64UrlSha256Digest()
    {
        // Sample known key authorization RFC 8555
        string keyAuthz = "evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ-PCt92wr-oA.H573n6-R5s9_aP7WkF-1j9X3B5i6Y1o2";
        string dnsTxt = DnsChallengeHelper.ComputeDnsTxtValue(keyAuthz);

        dnsTxt.Should().NotBeNullOrWhiteSpace();
        dnsTxt.Should().NotContain("+");
        dnsTxt.Should().NotContain("/");
        dnsTxt.Should().NotContain("="); // RFC 7515 no padding
        dnsTxt.Length.Should().Be(43); // 32-byte SHA256 base64url encoded without padding is 43 chars
    }
}
