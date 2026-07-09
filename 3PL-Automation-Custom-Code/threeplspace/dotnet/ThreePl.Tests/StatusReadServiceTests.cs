using System.Text.Json;
using ThreePl.Core.Entities;
using ThreePl.Core.Reads;

namespace ThreePl.Tests;

public class StatusReadServiceTests : IDisposable
{
    private const string Cid = "3PLPnP-TEST-EU-20260709000000";
    private readonly TestDbFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private void SeedFullOnboarding(
        string btpStatus = "Complete", string solaceStatus = "Complete", string muleStatus = "Complete")
    {
        using var db = _factory.CreateDbContext();
        db.Onboardings.Add(new Onboarding { CorrelationId = Cid, CreatedAt = DateTime.UtcNow });
        db.BtpConfigs.Add(new BtpConfig
        {
            SubAccount = "sa", ProductName = "pn", Environment = "uat",
            CorrelationId = Cid, EnrichmentStatus = btpStatus, DeploymentStatus = "Pending",
            Direction = "Outbound",
            Mode = "create", DeveloperId = "dev", Title = "t", RepoOwner = "o", RepoName = "r",
            WorkflowFileName = "w.yml", BranchRef = "main", ServiceExists = true,
            CardSentAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
        });
        var solace = new SolaceClient
        {
            Brand = "petc", Env = "rc", SystemName = "nav", ThreePLCode = "3pl",
            CorrelationId = Cid, EnrichmentStatus = solaceStatus, DeploymentStatus = "Pending",
            Direction = "Outbound",
            EncryptedPassword = "S3cr3t-Enc-Value",
            BranchApprovalStatus = "Pending", PendingBranchName = "feature/solace-x",
        };
        solace.MessageTypes.Add(new SolaceMessageType { MessageType = "DespatchStock", Topic = "scx/whm" });
        db.SolaceClients.Add(solace);
        db.MuleSoftPartners.Add(new MuleSoftPartner
        {
            CountryKey = "rc-fr", CorrelationId = Cid,
            EnrichmentStatus = muleStatus, DeploymentStatus = "Pending", Direction = "Inbound",
        });
        db.OnboardingApprovals.Add(new OnboardingApproval
        {
            CorrelationId = Cid, ArchitectureApprovalStatus = "Approved",
            ApproverEmail = "approver@example.com",
            RespondedAt = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc),
        });
        db.EnrichmentAuditLogs.Add(new EnrichmentAuditLog
        {
            Domain = "Btp", CorrelationId = Cid, Channel = "Api",
            ActorEmail = "john.doe@example.com", EventType = "Upserted",
            EventDetail = "enrichmentStatus=Complete", CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetStatus_MapsRowsToSameShapeTheUiRenders()
    {
        SeedFullOnboarding();
        var service = new StatusReadService(_factory);

        var dto = await service.GetStatusAsync(Cid);

        Assert.Equal(Cid, dto.CorrelationId);
        Assert.True(dto.Btp.Found);
        Assert.Equal("Complete", dto.Btp.EnrichmentStatus);
        Assert.Equal("Pending", dto.Btp.DeploymentStatus);
        Assert.Equal("Outbound", dto.Btp.Direction);
        Assert.NotNull(dto.Btp.CardSentAt);
        Assert.Empty(dto.Btp.MissingFields);

        Assert.True(dto.Solace.Found);
        Assert.Equal("Pending", dto.Solace.BranchApprovalStatus);
        Assert.Equal("feature/solace-x", dto.Solace.PendingBranchName);

        // MuleSoft row is Inbound and otherwise empty — the direction
        // short-circuit must keep missingFields empty.
        Assert.True(dto.MuleSoft.Found);
        Assert.Empty(dto.MuleSoft.MissingFields);

        Assert.Equal("Approved", dto.ArchitectureApproval.Status);
        Assert.Equal("approver@example.com", dto.ArchitectureApproval.ApproverEmail);
    }

    [Fact]
    public async Task GetStatus_ReadyToLaunch_RequiresAllThreeFoundAndComplete()
    {
        SeedFullOnboarding();
        var service = new StatusReadService(_factory);
        Assert.True((await service.GetStatusAsync(Cid)).ReadyToLaunch);
    }

    [Fact]
    public async Task GetStatus_NotReady_WhenAnyDomainAwaitingInput()
    {
        SeedFullOnboarding(solaceStatus: "AwaitingInput");
        var service = new StatusReadService(_factory);
        Assert.False((await service.GetStatusAsync(Cid)).ReadyToLaunch);
    }

    [Fact]
    public async Task GetStatus_MissingDomainRow_ReportsNotFound_AndNotReady()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Onboardings.Add(new Onboarding { CorrelationId = Cid, CreatedAt = DateTime.UtcNow });
            db.BtpConfigs.Add(new BtpConfig
            {
                SubAccount = "sa", ProductName = "pn", Environment = "uat",
                CorrelationId = Cid, EnrichmentStatus = "Complete", Direction = "Outbound",
                Mode = "m", DeveloperId = "d", Title = "t", RepoOwner = "o", RepoName = "r",
                WorkflowFileName = "w", BranchRef = "b", ServiceExists = true,
            });
            db.SaveChanges();
        }
        var service = new StatusReadService(_factory);

        var dto = await service.GetStatusAsync(Cid);

        Assert.True(dto.Btp.Found);
        Assert.False(dto.Solace.Found);
        Assert.False(dto.MuleSoft.Found);
        Assert.False(dto.ReadyToLaunch);
    }

    [Fact]
    public async Task GetStatus_AuditTrail_MasksActorEmail()
    {
        SeedFullOnboarding();
        var service = new StatusReadService(_factory);

        var dto = await service.GetStatusAsync(Cid);

        var entry = Assert.Single(dto.AuditTrail);
        Assert.Equal("jo***@example.com", entry.ActorEmail);
        Assert.Equal("Upserted", entry.EventType);
        Assert.Equal("Api", entry.Channel);
    }

    [Fact]
    public async Task GetStatus_SerializedDto_NeverContainsEncryptedPassword()
    {
        SeedFullOnboarding();
        var service = new StatusReadService(_factory);

        var dto = await service.GetStatusAsync(Cid);
        var json = JsonSerializer.Serialize(dto);

        Assert.DoesNotContain("S3cr3t-Enc-Value", json);
        Assert.DoesNotContain("EncryptedPassword", json, StringComparison.OrdinalIgnoreCase);
        // Raw (unmasked) actor email must not appear anywhere either.
        Assert.DoesNotContain("john.doe@example.com", json);
    }

    [Fact]
    public async Task GetStatus_UnknownCorrelationId_AllNotFound()
    {
        var service = new StatusReadService(_factory);
        var dto = await service.GetStatusAsync("3PLPnP-NOPE-EU-20260101000000");
        Assert.False(dto.Btp.Found);
        Assert.False(dto.Solace.Found);
        Assert.False(dto.MuleSoft.Found);
        Assert.False(dto.ReadyToLaunch);
        Assert.Equal("NotRequested", dto.ArchitectureApproval.Status);
        Assert.Empty(dto.AuditTrail);
    }
}
