using ThreePl.Core.Entities;

namespace ThreePl.Core.Reads;

/// <summary>
/// Pure, direction-aware mirror of the required-field lists in the
/// data-enrichment workflow's Compose_{Domain}_Enrichment_Status expressions
/// (three3pllogicapp/data-enrichment/workflow.json). Inbound rows skip SME
/// enrichment entirely, so they never have missing fields. Field names are
/// the camelCase payload names the current UI shows as chips.
/// </summary>
public static class MissingFieldRules
{
    public static bool IsInbound(string? direction) =>
        string.Equals(direction, "Inbound", StringComparison.OrdinalIgnoreCase);

    private static bool IsEmpty(string? value) => string.IsNullOrEmpty(value);

    /// <summary>Mirrors Compose_Btp_Enrichment_Status.</summary>
    public static IReadOnlyList<string> ForBtp(BtpConfig row)
    {
        if (IsInbound(row.Direction)) return Array.Empty<string>();
        var missing = new List<string>();
        if (IsEmpty(row.Mode)) missing.Add("mode");
        if (IsEmpty(row.DeveloperId)) missing.Add("developerId");
        if (IsEmpty(row.Title)) missing.Add("title");
        if (IsEmpty(row.RepoOwner)) missing.Add("repoOwner");
        if (IsEmpty(row.RepoName)) missing.Add("repoName");
        if (IsEmpty(row.WorkflowFileName)) missing.Add("workflowFileName");
        if (IsEmpty(row.BranchRef)) missing.Add("branchRef");
        if (row.ServiceExists is null) missing.Add("serviceExists");
        return missing;
    }

    /// <summary>Mirrors Compose_Solace_Enrichment_Status (incl. the messageTypes row-count gate).</summary>
    public static IReadOnlyList<string> ForSolace(SolaceClient row, int messageTypeCount)
    {
        if (IsInbound(row.Direction)) return Array.Empty<string>();
        var missing = new List<string>();
        if (IsEmpty(row.Action)) missing.Add("action");
        if (IsEmpty(row.AclClientConnectDefaultAction)) missing.Add("aclClientConnectDefaultAction");
        if (IsEmpty(row.AclPublishTopicDefaultAction)) missing.Add("aclPublishTopicDefaultAction");
        if (IsEmpty(row.AclSubscribeShareNameDefaultAction)) missing.Add("aclSubscribeShareNameDefaultAction");
        if (IsEmpty(row.AclSubscribeTopicDefaultAction)) missing.Add("aclSubscribeTopicDefaultAction");
        if (row.ClientProfileAllowGuaranteedMsgSendEnabled is null) missing.Add("clientProfileAllowGuaranteedMsgSendEnabled");
        if (row.ClientProfileAllowGuaranteedMsgReceiveEnabled is null) missing.Add("clientProfileAllowGuaranteedMsgReceiveEnabled");
        if (row.ClientProfileCompressionEnabled is null) missing.Add("clientProfileCompressionEnabled");
        if (row.ClientProfileReplicationAllowClientConnectWhenStandbyEnabled is null) missing.Add("clientProfileReplicationAllowClientConnectWhenStandbyEnabled");
        if (row.ClientProfileAllowTransactedSessionsEnabled is null) missing.Add("clientProfileAllowTransactedSessionsEnabled");
        if (row.ClientProfileAllowBridgeConnectionsEnabled is null) missing.Add("clientProfileAllowBridgeConnectionsEnabled");
        if (row.ClientProfileAllowGuaranteedEndpointCreateEnabled is null) missing.Add("clientProfileAllowGuaranteedEndpointCreateEnabled");
        if (row.ClientProfileAllowSharedSubscriptionsEnabled is null) missing.Add("clientProfileAllowSharedSubscriptionsEnabled");
        if (row.ClientUserEnabled is null) missing.Add("clientUserEnabled");
        if (row.ClientUserGuaranteedEndpointPermissionOverrideEnabled is null) missing.Add("clientUserGuaranteedEndpointPermissionOverrideEnabled");
        if (row.ClientUserSubscriptionManagerEnabled is null) missing.Add("clientUserSubscriptionManagerEnabled");
        if (IsEmpty(row.RepoOwner)) missing.Add("repoOwner");
        if (IsEmpty(row.RepoName)) missing.Add("repoName");
        if (IsEmpty(row.FilePath)) missing.Add("filePath");
        if (IsEmpty(row.Branch)) missing.Add("branch");
        if (IsEmpty(row.BaseBranch)) missing.Add("baseBranch");
        if (IsEmpty(row.FeatureBranchName)) missing.Add("featureBranchName");
        if (IsEmpty(row.RequesterEmail)) missing.Add("requesterEmail");
        if (IsEmpty(row.RecipientEmail)) missing.Add("recipientEmail");
        if (IsEmpty(row.CommitMessage)) missing.Add("commitMessage");
        if (row.ServiceExists is null) missing.Add("serviceExists");
        if (messageTypeCount == 0) missing.Add("messageTypes");
        return missing;
    }

    /// <summary>Mirrors Compose_MuleSoft_Enrichment_Status (incl. the five child row-count gates).</summary>
    public static IReadOnlyList<string> ForMuleSoft(
        MuleSoftPartner row,
        int environmentCount,
        int transactionTypeCount,
        int messageTypeCount,
        int sourceDestinationCount,
        int uomMappingCount)
    {
        if (IsInbound(row.Direction)) return Array.Empty<string>();
        var missing = new List<string>();
        if (IsEmpty(row.CountryCode)) missing.Add("countryCode");
        if (IsEmpty(row.PartnerComment)) missing.Add("partnerComment");
        if (IsEmpty(row.CreatedBy)) missing.Add("createdBy");
        if (IsEmpty(row.NavProtocol)) missing.Add("navProtocol");
        if (IsEmpty(row.NavPort)) missing.Add("navPort");
        if (IsEmpty(row.NavUsername)) missing.Add("navUsername");
        if (IsEmpty(row.NavDomain)) missing.Add("navDomain");
        if (IsEmpty(row.NavService)) missing.Add("navService");
        if (IsEmpty(row.NavSoapPort)) missing.Add("navSoapPort");
        if (IsEmpty(row.TranslationReceiverName)) missing.Add("translationReceiverName");
        if (row.NavUseCommonCert is null) missing.Add("navUseCommonCert");
        if (IsEmpty(row.RepoOwner)) missing.Add("repoOwner");
        if (IsEmpty(row.RepoName)) missing.Add("repoName");
        if (IsEmpty(row.FilePathPrefix)) missing.Add("filePathPrefix");
        if (IsEmpty(row.Branch)) missing.Add("branch");
        if (IsEmpty(row.BaseBranch)) missing.Add("baseBranch");
        if (IsEmpty(row.FeatureBranchName)) missing.Add("featureBranchName");
        if (IsEmpty(row.RequesterEmail)) missing.Add("requesterEmail");
        if (IsEmpty(row.RecipientEmail)) missing.Add("recipientEmail");
        if (IsEmpty(row.CommitMessage)) missing.Add("commitMessage");
        if (row.ServiceExists is null) missing.Add("serviceExists");
        if (environmentCount == 0) missing.Add("environments");
        if (transactionTypeCount == 0) missing.Add("transactionTypes");
        if (messageTypeCount == 0) missing.Add("messageTypes");
        if (sourceDestinationCount == 0) missing.Add("sourceDestinations");
        if (uomMappingCount == 0) missing.Add("uomMappings");
        return missing;
    }
}
