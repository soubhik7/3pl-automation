using ThreePl.Core.Reads;

namespace ThreePl.Tests;

/// <summary>
/// Parity with the SQL CASE expression in enrichment-status's Get_Audit_Log:
/// LEFT(email,2) + '***' + SUBSTRING(email, CHARINDEX('@',email), 320).
/// </summary>
public class EmailMaskerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("not-an-email", "not-an-email")]
    [InlineData("john.doe@example.com", "jo***@example.com")]
    [InlineData("ab@x.io", "ab***@x.io")]
    // 1-char local part: SQL LEFT(email,2) grabs "a@" — mirrored exactly.
    [InlineData("a@x.com", "a@***@x.com")]
    public void Mask_MatchesSqlCaseExpression(string? input, string expected)
    {
        Assert.Equal(expected, EmailMasker.Mask(input));
    }
}
