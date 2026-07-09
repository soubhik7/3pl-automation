using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Entities;

namespace ThreePl.Core.Data;

/// <summary>
/// Read-focused DbContext over the existing dbo tables (schema.sql stays
/// authoritative — no migrations are ever run against the live DB). The only
/// table this context writes to is the app-owned <see cref="FieldRequirement"/>.
/// </summary>
public class OnboardingDbContext : DbContext
{
    public OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : base(options) { }

    public DbSet<Onboarding> Onboardings => Set<Onboarding>();
    public DbSet<BtpConfig> BtpConfigs => Set<BtpConfig>();
    public DbSet<SolaceClient> SolaceClients => Set<SolaceClient>();
    public DbSet<SolaceMessageType> SolaceMessageTypes => Set<SolaceMessageType>();
    public DbSet<MuleSoftPartner> MuleSoftPartners => Set<MuleSoftPartner>();
    public DbSet<MuleSoftEnvironment> MuleSoftEnvironments => Set<MuleSoftEnvironment>();
    public DbSet<MuleSoftTransactionType> MuleSoftTransactionTypes => Set<MuleSoftTransactionType>();
    public DbSet<MuleSoftMessageType> MuleSoftMessageTypes => Set<MuleSoftMessageType>();
    public DbSet<MuleSoftSourceDestination> MuleSoftSourceDestinations => Set<MuleSoftSourceDestination>();
    public DbSet<MuleSoftUomMapping> MuleSoftUomMappings => Set<MuleSoftUomMapping>();
    public DbSet<EnrichmentAuditLog> EnrichmentAuditLogs => Set<EnrichmentAuditLog>();
    public DbSet<OnboardingApproval> OnboardingApprovals => Set<OnboardingApproval>();
    public DbSet<FieldRequirement> FieldRequirements => Set<FieldRequirement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Onboarding>(e =>
        {
            e.ToTable("Onboarding", "dbo");
            e.HasKey(x => x.CorrelationId);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
        });

        modelBuilder.Entity<BtpConfig>(e =>
        {
            e.ToTable("BtpConfig", "dbo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SubAccount, x.ProductName, x.Environment }).IsUnique();
            e.HasIndex(x => x.CorrelationId);
        });

        modelBuilder.Entity<SolaceClient>(e =>
        {
            e.ToTable("SolaceClient", "dbo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Brand, x.Env, x.SystemName, x.ThreePLCode }).IsUnique();
            e.HasIndex(x => x.CorrelationId);
            e.HasMany(x => x.MessageTypes)
                .WithOne(x => x.Client)
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SolaceMessageType>(e =>
        {
            e.ToTable("SolaceMessageType", "dbo");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<MuleSoftPartner>(e =>
        {
            e.ToTable("MuleSoftPartner", "dbo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CountryKey).IsUnique();
            e.HasIndex(x => x.CorrelationId);
            e.HasMany(x => x.Environments).WithOne(x => x.Partner).HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.TransactionTypes).WithOne(x => x.Partner).HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MessageTypes).WithOne(x => x.Partner).HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SourceDestinations).WithOne(x => x.Partner).HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.UomMappings).WithOne(x => x.Partner).HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MuleSoftEnvironment>(e => { e.ToTable("MuleSoftEnvironment", "dbo"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<MuleSoftTransactionType>(e => { e.ToTable("MuleSoftTransactionType", "dbo"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<MuleSoftMessageType>(e => { e.ToTable("MuleSoftMessageType", "dbo"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<MuleSoftSourceDestination>(e => { e.ToTable("MuleSoftSourceDestination", "dbo"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<MuleSoftUomMapping>(e => { e.ToTable("MuleSoftUomMapping", "dbo"); e.HasKey(x => x.Id); });

        modelBuilder.Entity<EnrichmentAuditLog>(e =>
        {
            e.ToTable("EnrichmentAuditLog", "dbo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CorrelationId);
        });

        modelBuilder.Entity<OnboardingApproval>(e =>
        {
            e.ToTable("OnboardingApproval", "dbo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CorrelationId).IsUnique();
        });

        modelBuilder.Entity<FieldRequirement>(e =>
        {
            e.ToTable("FieldRequirement", "dbo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Domain, x.FieldName }).IsUnique();
            e.Property(x => x.Domain).HasMaxLength(20);
            e.Property(x => x.FieldName).HasMaxLength(100);
            e.Property(x => x.Level).HasMaxLength(20);
        });
    }
}
