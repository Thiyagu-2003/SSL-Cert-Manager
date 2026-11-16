using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CertManager.Core.Entities;
using CertManager.Core.Interfaces;
using CertManager.Infrastructure.Data;
using CertManager.Infrastructure.Logging;
using CertManager.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CertManager.UnitTests.Notifications;

public class LoggingAndAuditTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CertManagerDbContext _context;

    public LoggingAndAuditTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"certflow_audit_test_{Guid.NewGuid():N}.db");
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
    public async Task SendAsync_ShouldPersistAuditRecords_AndWriteToFile()
    {
        var dispatcher = new NotificationDispatcher(_context, NullLogger<NotificationDispatcher>.Instance);

        var msg1 = new NotificationMessage("Certificate Request Started", "Request for test.com started", "Info");
        var msg2 = new NotificationMessage("Certificate Issued Successfully", "Thumbprint: AABBCC112233", "Info");

        await dispatcher.SendAsync(msg1);
        await dispatcher.SendAsync(msg2);

        var records = await _context.Notifications.ToListAsync();
        records.Should().HaveCount(2);
        records.Should().Contain(r => r.Subject == "Certificate Request Started");
        records.Should().Contain(r => r.Subject == "Certificate Issued Successfully");

        string logPath = FileLoggerHelper.GetLogFilePath();
        File.Exists(logPath).Should().BeTrue();
    }

    [Fact]
    public async Task DatabaseInitializer_ShouldSeedInitialNotification_WhenEmpty()
    {
        var initializer = new DatabaseInitializer(_context);
        await initializer.InitializeAsync();

        var records = await _context.Notifications.ToListAsync();
        records.Should().NotBeEmpty();
        records[0].Subject.Should().Contain("CertFlow");
    }

    [Fact]
    public void FileLoggerHelper_AppendAndClear_ShouldWorkWithoutThrowing()
    {
        string dir = FileLoggerHelper.GetLogsDirectory();
        dir.Should().NotBeNullOrWhiteSpace();

        FileLoggerHelper.AppendLog("Info", "Test Unit Subject", "Test Unit Body");
        string filePath = FileLoggerHelper.GetLogFilePath();
        File.Exists(filePath).Should().BeTrue();

        FileLoggerHelper.ClearLogFile();
        File.Exists(filePath).Should().BeTrue();
    }
}
