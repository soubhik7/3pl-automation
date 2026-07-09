namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.SolaceClient — one Solace client (client-profile/ACL/user settings).
/// Natural key: Brand + Env + SystemName + ThreePLCode.
/// EncryptedPassword is mapped for EF completeness but must never be copied
/// into any DTO that reaches the browser.
/// </summary>
public class SolaceClient
{
    public int Id { get; set; }
    public string Brand { get; set; } = null!;
    public string Env { get; set; } = null!;
    public string SystemName { get; set; } = null!;
    public string ThreePLCode { get; set; } = null!;
    public string? EncryptedPassword { get; set; }
    public string? Action { get; set; }
    public bool? ClientProfileAllowGuaranteedMsgSendEnabled { get; set; }
    public bool? ClientProfileAllowGuaranteedMsgReceiveEnabled { get; set; }
    public bool? ClientProfileCompressionEnabled { get; set; }
    public bool? ClientProfileReplicationAllowClientConnectWhenStandbyEnabled { get; set; }
    public bool? ClientProfileAllowTransactedSessionsEnabled { get; set; }
    public bool? ClientProfileAllowBridgeConnectionsEnabled { get; set; }
    public bool? ClientProfileAllowGuaranteedEndpointCreateEnabled { get; set; }
    public bool? ClientProfileAllowSharedSubscriptionsEnabled { get; set; }
    public string? AclClientConnectDefaultAction { get; set; }
    public string? AclPublishTopicDefaultAction { get; set; }
    public string? AclSubscribeShareNameDefaultAction { get; set; }
    public string? AclSubscribeTopicDefaultAction { get; set; }
    public bool? ClientUserEnabled { get; set; }
    public bool? ClientUserGuaranteedEndpointPermissionOverrideEnabled { get; set; }
    public bool? ClientUserSubscriptionManagerEnabled { get; set; }

    // Pipeline / GitHub publish metadata
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? FilePath { get; set; }
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

    public ICollection<SolaceMessageType> MessageTypes { get; set; } = new List<SolaceMessageType>();
}
