namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.FieldRequirement — app-owned admin configuration (which intake fields
/// are mandatory), shared across users. This is the only table this app
/// writes to; onboarding business data is written exclusively by the Logic
/// App workflows.
/// </summary>
public class FieldRequirement
{
    public int Id { get; set; }
    public string Domain { get; set; } = null!;
    public string FieldName { get; set; } = null!;
    /// <summary>Always | Outbound | Optional.</summary>
    public string Level { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }
}
