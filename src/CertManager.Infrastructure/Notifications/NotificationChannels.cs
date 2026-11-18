using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Entities;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using CertManager.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace CertManager.Infrastructure.Notifications;

public interface INotificationChannel
{
    string ChannelName { get; }
    string? LastError { get; }
    Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

public class SmtpChannelOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "certflow@local";
    public string ToAddress { get; set; } = string.Empty;
}

public class SmtpNotificationChannel : INotificationChannel
{
    private readonly SmtpChannelOptions _options;
    private readonly ILogger<SmtpNotificationChannel> _logger;

    public SmtpNotificationChannel(SmtpChannelOptions options, ILogger<SmtpNotificationChannel> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ChannelName => "SMTP";
    public string? LastError { get; private set; }

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            LastError = "SMTP server hostname or IP address cannot be empty.";
            _logger.LogWarning("SMTP host is not configured. Skipping email notification.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.ToAddress))
        {
            LastError = "Recipient email address is not configured.";
            _logger.LogWarning("SMTP recipient address is not configured. Skipping email notification.");
            return false;
        }

        try
        {
            using var mail = new MailMessage();
            string from = string.IsNullOrWhiteSpace(_options.FromAddress) ? "certflow@local" : _options.FromAddress;
            mail.From = new MailAddress(from, "CertFlow Automation");

            var recipients = _options.ToAddress.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (recipients.Length == 0)
            {
                LastError = "No recipient email addresses were provided.";
                _logger.LogWarning("No valid SMTP recipient addresses configured.");
                return false;
            }

            int validRecipients = 0;
            foreach (var recipient in recipients)
            {
                try
                {
                    mail.To.Add(recipient);
                    validRecipients++;
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning("Skipping invalid SMTP recipient '{Recipient}': {Message}", recipient, ex.Message);
                }
            }

            if (validRecipients == 0)
            {
                LastError = $"Recipient address '{_options.ToAddress}' is not a valid email format.";
                _logger.LogWarning("No valid SMTP recipients parsed from: {ToAddress}", _options.ToAddress);
                return false;
            }

            mail.Subject = $"[CertFlow {message.Level.ToUpperInvariant()}] {message.Subject}";
            mail.Body = $"{message.Body}\n\n---\nSent automatically by CertFlow SSL/TLS Manager at {DateTime.UtcNow:u}";
            mail.IsBodyHtml = false;

            using var client = new SmtpClient(_options.Host, _options.Port);
            client.Timeout = 15000;
            client.EnableSsl = _options.EnableSsl;
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new NetworkCredential(_options.Username, _options.Password);
            }

            await client.SendMailAsync(mail, cancellationToken);
            _logger.LogInformation("Sent notification email to {Recipient}: {Subject}", MaskEmail(_options.ToAddress), message.Subject);
            LastError = null;
            return true;
        }
        catch (SmtpException ex)
        {
            string detail = FormatSmtpException(ex, _options);
            _logger.LogError(ex, "Failed to deliver SMTP notification: {Message}", detail);
            LastError = detail;
            return false;
        }
        catch (Exception ex)
        {
            string detail = $"SMTP delivery failed: {ex.Message}" + (ex.InnerException != null ? $" ({ex.InnerException.Message})" : "");
            _logger.LogError(ex, "Failed to deliver SMTP notification: {Message}", detail);
            LastError = detail;
            return false;
        }
    }

    private static string FormatSmtpException(SmtpException ex, SmtpChannelOptions options)
    {
        string statusInfo = ex.StatusCode != SmtpStatusCode.GeneralFailure ? $" [Code: {(int)ex.StatusCode} {ex.StatusCode}]" : "";
        string inner = ex.InnerException != null ? $": {ex.InnerException.Message}" : "";

        if (ex.StatusCode == SmtpStatusCode.MustIssueStartTlsFirst || 
            ex.Message.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase))
        {
            return $"SMTP server requires SSL/TLS. Please enable the 'Enable SSL/TLS' option for {options.Host}:{options.Port}.";
        }
        if (ex.StatusCode == SmtpStatusCode.MailboxUnavailable)
        {
            return $"SMTP mailbox unavailable (550). Check sender '{options.FromAddress}' and recipient '{options.ToAddress}'.";
        }
        if (ex.StatusCode == SmtpStatusCode.ClientNotPermitted || ex.Message.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
        {
            return $"SMTP authentication failed{statusInfo}. Please verify username and password for {options.Host}.";
        }

        return $"SMTP error{statusInfo}: {ex.Message}{inner}";
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "***@domain.com";
        return string.Join(", ", parts.Select(p =>
        {
            int atIndex = p.IndexOf('@');
            if (atIndex <= 1) return "***@domain.com";
            return p[0] + "***" + p[atIndex..];
        }));
    }
}

public class TelegramChannelOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}

public class TelegramNotificationChannel : INotificationChannel, IDisposable
{
    private readonly TelegramChannelOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly ILogger<TelegramNotificationChannel> _logger;

    public TelegramNotificationChannel(
        TelegramChannelOptions options,
        HttpClient? httpClient,
        ILogger<TelegramNotificationChannel> logger)
    {
        _options = options;
        _ownsClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    public string ChannelName => "Telegram";
    public string? LastError { get; private set; }

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.ChatId))
        {
            LastError = "Telegram bot token and chat ID must both be configured.";
            _logger.LogDebug("Telegram bot token or chat ID is not configured. Skipping.");
            return false;
        }

        try
        {
            string emoji = message.Level.ToLowerInvariant() switch
            {
                "error" => "🚨",
                "warning" => "⚠️",
                _ => "🛡️"
            };

            string text = $"{emoji} *CertFlow Alert: {message.Subject}*\n\n{message.Body}\n\n_Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC_";

            var payload = new
            {
                chat_id = _options.ChatId,
                text = text,
                parse_mode = "Markdown"
            };

            string url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram alert delivered successfully for: {Subject}", message.Subject);
                LastError = null;
                return true;
            }

            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telegram delivery returned status {Status}: {Response}", response.StatusCode, err);
            LastError = $"Telegram API returned HTTP {(int)response.StatusCode}: {err}";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver Telegram notification: {Message}", ex.Message);
            LastError = $"Telegram delivery failed: {ex.Message}";
            return false;
        }
    }
}

public class WebhookChannelOptions
{
    public string WebhookUrl { get; set; } = string.Empty;
}

public class WebhookNotificationChannel : INotificationChannel, IDisposable
{
    private readonly WebhookChannelOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly ILogger<WebhookNotificationChannel> _logger;

    public WebhookNotificationChannel(
        WebhookChannelOptions options,
        HttpClient? httpClient,
        ILogger<WebhookNotificationChannel> logger)
    {
        _options = options;
        _ownsClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public string ChannelName => "Webhook";
    public string? LastError { get; private set; }

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            LastError = "Webhook endpoint URL is empty.";
            return false;
        }

        try
        {
            string url = _options.WebhookUrl.Trim();
            int color = message.Level.ToLowerInvariant() switch
            {
                "error" => 0xE11D48,
                "warning" => 0xD97706,
                _ => 0x16A34A
            };

            string safeSubject = NotificationDispatcher.MaskSecrets(message.Subject);
            string safeBody = NotificationDispatcher.MaskSecrets(message.Body);

            object payload;

            if (url.Contains("discord.com/api/webhooks", StringComparison.OrdinalIgnoreCase))
            {
                payload = new
                {
                    username = "CertFlow SSL Automation",
                    embeds = new[]
                    {
                        new
                        {
                            title = safeSubject,
                            description = safeBody,
                            color = color,
                            timestamp = DateTime.UtcNow.ToString("o"),
                            footer = new { text = "CertFlow Automated Lifecycle Manager" }
                        }
                    }
                };
            }
            else if (url.Contains("hooks.slack.com", StringComparison.OrdinalIgnoreCase))
            {
                string hexColor = message.Level.ToLowerInvariant() switch
                {
                    "error" => "#e11d48",
                    "warning" => "#d97706",
                    _ => "#16a34a"
                };

                payload = new
                {
                    text = $"*[CertFlow {message.Level.ToUpperInvariant()}]* {safeSubject}",
                    attachments = new[]
                    {
                        new
                        {
                            color = hexColor,
                            title = safeSubject,
                            text = safeBody,
                            footer = "CertFlow Automated Lifecycle Manager",
                            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        }
                    }
                };
            }
            else if (url.Contains("office.com", StringComparison.OrdinalIgnoreCase) ||
                     url.Contains("logic.azure.com", StringComparison.OrdinalIgnoreCase) ||
                     url.Contains("teams", StringComparison.OrdinalIgnoreCase))
            {
                string themeColor = message.Level.ToLowerInvariant() switch
                {
                    "error" => "E11D48",
                    "warning" => "D97706",
                    _ => "0284C7"
                };

                payload = new
                {
                    @type = "MessageCard",
                    @context = "http://schema.org/extensions",
                    themeColor = themeColor,
                    summary = safeSubject,
                    sections = new[]
                    {
                        new
                        {
                            activityTitle = safeSubject,
                            activitySubtitle = "CertFlow Automated Lifecycle Manager",
                            facts = new[]
                            {
                                new { name = "Severity", value = message.Level.ToUpperInvariant() },
                                new { name = "Details", value = safeBody },
                                new { name = "Timestamp", value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC") }
                            },
                            markdown = true
                        }
                    }
                };
            }
            else
            {
                payload = new
                {
                    title = $"[CertFlow {message.Level.ToUpperInvariant()}] {safeSubject}",
                    text = safeBody,
                    level = message.Level,
                    timestamp = DateTime.UtcNow
                };
            }

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Webhook alert delivered successfully for: {Subject}", message.Subject);
                LastError = null;
                return true;
            }

            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Webhook delivery failed with HTTP {Status}: {Err}", response.StatusCode, err);
            LastError = $"Webhook server returned HTTP {(int)response.StatusCode}: {err}";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver Webhook notification: {Message}", ex.Message);
            LastError = $"Webhook delivery failed: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

public class PushoverChannelOptions
{
    public string AppToken { get; set; } = string.Empty;
    public string UserKey { get; set; } = string.Empty;
}

public class PushoverNotificationChannel : INotificationChannel, IDisposable
{
    private readonly PushoverChannelOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly ILogger<PushoverNotificationChannel> _logger;

    public PushoverNotificationChannel(PushoverChannelOptions options, HttpClient? httpClient, ILogger<PushoverNotificationChannel> logger)
    {
        _options = options;
        _ownsClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public string ChannelName => "Pushover";
    public string? LastError { get; private set; }

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(_options.AppToken) || string.IsNullOrWhiteSpace(_options.UserKey))
        {
            LastError = "Pushover App Token and User Key are required.";
            return false;
        }

        try
        {
            int priority = message.Level.ToLowerInvariant() switch
            {
                "error" => 1,
                "warning" => 0,
                _ => -1
            };

            var postData = new Dictionary<string, string>
            {
                { "token", _options.AppToken.Trim() },
                { "user", _options.UserKey.Trim() },
                { "title", $"[CertFlow {message.Level.ToUpperInvariant()}] {NotificationDispatcher.MaskSecrets(message.Subject)}" },
                { "message", NotificationDispatcher.MaskSecrets(message.Body) },
                { "priority", priority.ToString() }
            };

            using var content = new FormUrlEncodedContent(postData);
            using var response = await _httpClient.PostAsync("https://api.pushover.net/1/messages.json", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                LastError = null;
                return true;
            }

            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            LastError = $"Pushover API returned HTTP {(int)response.StatusCode}: {err}";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver Pushover notification: {Message}", ex.Message);
            LastError = $"Pushover delivery failed: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}

public class GotifyChannelOptions
{
    public string ServerUrl { get; set; } = string.Empty;
    public string AppToken { get; set; } = string.Empty;
}

public class GotifyNotificationChannel : INotificationChannel, IDisposable
{
    private readonly GotifyChannelOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly ILogger<GotifyNotificationChannel> _logger;

    public GotifyNotificationChannel(GotifyChannelOptions options, HttpClient? httpClient, ILogger<GotifyNotificationChannel> logger)
    {
        _options = options;
        _ownsClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public string ChannelName => "Gotify";
    public string? LastError { get; private set; }

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(_options.ServerUrl) || string.IsNullOrWhiteSpace(_options.AppToken))
        {
            LastError = "Gotify Server URL and App Token are required.";
            return false;
        }

        try
        {
            int priority = message.Level.ToLowerInvariant() switch
            {
                "error" => 8,
                "warning" => 5,
                _ => 2
            };

            var payload = new
            {
                title = $"[CertFlow {message.Level.ToUpperInvariant()}] {NotificationDispatcher.MaskSecrets(message.Subject)}",
                message = NotificationDispatcher.MaskSecrets(message.Body),
                priority = priority
            };

            string endpoint = $"{_options.ServerUrl.TrimEnd('/')}/message";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("X-Gotify-Key", _options.AppToken.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                LastError = null;
                return true;
            }

            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            LastError = $"Gotify API returned HTTP {(int)response.StatusCode}: {err}";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver Gotify notification: {Message}", ex.Message);
            LastError = $"Gotify delivery failed: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}

public static class NotificationChannelFactory
{
    public static readonly HttpClient SharedNotificationHttpClient = new();

    public static List<INotificationChannel> CreateChannelsFromSettings(
        NotificationSettings settings,
        ILoggerFactory? loggerFactory = null)
    {
        var channels = new List<INotificationChannel>();
        var factory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

        // Discord Webhook
        if (settings.DiscordEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            channels.Add(new WebhookNotificationChannel(
                new WebhookChannelOptions { WebhookUrl = settings.DiscordWebhookUrl },
                SharedNotificationHttpClient,
                factory.CreateLogger<WebhookNotificationChannel>()));
        }

        // Slack Webhook
        if (settings.SlackEnabled && !string.IsNullOrWhiteSpace(settings.SlackWebhookUrl))
        {
            channels.Add(new WebhookNotificationChannel(
                new WebhookChannelOptions { WebhookUrl = settings.SlackWebhookUrl },
                SharedNotificationHttpClient,
                factory.CreateLogger<WebhookNotificationChannel>()));
        }

        // Teams Webhook
        if (settings.TeamsEnabled && !string.IsNullOrWhiteSpace(settings.TeamsWebhookUrl))
        {
            channels.Add(new WebhookNotificationChannel(
                new WebhookChannelOptions { WebhookUrl = settings.TeamsWebhookUrl },
                SharedNotificationHttpClient,
                factory.CreateLogger<WebhookNotificationChannel>()));
        }

        // Generic Custom Webhook
        if (settings.GenericWebhookEnabled && !string.IsNullOrWhiteSpace(settings.GenericWebhookUrl))
        {
            channels.Add(new WebhookNotificationChannel(
                new WebhookChannelOptions { WebhookUrl = settings.GenericWebhookUrl },
                SharedNotificationHttpClient,
                factory.CreateLogger<WebhookNotificationChannel>()));
        }

        // Telegram Bot
        if (settings.TelegramEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            channels.Add(new TelegramNotificationChannel(
                new TelegramChannelOptions { BotToken = settings.TelegramBotToken, ChatId = settings.TelegramChatId },
                SharedNotificationHttpClient,
                factory.CreateLogger<TelegramNotificationChannel>()));
        }

        // SMTP Email
        if (settings.SmtpEnabled && !string.IsNullOrWhiteSpace(settings.SmtpToAddress))
        {
            channels.Add(new SmtpNotificationChannel(
                new SmtpChannelOptions
                {
                    Host = settings.SmtpHost,
                    Port = settings.SmtpPort,
                    EnableSsl = settings.SmtpEnableSsl,
                    Username = settings.SmtpUsername,
                    Password = settings.SmtpPassword,
                    FromAddress = settings.SmtpFromAddress,
                    ToAddress = settings.SmtpToAddress
                },
                factory.CreateLogger<SmtpNotificationChannel>()));
        }

        // Pushover
        if (settings.PushoverEnabled && !string.IsNullOrWhiteSpace(settings.PushoverAppToken) && !string.IsNullOrWhiteSpace(settings.PushoverUserKey))
        {
            channels.Add(new PushoverNotificationChannel(
                new PushoverChannelOptions { AppToken = settings.PushoverAppToken, UserKey = settings.PushoverUserKey },
                SharedNotificationHttpClient,
                factory.CreateLogger<PushoverNotificationChannel>()));
        }

        // Gotify
        if (settings.GotifyEnabled && !string.IsNullOrWhiteSpace(settings.GotifyServerUrl) && !string.IsNullOrWhiteSpace(settings.GotifyAppToken))
        {
            channels.Add(new GotifyNotificationChannel(
                new GotifyChannelOptions { ServerUrl = settings.GotifyServerUrl, AppToken = settings.GotifyAppToken },
                SharedNotificationHttpClient,
                factory.CreateLogger<GotifyNotificationChannel>()));
        }

        return channels;
    }
}

public class NotificationDispatcher : INotificationProvider
{
    private readonly CertManagerDbContext _db;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly IEnumerable<INotificationChannel>? _explicitChannels;
    private readonly ILoggerFactory? _loggerFactory;
    private static readonly ConcurrentDictionary<string, DateTime> CooldownCache = new();

    public NotificationDispatcher(
        CertManagerDbContext db,
        ILogger<NotificationDispatcher> logger,
        IEnumerable<INotificationChannel>? channels = null,
        ILoggerFactory? loggerFactory = null)
    {
        _db = db;
        _logger = logger;
        _explicitChannels = channels;
        _loggerFactory = loggerFactory;
    }

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = NotificationSettingsManager.CurrentSettings;

        // Check event subscriptions
        if (message.Subject.Contains("Expiring Soon", StringComparison.OrdinalIgnoreCase) && !settings.NotifyOnExpiryWarning)
        {
            _logger.LogInformation("Skipping notification dispatch for '{Subject}' because NotifyOnExpiryWarning is disabled.", message.Subject);
            return;
        }
        if (message.Subject.Contains("Renewed Successfully", StringComparison.OrdinalIgnoreCase) && !settings.NotifyOnRenewalSuccess)
        {
            _logger.LogInformation("Skipping notification dispatch for '{Subject}' because NotifyOnRenewalSuccess is disabled.", message.Subject);
            return;
        }
        if (message.Subject.Contains("Renewal Failed", StringComparison.OrdinalIgnoreCase) && !settings.NotifyOnRenewalFailure)
        {
            _logger.LogInformation("Skipping notification dispatch for '{Subject}' because NotifyOnRenewalFailure is disabled.", message.Subject);
            return;
        }

        // 1. Duplicate Suppression Check: prevent spamming identical alerts within 60 minutes
        string deduplicationKey = $"{message.Level}:{message.Subject}";
        if (CooldownCache.TryGetValue(deduplicationKey, out var lastSentTime))
        {
            if (DateTime.UtcNow - lastSentTime < TimeSpan.FromMinutes(60))
            {
                _logger.LogInformation("Suppressing duplicate notification for '{Subject}' (sent at {LastSent})", message.Subject, lastSentTime);
                return;
            }
        }

        CooldownCache[deduplicationKey] = DateTime.UtcNow;

        // Periodic eviction to prevent unbounded memory growth in 24/7 service
        if (CooldownCache.Count > 500)
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(60);
            foreach (var kvp in CooldownCache)
            {
                if (kvp.Value < cutoff)
                {
                    CooldownCache.TryRemove(kvp.Key, out _);
                }
            }
        }

        // 2. Mask any sensitive strings (tokens, private keys) before logging and storing
        string safeBody = MaskSecrets(message.Body);
        string level = string.IsNullOrWhiteSpace(message.Level) ? "Info" : message.Level;

        _logger.LogInformation("[ALERT {Level}] {Subject} - {Body}", level, message.Subject, safeBody);

        // Always log to physical rolling file
        CertManager.Infrastructure.Logging.FileLoggerHelper.AppendLog(level, message.Subject, safeBody);

        // 3. Resolve active dispatch channels
        IEnumerable<INotificationChannel> channelsToDispatch;
        if (_explicitChannels != null && _explicitChannels.Any())
        {
            channelsToDispatch = _explicitChannels;
        }
        else
        {
            channelsToDispatch = NotificationChannelFactory.CreateChannelsFromSettings(settings, _loggerFactory);
        }

        try
        {
            // Dispatch to external channels (Teams, Discord, Slack, Telegram, SMTP, Pushover, Gotify, Custom Webhook)
            foreach (var channel in channelsToDispatch)
            {
                try
                {
                    await channel.SendAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispatch notification via channel {Channel}", channel.ChannelName);
                }
            }
        }
        finally
        {
            if (_explicitChannels == null)
            {
                foreach (var channel in channelsToDispatch)
                {
                    if (channel is IDisposable disp)
                    {
                        try { disp.Dispose(); } catch { }
                    }
                }
            }
        }

        // 4. Persist into SQLite Notification Audit Log
        try
        {
            var record = new NotificationRecord
            {
                Channel = "System/Dashboard",
                Level = level,
                Subject = message.Subject,
                Body = safeBody,
                Delivered = true,
                SentAt = DateTime.UtcNow
            };

            _db.Notifications.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save notification audit record in database.");
        }
    }

    public static async Task<NotificationTestResult> SendTestAlertAsync(NotificationTestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string testSubject = $"Test Alert: {request.ChannelType}";
        string testBody = $"This is a verification test notification sent by CertFlow SSL/TLS Automation Manager. Your {request.ChannelType} alert destination is successfully connected and ready to receive real-time certificate lifecycle events!";
        var msg = new NotificationMessage(testSubject, testBody, "Info");

        try
        {
            INotificationChannel? channel = null;

            switch (request.ChannelType.ToLowerInvariant())
            {
                case "discord":
                case "slack":
                case "teams":
                case "genericwebhook":
                case "webhook":
                    if (string.IsNullOrWhiteSpace(request.TargetUrl))
                    {
                        return new NotificationTestResult { Success = false, Message = "Webhook URL cannot be empty." };
                    }
                    channel = new WebhookNotificationChannel(
                        new WebhookChannelOptions { WebhookUrl = request.TargetUrl.Trim() },
                        NotificationChannelFactory.SharedNotificationHttpClient,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookNotificationChannel>.Instance);
                    break;

                case "telegram":
                    if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.SecondaryTokenOrChatId))
                    {
                        return new NotificationTestResult { Success = false, Message = "Telegram bot token and chat ID are required." };
                    }
                    channel = new TelegramNotificationChannel(
                        new TelegramChannelOptions { BotToken = request.Token.Trim(), ChatId = request.SecondaryTokenOrChatId.Trim() },
                        NotificationChannelFactory.SharedNotificationHttpClient,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramNotificationChannel>.Instance);
                    break;

                case "smtp":
                case "email":
                    if (string.IsNullOrWhiteSpace(request.SmtpHost))
                    {
                        return new NotificationTestResult { Success = false, Message = "SMTP server host is required." };
                    }
                    if (string.IsNullOrWhiteSpace(request.SmtpTo))
                    {
                        return new NotificationTestResult { Success = false, Message = "Recipient email address is required." };
                    }
                    channel = new SmtpNotificationChannel(
                        new SmtpChannelOptions
                        {
                            Host = request.SmtpHost.Trim(),
                            Port = request.SmtpPort ?? 25,
                            EnableSsl = request.SmtpSsl ?? true,
                            Username = request.SmtpUser,
                            Password = request.SmtpPass,
                            FromAddress = string.IsNullOrWhiteSpace(request.SmtpFrom) ? "certflow@local" : request.SmtpFrom.Trim(),
                            ToAddress = request.SmtpTo.Trim()
                        },
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpNotificationChannel>.Instance);
                    break;

                case "pushover":
                    if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.SecondaryTokenOrChatId))
                    {
                        return new NotificationTestResult { Success = false, Message = "Pushover App Token and User Key are required." };
                    }
                    channel = new PushoverNotificationChannel(
                        new PushoverChannelOptions { AppToken = request.Token.Trim(), UserKey = request.SecondaryTokenOrChatId.Trim() },
                        NotificationChannelFactory.SharedNotificationHttpClient,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<PushoverNotificationChannel>.Instance);
                    break;

                case "gotify":
                    if (string.IsNullOrWhiteSpace(request.TargetUrl) || string.IsNullOrWhiteSpace(request.Token))
                    {
                        return new NotificationTestResult { Success = false, Message = "Gotify server URL and app token are required." };
                    }
                    channel = new GotifyNotificationChannel(
                        new GotifyChannelOptions { ServerUrl = request.TargetUrl.Trim(), AppToken = request.Token.Trim() },
                        NotificationChannelFactory.SharedNotificationHttpClient,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<GotifyNotificationChannel>.Instance);
                    break;

                default:
                    return new NotificationTestResult { Success = false, Message = $"Unknown channel type '{request.ChannelType}'." };
            }

            bool delivered = await channel.SendAsync(msg, cancellationToken);
            string? channelError = channel.LastError;
            if (channel is IDisposable d) d.Dispose();

            return delivered
                ? new NotificationTestResult { Success = true, Message = $"Test notification delivered to {request.ChannelType} successfully!" }
                : new NotificationTestResult { Success = false, Message = channelError ?? $"Delivery to {request.ChannelType} failed. Please verify endpoint credentials." };
        }
        catch (Exception ex)
        {
            return new NotificationTestResult { Success = false, Message = $"Error delivering test alert: {ex.Message}" };
        }
    }

    public static string MaskSecrets(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Mask Cloudflare tokens, Bearer tokens, or passwords
        string masked = Regex.Replace(input, @"(Bearer\s+)([A-Za-z0-9_\-\.]{6,})", "$1******", RegexOptions.IgnoreCase);
        masked = Regex.Replace(masked, @"(token[:=]\s*)([A-Za-z0-9_\-\.]{6,})", "$1******", RegexOptions.IgnoreCase);
        masked = Regex.Replace(masked, @"(password[:=]\s*)([^\s,;]+)", "$1******", RegexOptions.IgnoreCase);
        return masked;
    }
}
