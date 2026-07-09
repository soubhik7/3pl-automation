namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.MuleSoftPartner — one partner/country onboarding (partner-level NAV
/// connection fields). Natural key: CountryKey.
/// </summary>
public class MuleSoftPartner
{
    public int Id { get; set; }
    public string CountryKey { get; set; } = null!;
    public string? CountryCode { get; set; }
    public string? PartnerComment { get; set; }
    public string? CreatedBy { get; set; }
    public string? NavProtocol { get; set; }
    public string? NavPort { get; set; }
    public string? NavUsername { get; set; }
    public string? NavDomain { get; set; }
    public string? NavService { get; set; }
    public string? NavSoapPort { get; set; }
    public bool? NavUseCommonCert { get; set; }
    public string? TranslationReceiverName { get; set; }

    // Pipeline / GitHub publish metadata
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? FilePathPrefix { get; set; }
    public string? Branch { get; set; }
    public string? BaseBranch { get; set; }
    public string? FeatureBranchName { get; set; }
    public string? RequesterEmail { get; set; }
    public string? RecipientEmail { get; set; }
    public string? CommitMessage { get; set; }
    public bool? ServiceExists { get; set; }
    public string DeploymentStatus { get; set; } = "Pending";
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string EnrichmentStatus { get; set; } = "AwaitingInput";
    public DateTime? CardSentAt { get; set; }
    public DateTime? CardRespondedAt { get; set; }
    public string Direction { get; set; } = "Outbound";
    public string? BranchApprovalStatus { get; set; }
    public string? PendingBranchName { get; set; }

    public ICollection<MuleSoftEnvironment> Environments { get; set; } = new List<MuleSoftEnvironment>();
    public ICollection<MuleSoftTransactionType> TransactionTypes { get; set; } = new List<MuleSoftTransactionType>();
    public ICollection<MuleSoftMessageType> MessageTypes { get; set; } = new List<MuleSoftMessageType>();
    public ICollection<MuleSoftSourceDestination> SourceDestinations { get; set; } = new List<MuleSoftSourceDestination>();
    public ICollection<MuleSoftUomMapping> UomMappings { get; set; } = new List<MuleSoftUomMapping>();
}
