namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.OnboardingApproval — one row per CorrelationId; the whole-onboarding
/// architecture approval gate.
/// </summary>
public class OnboardingApproval
{
    public int Id { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string ArchitectureApprovalStatus { get; set; } = "Pending";
    public DateTime? RequestedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? ApproverEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
