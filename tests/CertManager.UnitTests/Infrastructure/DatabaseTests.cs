using System;
using System.IO;
using System.Threading.Tasks;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CertManager.UnitTests.Infrastructure;

public class DatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CertManagerDbContext _context;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"certmanager_test_{Guid.NewGuid():N}.db");
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
    public async Task CanInsertAndRetrieve_DomainAndCertificateProfile()
    {
        var domain = new Domain
        {
            DomainName = "example.com",
            DisplayName = "example.com",
            RootDomain = "example.com",
            DomainType = DomainType.Apex
        };

        var profile = new CertificateProfile
        {
            ProfileName = "Production Web Profile",
            AuthorityId = "letsencrypt",
            DefaultValidationMethod = ValidationMethod.Http01,
            RenewalThresholdPercent = 33,
            UseAri = true
        };

        var profileDomain = new ProfileDomain
        {
            Profile = profile,
            Domain = domain,
            IdentifierType = DomainType.Apex
        };

        _context.Domains.Add(domain);
        _context.CertificateProfiles.Add(profile);
        _context.ProfileDomains.Add(profileDomain);
        await _context.SaveChangesAsync();

        var retrievedProfile = await _context.CertificateProfiles
            .Include(p => p.Domains)
            .ThenInclude(pd => pd.Domain)
            .FirstOrDefaultAsync(p => p.ProfileName == "Production Web Profile");

        retrievedProfile.Should().NotBeNull();
        retrievedProfile!.Domains.Should().HaveCount(1);
        retrievedProfile.Domains.First().Domain!.DomainName.Should().Be("example.com");
    }

    [Fact]
    public async Task AcmeOrders_EnforcesUniqueIdempotencyKey()
    {
        var profile = new CertificateProfile
        {
            ProfileName = "Order Test Profile"
        };
        _context.CertificateProfiles.Add(profile);
        await _context.SaveChangesAsync();

        var order1 = new AcmeOrder
        {
            ProfileId = profile.ProfileId,
            IdempotencyKey = "unique-order-hash-12345",
            Status = AcmeOrderStatus.Pending
        };

        var order2 = new AcmeOrder
        {
            ProfileId = profile.ProfileId,
            IdempotencyKey = "unique-order-hash-12345",
            Status = AcmeOrderStatus.Pending
        };

        _context.AcmeOrders.Add(order1);
        await _context.SaveChangesAsync();

        _context.AcmeOrders.Add(order2);
        Func<Task> act = async () => await _context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task WorkflowLocks_SupportsKeyAndLeaseExpiry()
    {
        var opId = Guid.NewGuid();
        var workflowLock = new WorkflowLock
        {
            LockKey = "domain:example.com",
            OwnerOperationId = opId,
            AcquiredAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        _context.WorkflowLocks.Add(workflowLock);
        await _context.SaveChangesAsync();

        var retrieved = await _context.WorkflowLocks.FindAsync("domain:example.com");
        retrieved.Should().NotBeNull();
        retrieved!.OwnerOperationId.Should().Be(opId);
    }
}
