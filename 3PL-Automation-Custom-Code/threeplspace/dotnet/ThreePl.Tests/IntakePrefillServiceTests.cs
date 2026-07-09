using ThreePl.Core.Entities;
using ThreePl.Core.Reads;

namespace ThreePl.Tests;

public class IntakePrefillServiceTests : IDisposable
{
    private const string Cid = "3PLPnP-PRE-EU-20260709000000";
    private readonly TestDbFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Prefill_MapsSavedRecordsAndChildArrays_ButNeverThePassword()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Onboardings.Add(new Onboarding
            {
                CorrelationId = Cid, CreatedAt = DateTime.UtcNow,
                InterfaceId = "ITF_WMS_010", Country = "France", RegionIso = "EU",
            });
            var solace = new SolaceClient
            {
                Brand = "petc", Env = "rc", SystemName = "nav", ThreePLCode = "3pl",
                CorrelationId = Cid, Direction = "Outbound",
                EncryptedPassword = "S3cr3t-Enc-Value",
                Action = "FullOnboarding", ServiceExists = true,
                AclClientConnectDefaultAction = "disallow",
                ClientUserEnabled = true,
            };
            solace.MessageTypes.Add(new SolaceMessageType
            {
                MessageType = "DespatchStock", Topic = "scx/whm",
                QueuePermission = "consume", QueueEgressEnabled = true, QueueMaxRedeliveryCount = 3,
            });
            db.SolaceClients.Add(solace);
            var mule = new MuleSoftPartner
            {
                CountryKey = "rc-fr", CorrelationId = Cid, Direction = "Outbound",
                NavUseCommonCert = true, ServiceExists = false,
            };
            mule.Environments.Add(new MuleSoftEnvironment { Environment = "dev", NavHost = "h", NavCompany = "c" });
            mule.TransactionTypes.Add(new MuleSoftTransactionType { TransactionTypeCode = "sal_008", TransactionTypeEnabled = true });
            mule.MessageTypes.Add(new MuleSoftMessageType { MessageType = "despatchStock" });
            mule.SourceDestinations.Add(new MuleSoftSourceDestination { SourceDestinationFrom = "PLANT-FR1", SourceDestinationTo = "spl_001" });
            mule.UomMappings.Add(new MuleSoftUomMapping { UomFrom = "EA", UomTo = "UNIT" });
            db.MuleSoftPartners.Add(mule);
            db.SaveChanges();
        }

        var service = new IntakePrefillService(_factory);
        var prefill = await service.GetPrefillAsync(Cid);

        Assert.NotNull(prefill.Common);
        Assert.Equal("ITF_WMS_010", prefill.Common!.InterfaceId);
        Assert.Equal("France", prefill.Common.Country);

        Assert.Null(prefill.Btp); // no BTP row saved

        Assert.NotNull(prefill.Solace);
        Assert.Null(prefill.Solace!.EncryptedPassword); // privacy: never prefilled
        Assert.Equal("FullOnboarding", prefill.Solace.Action);
        Assert.Equal("true", prefill.Solace.ServiceExists);
        Assert.Equal("disallow", prefill.Solace.AclClientConnectDefaultAction);
        Assert.True(prefill.Solace.ClientUserEnabled);
        var mt = Assert.Single(prefill.Solace.MessageTypes);
        Assert.Equal("DespatchStock", mt.MessageType);
        Assert.Equal(3, mt.QueueMaxRedeliveryCount);

        Assert.NotNull(prefill.MuleSoft);
        Assert.Equal("false", prefill.MuleSoft!.ServiceExists);
        Assert.True(prefill.MuleSoft.NavUseCommonCert);
        Assert.Single(prefill.MuleSoft.Environments);
        Assert.Single(prefill.MuleSoft.TransactionTypes);
        Assert.Single(prefill.MuleSoft.MessageTypes);
        Assert.Single(prefill.MuleSoft.SourceDestinations);
        Assert.Single(prefill.MuleSoft.UomMappings);
    }

    [Fact]
    public async Task Prefill_UnknownCorrelationId_AllNull()
    {
        var service = new IntakePrefillService(_factory);
        var prefill = await service.GetPrefillAsync("3PLPnP-NOPE-EU-20260101000000");
        Assert.Null(prefill.Common);
        Assert.Null(prefill.Btp);
        Assert.Null(prefill.Solace);
        Assert.Null(prefill.MuleSoft);
    }

    [Fact]
    public async Task Sessions_ComeFromTheOnboardingTable_NewestFirst()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Onboardings.Add(new Onboarding { CorrelationId = "OLD", CreatedAt = DateTime.UtcNow.AddDays(-2) });
            db.Onboardings.Add(new Onboarding { CorrelationId = "NEW", CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }
        var service = new SessionService(_factory);
        Assert.Equal(new[] { "NEW", "OLD" }, await service.GetRecentSessionsAsync());
    }
}
