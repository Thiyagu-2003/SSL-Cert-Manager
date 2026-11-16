using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CertManager.Infrastructure.Data;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public class DatabaseInitializer : IDatabaseInitializer
{
    private readonly CertManagerDbContext _context;

    public DatabaseInitializer(CertManagerDbContext context)
    {
        _context = context;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.EnsureCreatedAsync(cancellationToken);
        if (_context.Database.IsSqlite())
        {
            await _context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = 'wal';", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);
        }

        if (!await _context.Notifications.AnyAsync(cancellationToken))
        {
            _context.Notifications.Add(new Core.Entities.NotificationRecord
            {
                Channel = "System",
                Level = "Info",
                Subject = "CertFlow SSL/TLS Manager Started",
                Body = "CertFlow service engine initialized. Automated certificate lifecycle management, IIS binding sync, and health monitoring are active.",
                Delivered = true,
                SentAt = System.DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
