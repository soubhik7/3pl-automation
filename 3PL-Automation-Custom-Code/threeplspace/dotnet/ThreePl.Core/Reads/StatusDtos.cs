namespace ThreePl.Core.Reads;

/// <summary>
/// Same shape the current UI renders from the enrichment-status workflow's
/// 200 response. Deliberately contains no EncryptedPassword and only masked
/// ActorEmail values — these DTOs go to the browser.
/// </summary>
public sealed class OnboardingStatusDto
{
    public string CorrelationId { get; init; } = "";
    public bool ReadyToLaunch { get; init; }
    public ArchitectureApprovalDto ArchitectureApproval { get; init; } = new();
    public DomainStatusDto Btp { get; init; } = DomainStatusDto.NotFound();
    public DomainStatusDto Solace { get; init; } = DomainStatusDto.NotFound();
    public DomainStatusDto MuleSoft { get; init; } = DomainStatusDto.NotFound();
    public IReadOnlyList<AuditEntryDto> AuditTrail { get; init; } = Array.Empty<AuditEntryDto>();
}

public sealed class ArchitectureApprovalDto
{
    public string Status { get; init; } = "NotRequested";
    public string? ApproverEmail { get; init; }
    public DateTime? RespondedAt { get; init; }
}

public sealed class DomainStatusDto
{
    public bool Found { get; init; }
    public string EnrichmentStatus { get; init; } = "AwaitingInput";
    public string DeploymentStatus { get; init; } = "Pending";
    public DateTime? CardSentAt { get; init; }
    public DateTime? CardRespondedAt { get; init; }
    public string Direction { get; init; } = "Outbound";
    public string? BranchApprovalStatus { get; init; }
    public string? PendingBranchName { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();

    public static DomainStatusDto NotFound() => new();
}

public sealed class AuditEntryDto
{
    public long Id { get; init; }
    public string Domain { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public string? EntityKey { get; init; }
    public string Channel { get; init; } = "";
    /// <summary>Always masked (see <see cref="EmailMasker"/>).</summary>
    public string ActorEmail { get; init; } = "";
    public string EventType { get; init; } = "";
    public string? EventDetail { get; init; }
    public DateTime CreatedAt { get; init; }
}
