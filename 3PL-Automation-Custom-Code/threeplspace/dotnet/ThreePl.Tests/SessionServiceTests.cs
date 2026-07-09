using ThreePl.Core.Reads;

namespace ThreePl.Tests;

/// <summary>Parity with the HTML's buildCorrelationId/slugifyCorrelationSegment.</summary>
public class SessionServiceTests
{
    private static readonly DateTime Stamp = new(2026, 7, 9, 10, 30, 5, DateTimeKind.Utc);

    [Fact]
    public void BuildCorrelationId_SlugifiesAndTimestamps()
    {
        Assert.Equal(
            "3PLPnP-ROYALCANIN-FRANCE-20260709103005",
            SessionService.BuildCorrelationId("Royal Canin", "France", Stamp));
    }

    [Fact]
    public void BuildCorrelationId_StripsNonAlphanumerics_AndUppercases()
    {
        Assert.Equal(
            "3PLPnP-ABC123-EMEA-20260709103005",
            SessionService.BuildCorrelationId("  a-b c#1!2%3 ", "e.m/e:a", Stamp));
    }

    [Fact]
    public void BuildCorrelationId_CapsSegmentsAt24Chars()
    {
        var id = SessionService.BuildCorrelationId(new string('x', 40), "r", Stamp);
        Assert.Equal($"3PLPnP-{new string('X', 24)}-R-20260709103005", id);
    }

    [Fact]
    public void BuildCorrelationId_EmptySegments_FallBackToPlaceholders()
    {
        Assert.Equal(
            "3PLPnP-PARTNER-REGION-20260709103005",
            SessionService.BuildCorrelationId("", "!!!", Stamp));
    }
}
