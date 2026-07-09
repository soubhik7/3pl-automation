using ThreePl.Core.Admin;

namespace ThreePl.Tests;

public class FieldRequirementServiceTests : IDisposable
{
    private readonly TestDbFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Defaults_MirrorTheWorkflowRules()
    {
        var service = new FieldRequirementService(_factory);
        var levels = await service.GetEffectiveLevelsAsync();

        Assert.Equal(RequirementLevel.Always, levels[("Btp", "subAccount")]);
        Assert.Equal(RequirementLevel.Outbound, levels[("Btp", "mode")]);
        Assert.Equal(RequirementLevel.Optional, levels[("Btp", "shortText")]);
        Assert.Equal(RequirementLevel.Optional, levels[("Common", "eaRef")]);
        Assert.Equal(RequirementLevel.Outbound, levels[("Solace", "messageTypes")]);
        Assert.Equal(RequirementLevel.Outbound, levels[("MuleSoft", "uomMappings")]);
    }

    [Fact]
    public async Task SetLevel_Persists_AndIsSharedAcrossServiceInstances()
    {
        var service = new FieldRequirementService(_factory);
        await service.SetLevelAsync("Btp", "mode", RequirementLevel.Optional);

        // A different instance over the same DB sees it — server-persisted,
        // unlike the old per-browser localStorage.
        var other = new FieldRequirementService(_factory);
        var levels = await other.GetEffectiveLevelsAsync();
        Assert.Equal(RequirementLevel.Optional, levels[("Btp", "mode")]);
    }

    [Fact]
    public async Task SetLevel_Twice_UpdatesTheSameRow()
    {
        var service = new FieldRequirementService(_factory);
        await service.SetLevelAsync("Btp", "mode", RequirementLevel.Optional);
        await service.SetLevelAsync("Btp", "mode", RequirementLevel.Always);

        var levels = await service.GetEffectiveLevelsAsync();
        Assert.Equal(RequirementLevel.Always, levels[("Btp", "mode")]);

        using var db = _factory.CreateDbContext();
        Assert.Single(db.FieldRequirements);
    }

    [Fact]
    public async Task LockedNaturalKeys_CannotBeOverridden()
    {
        var service = new FieldRequirementService(_factory);
        await service.SetLevelAsync("Btp", "subAccount", RequirementLevel.Optional);

        var levels = await service.GetEffectiveLevelsAsync();
        Assert.Equal(RequirementLevel.Always, levels[("Btp", "subAccount")]);
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.FieldRequirements);
    }

    [Fact]
    public async Task Reset_RestoresDefaults()
    {
        var service = new FieldRequirementService(_factory);
        await service.SetLevelAsync("Btp", "mode", RequirementLevel.Optional);
        await service.ResetAsync();

        var levels = await service.GetEffectiveLevelsAsync();
        Assert.Equal(RequirementLevel.Outbound, levels[("Btp", "mode")]);
    }

    [Fact]
    public void IsRequired_DirectionAware()
    {
        Assert.True(FieldRequirementService.IsRequired(RequirementLevel.Always, "Inbound"));
        Assert.True(FieldRequirementService.IsRequired(RequirementLevel.Outbound, "Outbound"));
        Assert.False(FieldRequirementService.IsRequired(RequirementLevel.Outbound, "Inbound"));
        Assert.False(FieldRequirementService.IsRequired(RequirementLevel.Optional, "Outbound"));
    }
}
