using CertManager.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CertManager.Infrastructure.Data;

public class CertManagerDbContext : DbContext
{
    public CertManagerDbContext(DbContextOptions<CertManagerDbContext> options)
        : base(options)
    {
    }

    public DbSet<Domain> Domains => Set<Domain>();
    public DbSet<CertificateProfile> CertificateProfiles => Set<CertificateProfile>();
    public DbSet<ProfileDomain> ProfileDomains => Set<ProfileDomain>();
    public DbSet<CertificateRecord> Certificates => Set<CertificateRecord>();
    public DbSet<CertificateDomain> CertificateDomains => Set<CertificateDomain>();
    public DbSet<AcmeOrder> AcmeOrders => Set<AcmeOrder>();
    public DbSet<AcmeChallenge> AcmeChallenges => Set<AcmeChallenge>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowLock> WorkflowLocks => Set<WorkflowLock>();
    public DbSet<IisWebsite> IisWebsites => Set<IisWebsite>();
    public DbSet<IisBinding> IisBindings => Set<IisBinding>();
    public DbSet<OperationHistory> OperationHistories => Set<OperationHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Domains
        modelBuilder.Entity<Domain>(b =>
        {
            b.HasKey(e => e.DomainId);
            b.HasIndex(e => e.DomainName).IsUnique();
            b.Property(e => e.DomainName).IsRequired().HasMaxLength(255);
            b.Property(e => e.DisplayName).IsRequired().HasMaxLength(255);
            b.Property(e => e.RootDomain).IsRequired().HasMaxLength(255);
        });

        // CertificateProfiles
        modelBuilder.Entity<CertificateProfile>(b =>
        {
            b.HasKey(e => e.ProfileId);
            b.Property(e => e.ProfileName).IsRequired().HasMaxLength(150);
            b.Property(e => e.AuthorityId).IsRequired().HasMaxLength(50);
            b.Property(e => e.KeyAlgorithm).IsRequired().HasMaxLength(20);
            b.Property(e => e.KeySizeOrCurve).IsRequired().HasMaxLength(20);
        });

        // ProfileDomains
        modelBuilder.Entity<ProfileDomain>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasOne(e => e.Profile)
                .WithMany(p => p.Domains)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Domain)
                .WithMany(d => d.ProfileDomains)
                .HasForeignKey(e => e.DomainId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Certificates
        modelBuilder.Entity<CertificateRecord>(b =>
        {
            b.HasKey(e => e.CertificateId);
            b.HasIndex(e => e.Thumbprint);
            b.Property(e => e.Thumbprint).IsRequired().HasMaxLength(128);
            b.Property(e => e.Subject).IsRequired().HasMaxLength(500);

            b.HasOne(e => e.Profile)
                .WithMany(p => p.Certificates)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CertificateDomains
        modelBuilder.Entity<CertificateDomain>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasOne(e => e.Certificate)
                .WithMany(c => c.CertificateDomains)
                .HasForeignKey(e => e.CertificateId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Domain)
                .WithMany(d => d.CertificateDomains)
                .HasForeignKey(e => e.DomainId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // AcmeOrders
        modelBuilder.Entity<AcmeOrder>(b =>
        {
            b.HasKey(e => e.AcmeOrderId);
            b.HasIndex(e => e.IdempotencyKey).IsUnique();
            b.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(128);

            b.HasOne(e => e.Profile)
                .WithMany(p => p.AcmeOrders)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AcmeChallenges
        modelBuilder.Entity<AcmeChallenge>(b =>
        {
            b.HasKey(e => e.ChallengeId);
            b.HasOne(e => e.AcmeOrder)
                .WithMany(o => o.Challenges)
                .HasForeignKey(e => e.AcmeOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Domain)
                .WithMany()
                .HasForeignKey(e => e.DomainId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // WorkflowSteps
        modelBuilder.Entity<WorkflowStep>(b =>
        {
            b.HasKey(e => e.StepId);
            b.HasIndex(e => e.OperationId);
        });

        // WorkflowLocks
        modelBuilder.Entity<WorkflowLock>(b =>
        {
            b.HasKey(e => e.LockKey);
            b.Property(e => e.LockKey).HasMaxLength(150);
        });

        // IisWebsites
        modelBuilder.Entity<IisWebsite>(b =>
        {
            b.HasKey(e => e.WebsiteId);
            b.Property(e => e.WebsiteName).IsRequired().HasMaxLength(255);
        });

        // IisBindings
        modelBuilder.Entity<IisBinding>(b =>
        {
            b.HasKey(e => e.BindingId);
            b.HasOne(e => e.Website)
                .WithMany(w => w.Bindings)
                .HasForeignKey(e => e.WebsiteId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Certificate)
                .WithMany()
                .HasForeignKey(e => e.CertificateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // OperationHistory
        modelBuilder.Entity<OperationHistory>(b =>
        {
            b.HasKey(e => e.OperationId);
        });
    }
}
