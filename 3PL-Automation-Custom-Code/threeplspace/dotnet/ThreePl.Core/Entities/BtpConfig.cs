namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.BtpConfig — one deployable BTP app-creation/deployment entity.
/// Natural key: SubAccount + ProductName + Environment.
/// </summary>
public class BtpConfig
{
    public int Id { get; set; }
    public string SubAccount { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string Environment { get; set; } = null!;
    public string? Mode { get; set; }
    public string? DeveloperId { get; set; }
    public string? Title { get; set; }
    public string? ShortText { get; set; }
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? WorkflowFileName { get; set; }
    public string? BranchRef { get; set; }
    public bool? ServiceExists { get; set; }
    public string DeploymentStatus { get; set; } = "Pending";
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string EnrichmentStatus { get; set; } = "AwaitingInput";
    public DateTime? CardSentAt { get; set; }
    public DateTime? CardRespondedAt { get; set; }
    public string Direction { get; set; } = "Outbound";
}
