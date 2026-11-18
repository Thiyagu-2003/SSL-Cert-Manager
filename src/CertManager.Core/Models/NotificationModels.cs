using System;

namespace CertManager.Core.Models;

public class NotificationSettings
{
    // Lifecycle Event Subscriptions
    public bool NotifyOnExpiryWarning { get; set; } = true;
    public int ExpiryWarningDays { get; set; } = 14;
    public bool NotifyOnRenewalSuccess { get; set; } = true;
    public bool NotifyOnRenewalFailure { get; set; } = true;
    public bool NotifyOnManualIssuance { get; set; } = true;

    // Discord Webhook
    public bool DiscordEnabled { get; set; }
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    // Slack Webhook
    public bool SlackEnabled { get; set; }
    public string SlackWebhookUrl { get; set; } = string.Empty;

    // Microsoft Teams Webhook
    public bool TeamsEnabled { get; set; }
    public string TeamsWebhookUrl { get; set; } = string.Empty;

    // Telegram Bot
    public bool TelegramEnabled { get; set; }
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;

    // Email / SMTP
    public bool SmtpEnabled { get; set; }
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 25;
    public bool SmtpEnableSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string SmtpFromAddress { get; set; } = "certflow@local";
    public string SmtpToAddress { get; set; } = string.Empty;

    // Pushover (Mobile Push for iOS & Android)
    public bool PushoverEnabled { get; set; }
    public string PushoverAppToken { get; set; } = string.Empty;
    public string PushoverUserKey { get; set; } = string.Empty;

    // Gotify (Self-hosted push notifications)
    public bool GotifyEnabled { get; set; }
    public string GotifyServerUrl { get; set; } = string.Empty;
    public string GotifyAppToken { get; set; } = string.Empty;

    // Generic Custom Webhook (JSON payload for Zapier, n8n, Make, Datadog)
    public bool GenericWebhookEnabled { get; set; }
    public string GenericWebhookUrl { get; set; } = string.Empty;
}

public class NotificationTestRequest
{
    public string ChannelType { get; set; } = string.Empty; // Discord, Slack, Teams, Telegram, Smtp, Pushover, Gotify, GenericWebhook
    public string? TargetUrl { get; set; }
    public string? Token { get; set; }
    public string? SecondaryTokenOrChatId { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public bool? SmtpSsl { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpPass { get; set; }
    public string? SmtpFrom { get; set; }
    public string? SmtpTo { get; set; }
}

public class NotificationTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
