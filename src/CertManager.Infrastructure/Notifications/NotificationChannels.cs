using System;
using System.Collections.Concurrent;
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
using CertManager.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace CertManager.Infrastructure.Notifications;

public interface INotificationChannel
{
    string ChannelName { get; }
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

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ToAddress))
        {
            _logger.LogWarning("SMTP recipient address is not configured. Skipping email notification.");
            return false;
        }

        try
        {
            using var mail = new MailMessage();
            mail.From = new MailAddress(_options.FromAddress, "CertFlow Automation");
            mail.To.Add(_options.ToAddress);
            mail.Subject = $"[CertFlow {message.Level.ToUpperInvariant()}] {message.Subject}";
            mail.Body = $"{message.Body}\n\n---\nSent automatically by CertFlow SSL/TLS Manager at {DateTime.UtcNow:u}";
            mail.IsBodyHtml = false;

            using var client = new SmtpClient(_options.Host, _options.Port);
            client.EnableSsl = _options.EnableSsl;
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new NetworkCredential(_options.Username, _options.Password);
            }

            await client.SendMailAsync(mail, cancellationToken);
            _logger.LogInformation("Sent notification email to {Recipient}: {Subject}", MaskEmail(_options.ToAddress), message.Subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver SMTP notification: {Message}", ex.Message);
            return false;
        }
    }

    private static string MaskEmail(string email)
    {
        int atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***@domain.com";
        return email[0] + "***" + email[atIndex..];
    }
}

public class TelegramChannelOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}

public class TelegramNotificationChannel : INotificationChannel
{
    private readonly TelegramChannelOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramNotificationChannel> _logger;

    public TelegramNotificationChannel(
        TelegramChannelOptions options,
        HttpClient? httpClient,
        ILogger<TelegramNotificationChannel> logger)
    {
        _options = options;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public string ChannelName => "Telegram";

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.ChatId))
        {
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
                return true;
            }

            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telegram delivery returned status {Status}: {Response}", response.StatusCode, err);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver Telegram notification: {Message}", ex.Message);
            return false;
        }
    }
}

public class NotificationDispatcher : INotificationProvider
{
    private readonly CertManagerDbContext _db;
    private readonly ILogger<NotificationDispatcher> _logger;
    private static readonly ConcurrentDictionary<string, DateTime> CooldownCache = new();

    public NotificationDispatcher(CertManagerDbContext db, ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
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

        // 2. Mask any sensitive strings (tokens, private keys) before logging and storing
        string safeBody = MaskSecrets(message.Body);

        _logger.LogInformation("[ALERT {Level}] {Subject} - {Body}", message.Level, message.Subject, safeBody);

        // 3. Persist into SQLite Notification Audit Log
        try
        {
            var record = new NotificationRecord
            {
                Channel = "System/Dashboard",
                Level = message.Level,
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
