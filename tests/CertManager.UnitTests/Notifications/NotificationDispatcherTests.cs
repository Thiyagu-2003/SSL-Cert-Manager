using System;
using System.IO;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using CertManager.Infrastructure.Data;
using CertManager.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CertManager.UnitTests.Notifications;

public class NotificationDispatcherTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CertManagerDbContext _context;

    public NotificationDispatcherTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"certflow_notif_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<CertManagerDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new CertManagerDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void MaskSecrets_ShouldMaskTokensAndPasswords()
    {
        string raw = "Connecting with Bearer secret-token-123456 and password: mySuperPassword999 to service token=secret-api-token-789";
        string masked = NotificationDispatcher.MaskSecrets(raw);

        masked.Should().NotContain("secret-token-123456");
        masked.Should().NotContain("mySuperPassword999");
        masked.Should().NotContain("secret-api-token-789");
        masked.Should().Contain("Bearer ******");
    }

    [Fact]
    public async Task SendAsync_ShouldDeduplicateIdenticalAlertsWithinCooldown()
    {
        var dispatcher = new NotificationDispatcher(_context, NullLogger<NotificationDispatcher>.Instance);

        string testSubject = $"Test Alert {Guid.NewGuid():N}";
        var msg1 = new NotificationMessage(testSubject, "First occurrence", "Warning");
        var msg2 = new NotificationMessage(testSubject, "Second occurrence within 1 min", "Warning");

        await dispatcher.SendAsync(msg1);
        await dispatcher.SendAsync(msg2);

        // Only first message should be stored due to duplicate suppression
        var stored = await _context.Notifications.ToListAsync();
        stored.Should().ContainSingle(n => n.Subject == testSubject);
        stored[0].Body.Should().Be("First occurrence");
    }

    [Fact]
    public async Task SendAsync_ThrowsArgumentNullException_WhenMessageIsNull()
    {
        var dispatcher = new NotificationDispatcher(_context, NullLogger<NotificationDispatcher>.Instance);
        Func<Task> act = async () => await dispatcher.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_DispatchesToConfiguredChannels()
    {
        var mockChannel = new TestNotificationChannel();
        var dispatcher = new NotificationDispatcher(_context, NullLogger<NotificationDispatcher>.Instance, new[] { mockChannel });

        string testSubject = $"Channel Test {Guid.NewGuid():N}";
        var msg = new NotificationMessage(testSubject, "Alert Body", "Info");

        await dispatcher.SendAsync(msg);

        mockChannel.DispatchedMessages.Should().ContainSingle(m => m.Subject == testSubject);
    }

    private class TestNotificationChannel : INotificationChannel
    {
        public string ChannelName => "TestChannel";
        public System.Collections.Generic.List<NotificationMessage> DispatchedMessages { get; } = new();

        public Task<bool> SendAsync(NotificationMessage message, System.Threading.CancellationToken cancellationToken = default)
        {
            DispatchedMessages.Add(message);
            return Task.FromResult(true);
        }
    }
}
