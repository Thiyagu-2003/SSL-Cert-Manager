using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using CertManager.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CertManager.UnitTests.Infrastructure;

public class WebhookNotificationTests
{
    [Fact]
    public async Task SendAsync_ReturnsFalse_WhenWebhookUrlIsEmpty()
    {
        var options = new WebhookChannelOptions { WebhookUrl = "" };
        using var channel = new WebhookNotificationChannel(options, null, NullLogger<WebhookNotificationChannel>.Instance);

        var msg = new NotificationMessage("Subject", "Body", "Info");
        bool sent = await channel.SendAsync(msg, CancellationToken.None);

        sent.Should().BeFalse();
    }

    [Fact]
    public void ChannelName_IsWebhook()
    {
        var options = new WebhookChannelOptions { WebhookUrl = "https://discord.com/api/webhooks/123/abc" };
        using var channel = new WebhookNotificationChannel(options, null, NullLogger<WebhookNotificationChannel>.Instance);

        channel.ChannelName.Should().Be("Webhook");
    }
}
