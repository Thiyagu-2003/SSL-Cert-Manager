using System;
using System.IO;
using System.Threading.Tasks;
using CertManager.Core.Entities;
using CertManager.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CertManager.UnitTests.Infrastructure;

public class DatabaseBackupManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly CertManagerDbContext _context;

    public DatabaseBackupManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"certflow_backup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "certflow.db");

        var options = new DbContextOptionsBuilder<CertManagerDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new CertManagerDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldGenerateValidOnlineSqliteBackup()
    {
        // Add sample data
        _context.Domains.Add(new Domain
        {
            DomainName = "backup-test.com",
            DisplayName = "backup-test.com",
            RootDomain = "backup-test.com"
        });
        await _context.SaveChangesAsync();

        var backupManager = new DatabaseBackupManager(_context, NullLogger<DatabaseBackupManager>.Instance);
        string backupDir = Path.Combine(_tempDir, "backups");
        string backupPath = await backupManager.CreateBackupAsync(backupDir);

        File.Exists(backupPath).Should().BeTrue();
        new FileInfo(backupPath).Length.Should().BeGreaterThan(0);

        // Verify we can read data from backup
        var backupOptions = new DbContextOptionsBuilder<CertManagerDbContext>()
            .UseSqlite($"Data Source={backupPath}")
            .Options;
        using var backupContext = new CertManagerDbContext(backupOptions);
        var domain = await backupContext.Domains.FirstOrDefaultAsync(d => d.DomainName == "backup-test.com");
        domain.Should().NotBeNull();
    }
}
