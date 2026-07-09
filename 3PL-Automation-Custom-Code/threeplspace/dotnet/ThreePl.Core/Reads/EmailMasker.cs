namespace ThreePl.Core.Reads;

/// <summary>
/// Mirrors the ActorEmail-masking CASE expression in the enrichment-status
/// workflow's Get_Audit_Log query: null/empty → '', contains '@' →
/// LEFT(email, 2) + '***' + everything from the '@' onward, else unchanged.
/// </summary>
public static class EmailMasker
{
    public static string Mask(string? email)
    {
        if (string.IsNullOrEmpty(email)) return string.Empty;
        var at = email.IndexOf('@');
        if (at < 0) return email;
        var left = email[..Math.Min(2, email.Length)];
        return left + "***" + email[at..];
    }
}
