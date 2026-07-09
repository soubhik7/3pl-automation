using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Data;
using ThreePl.Core.Entities;

namespace ThreePl.Core.Admin;

/// <summary>
/// Server-persisted field-requirement configuration (dbo.FieldRequirement) —
/// shared across users, replacing the old per-browser localStorage config.
/// Overrides sit on top of the FieldDefinitions defaults; locked natural
/// keys can never be overridden. Read failures (e.g. the table not created
/// yet) degrade to the defaults instead of breaking intake.
/// </summary>
public class FieldRequirementService
{
    private readonly IDbContextFactory<OnboardingDbContext> _dbFactory;

    public FieldRequirementService(IDbContextFactory<OnboardingDbContext> dbFactory) => _dbFactory = dbFactory;

    /// <summary>Effective level for every (domain, field): defaults merged with stored overrides.</summary>
    public async Task<Dictionary<(string Domain, string Field), RequirementLevel>> GetEffectiveLevelsAsync(
        CancellationToken ct = default)
    {
        var levels = new Dictionary<(string, string), RequirementLevel>();
        foreach (var domain in FieldDefinitions.All)
            foreach (var field in domain.Fields)
                levels[(domain.Domain, field.Name)] = field.DefaultLevel;

        List<FieldRequirement> overrides;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            overrides = await db.FieldRequirements.AsNoTracking().ToListAsync(ct);
        }
        catch (Exception)
        {
            // Table missing / DB unreachable — run on defaults.
            return levels;
        }

        foreach (var o in overrides)
        {
            var def = FieldDefinitions.All.FirstOrDefault(d => d.Domain == o.Domain)?.Find(o.FieldName);
            if (def is null || def.Locked) continue;
            if (Enum.TryParse<RequirementLevel>(o.Level, ignoreCase: true, out var level))
                levels[(o.Domain, o.FieldName)] = level;
        }
        return levels;
    }

    /// <summary>Upserts one override. Locked fields are ignored.</summary>
    public async Task SetLevelAsync(string domain, string fieldName, RequirementLevel level, CancellationToken ct = default)
    {
        var def = FieldDefinitions.Get(domain).Find(fieldName);
        if (def is null || def.Locked) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.FieldRequirements
            .FirstOrDefaultAsync(x => x.Domain == domain && x.FieldName == fieldName, ct);
        if (row is null)
        {
            db.FieldRequirements.Add(new FieldRequirement
            {
                Domain = domain,
                FieldName = fieldName,
                Level = level.ToString(),
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            row.Level = level.ToString();
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes every override, restoring the workflow-parity defaults.</summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.FieldRequirements.ExecuteDeleteAsync(ct);
    }

    public static bool IsRequired(RequirementLevel level, string? direction) =>
        level == RequirementLevel.Always
        || (level == RequirementLevel.Outbound
            && !string.Equals(direction, "Inbound", StringComparison.OrdinalIgnoreCase));
}
