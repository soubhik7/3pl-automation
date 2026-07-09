using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Data;

namespace ThreePl.Core.Reads;

/// <summary>
/// Recent onboarding sessions straight from dbo.Onboarding (replacing the
/// old per-browser localStorage list) plus the correlation-id builder ported
/// from the HTML's buildCorrelationId.
/// </summary>
public class SessionService
{
    private readonly IDbContextFactory<OnboardingDbContext> _dbFactory;

    public SessionService(IDbContextFactory<OnboardingDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<IReadOnlyList<string>> GetRecentSessionsAsync(int max = 50, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Onboardings.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(max)
            .Select(x => x.CorrelationId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Correlation ID format: 3PLPnP-&lt;3PL Partner&gt;-&lt;Region&gt;-&lt;DateTimeStamp&gt;.
    /// Partner/Region are slug-ified (uppercase alphanumeric only, capped at 24)
    /// so the 4-segment shape stays parseable and fits NVARCHAR(100).
    /// </summary>
    public static string BuildCorrelationId(string? partner, string? region, DateTime? utcNow = null)
    {
        var partnerSlug = SlugifySegment(partner, 24);
        var regionSlug = SlugifySegment(region, 24);
        if (partnerSlug.Length == 0) partnerSlug = "PARTNER";
        if (regionSlug.Length == 0) regionSlug = "REGION";
        var now = utcNow ?? DateTime.UtcNow;
        return $"3PLPnP-{partnerSlug}-{regionSlug}-{now:yyyyMMddHHmmss}";
    }

    private static string SlugifySegment(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var chars = value.Trim().ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(maxLen).ToArray();
        return new string(chars);
    }
}
