namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.AdminSetting — app-owned key/value portal settings (e.g. the default
/// SME/architecture approval email addresses configured on the Admin tab).
/// Like FieldRequirement, this is portal configuration, not onboarding
/// business data: the Blazor app is the only writer.
/// </summary>
public class AdminSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = null!;
    public string? Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}
