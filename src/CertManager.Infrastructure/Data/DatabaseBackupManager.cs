using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CertManager.Infrastructure.Data;

public interface IDatabaseBackupManager
{
    Task<string> CreateBackupAsync(string targetDirectory, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
}

public class DatabaseBackupManager : IDatabaseBackupManager
{
    private readonly CertManagerDbContext _context;
    private readonly ILogger<DatabaseBackupManager> _logger;

    public DatabaseBackupManager(CertManagerDbContext context, ILogger<DatabaseBackupManager> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> CreateBackupAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string backupFileName = $"certflow_backup_{timestamp}.db";
        string backupPath = Path.Combine(targetDirectory, backupFileName);

        _logger.LogInformation("Creating point-in-time SQLite backup at {BackupPath}...", backupPath);

        // Execute SQLite VACUUM INTO for consistent online transactional snapshot
        var connection = _context.Database.GetDbConnection();
        bool wasOpen = connection.State == System.Data.ConnectionState.Open;

        if (!wasOpen)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("SQLite backup completed successfully ({Size} bytes).", new FileInfo(backupPath).Length);
            return backupPath;
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }

    public Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException($"Backup file not found at '{backupFilePath}'.", backupFilePath);
        }

        string currentDbPath = _context.Database.GetConnectionString() ?? string.Empty;
        if (currentDbPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            currentDbPath = currentDbPath["Data Source=".Length..].Trim();
        }

        _logger.LogInformation("Restoring database from {Backup} to {Current}...", backupFilePath, currentDbPath);

        // Close connection and copy file
        _context.Database.CloseConnection();
        File.Copy(backupFilePath, currentDbPath, overwrite: true);
        _logger.LogInformation("Database restore completed successfully.");

        return Task.CompletedTask;
    }
}
