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

        // Delete existing file if present, as SQLite VACUUM INTO fails if destination exists
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

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

        var connectionString = _context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        string currentDbPath = builder.DataSource;
        if (string.IsNullOrWhiteSpace(currentDbPath))
        {
            throw new InvalidOperationException("Database file path could not be determined from connection string.");
        }

        _logger.LogInformation("Restoring database from {Backup} to {Current}...", backupFilePath, currentDbPath);

        // Close connection and clear SQLite connection pools to release file handles
        _context.Database.CloseConnection();
        SqliteConnection.ClearAllPools();

        // Remove stale WAL and SHM files to prevent SQLite from replaying old WAL frames onto restored database
        string walFile = $"{currentDbPath}-wal";
        string shmFile = $"{currentDbPath}-shm";
        if (File.Exists(walFile))
        {
            try { File.Delete(walFile); } catch { /* ignore */ }
        }
        if (File.Exists(shmFile))
        {
            try { File.Delete(shmFile); } catch { /* ignore */ }
        }

        File.Copy(backupFilePath, currentDbPath, overwrite: true);
        _logger.LogInformation("Database restore completed successfully.");

        return Task.CompletedTask;
    }
}
