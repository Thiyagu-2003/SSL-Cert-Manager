using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using CertManager.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CertManager.UnitTests.Notifications;

public class NotificationChannelTests
{
    [Fact]
    public async Task Pushover_SendAsync_ReturnsFalse_WhenEmpty()
    {
        var options = new PushoverChannelOptions { AppToken = "", UserKey = "" };
        using var channel = new PushoverNotificationChannel(options, null, NullLogger<PushoverNotificationChannel>.Instance);

        var msg = new NotificationMessage("Subject", "Body", "Info");
        bool sent = await channel.SendAsync(msg, CancellationToken.None);

        sent.Should().BeFalse();
        channel.ChannelName.Should().Be("Pushover");
    }

    [Fact]
    public async Task Gotify_SendAsync_ReturnsFalse_WhenEmpty()
    {
        var options = new GotifyChannelOptions { ServerUrl = "", AppToken = "" };
        using var channel = new GotifyNotificationChannel(options, null, NullLogger<GotifyNotificationChannel>.Instance);

        var msg = new NotificationMessage("Subject", "Body", "Info");
        bool sent = await channel.SendAsync(msg, CancellationToken.None);

        sent.Should().BeFalse();
        channel.ChannelName.Should().Be("Gotify");
    }

    [Fact]
    public void NotificationSettingsManager_ProvidesDefaultSettings()
    {
        var settings = NotificationSettingsManager.LoadSettings();
        settings.Should().NotBeNull();
        settings.ExpiryWarningDays.Should().BeGreaterThan(0);
        settings.NotifyOnExpiryWarning.Should().BeTrue();
        settings.NotifyOnRenewalSuccess.Should().BeTrue();
        settings.NotifyOnRenewalFailure.Should().BeTrue();
    }

    [Fact]
    public void NotificationDispatcher_MasksSensitiveInformation()
    {
        string raw = "Token token=secret_token_123456 and Bearer abcdef123456789 and password=SuperSecretPassword123!";
        string masked = NotificationDispatcher.MaskSecrets(raw);

        masked.Should().NotContain("secret_token_123456");
        masked.Should().NotContain("abcdef123456789");
        masked.Should().NotContain("SuperSecretPassword123!");
        masked.Should().Contain("******");
    }

    [Fact]
    public void NotificationChannelFactory_CreatesEnabledChannels()
    {
        var settings = new NotificationSettings
        {
            DiscordEnabled = true,
            DiscordWebhookUrl = "https://discord.com/api/webhooks/123/xyz",
            TeamsEnabled = true,
            TeamsWebhookUrl = "https://outlook.office.com/webhook/teams123",
            TelegramEnabled = true,
            TelegramBotToken = "bot123456",
            TelegramChatId = "chat999",
            PushoverEnabled = true,
            PushoverAppToken = "app_token",
            PushoverUserKey = "user_key",
            GotifyEnabled = true,
            GotifyServerUrl = "https://gotify.local",
            GotifyAppToken = "gotify_token"
        };

        var channels = NotificationChannelFactory.CreateChannelsFromSettings(settings);
        channels.Should().HaveCount(5);
        channels.Should().Contain(c => c.ChannelName == "Webhook");
        channels.Should().Contain(c => c.ChannelName == "Telegram");
        channels.Should().Contain(c => c.ChannelName == "Pushover");
        channels.Should().Contain(c => c.ChannelName == "Gotify");
    }

    [Fact]
    public async Task SendTestAlertAsync_ReturnsError_WhenMissingCredentials()
    {
        var req = new NotificationTestRequest
        {
            ChannelType = "Teams",
            TargetUrl = ""
        };

        var result = await NotificationDispatcher.SendTestAlertAsync(req);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Webhook URL cannot be empty");
    }
}
