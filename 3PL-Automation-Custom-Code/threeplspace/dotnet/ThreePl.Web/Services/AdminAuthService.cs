using Microsoft.Extensions.Options;

namespace ThreePl.Web.Services;

/// <summary>Credentials for the Admin tab, bound from the "AdminAuth" config section.</summary>
public class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin123";
}

/// <summary>
/// Circuit-scoped gate for the Admin tab. This is a UI lock for an internal
/// portal, not a hardened auth system — the credentials live in config and
/// the flag lasts for the current browser circuit only.
/// </summary>
public class AdminAuthService
{
    private readonly AdminAuthOptions _options;

    public AdminAuthService(IOptions<AdminAuthOptions> options) => _options = options.Value;

    public bool IsAuthenticated { get; private set; }

    public bool Login(string? username, string? password)
    {
        IsAuthenticated = string.Equals(username?.Trim(), _options.Username, StringComparison.Ordinal)
            && string.Equals(password, _options.Password, StringComparison.Ordinal);
        return IsAuthenticated;
    }

    public void Logout() => IsAuthenticated = false;
}
