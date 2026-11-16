using System;
using System.IO;
using System.Threading.Tasks;
using CertManager.Integrations.Iis.Challenges;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Integrations;

public class Http01ChallengeResponderTests : IDisposable
{
    private readonly string _tempWebRoot;
    private readonly Http01ChallengeResponder _responder;

    public Http01ChallengeResponderTests()
    {
        _tempWebRoot = Path.Combine(Path.GetTempPath(), $"certflow_webroot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWebRoot);
        _responder = new Http01ChallengeResponder();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWebRoot))
        {
            try { Directory.Delete(_tempWebRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task ProvisionChallengeAsync_CreatesDirectory_WebConfig_AndTokenFile()
    {
        string token = "test-token-123456";
        string keyAuth = "test-token-123456.account-thumbprint-xyz";

        string filePath = await _responder.ProvisionChallengeAsync(_tempWebRoot, token, keyAuth);

        File.Exists(filePath).Should().BeTrue();
        (await File.ReadAllTextAsync(filePath)).Should().Be(keyAuth);

        string webConfig = Path.Combine(_tempWebRoot, ".well-known", "acme-challenge", "web.config");
        File.Exists(webConfig).Should().BeTrue();
    }

    [Fact]
    public async Task CleanupChallengeAsync_RemovesTokenFile()
    {
        string token = "test-token-cleanup";
        string keyAuth = "test-token-cleanup.xyz";

        string filePath = await _responder.ProvisionChallengeAsync(_tempWebRoot, token, keyAuth);
        File.Exists(filePath).Should().BeTrue();

        await _responder.CleanupChallengeAsync(_tempWebRoot, token);
        File.Exists(filePath).Should().BeFalse();
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("")]
    public async Task ProvisionChallengeAsync_WithDirectoryTraversalToken_ThrowsArgumentException(string invalidToken)
    {
        Func<Task> act = async () => await _responder.ProvisionChallengeAsync(_tempWebRoot, invalidToken, "test-auth");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CleanupChallengeAsync_CleansUpEmptyDirectories()
    {
        string token = "test-clean-dir";
        string keyAuth = "auth-val";

        await _responder.ProvisionChallengeAsync(_tempWebRoot, token, keyAuth);
        string challengeDir = Path.Combine(_tempWebRoot, ".well-known", "acme-challenge");
        Directory.Exists(challengeDir).Should().BeTrue();

        await _responder.CleanupChallengeAsync(_tempWebRoot, token);
        Directory.Exists(challengeDir).Should().BeFalse();
    }
}
