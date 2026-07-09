using ThreePl.Core.Entities;
using ThreePl.Core.Reads;

namespace ThreePl.Tests;

/// <summary>
/// Parity with data-enrichment's Compose_{Domain}_Enrichment_Status
/// expressions: same required fields, same Inbound short-circuit.
/// </summary>
public class MissingFieldRulesTests
{
    private static BtpConfig CompleteBtp() => new()
    {
        SubAccount = "sa", ProductName = "pn", Environment = "uat",
        Direction = "Outbound",
        Mode = "create", DeveloperId = "dev", Title = "t",
        RepoOwner = "o", RepoName = "r", WorkflowFileName = "w.yml", BranchRef = "main",
        ServiceExists = false,
    };

    [Fact]
    public void Btp_AllFieldsPresent_NoMissing()
    {
        Assert.Empty(MissingFieldRules.ForBtp(CompleteBtp()));
    }

    [Fact]
    public void Btp_EmptyOutboundRow_ListsWorkflowRequiredFields_InWorkflowOrder()
    {
        var row = new BtpConfig { SubAccount = "sa", ProductName = "pn", Environment = "uat", Direction = "Outbound" };
        Assert.Equal(
            new[] { "mode", "developerId", "title", "repoOwner", "repoName", "workflowFileName", "branchRef", "serviceExists" },
            MissingFieldRules.ForBtp(row));
    }

    [Fact]
    public void Btp_Inbound_ShortCircuits_NoMissingFields()
    {
        var row = new BtpConfig { SubAccount = "sa", ProductName = "pn", Environment = "uat", Direction = "Inbound" };
        Assert.Empty(MissingFieldRules.ForBtp(row));
    }

    [Fact]
    public void Btp_ServiceExistsFalse_IsNotMissing_OnlyNullIs()
    {
        // The workflow checks equals(serviceExists, null) — false must count as provided.
        var row = CompleteBtp();
        row.ServiceExists = false;
        Assert.Empty(MissingFieldRules.ForBtp(row));
        row.ServiceExists = null;
        Assert.Equal(new[] { "serviceExists" }, MissingFieldRules.ForBtp(row));
    }

    private static SolaceClient CompleteSolace() => new()
    {
        Brand = "petc", Env = "rc", SystemName = "navision", ThreePLCode = "3plpnp",
        Direction = "Outbound", Action = "FullOnboarding",
        AclClientConnectDefaultAction = "allow", AclPublishTopicDefaultAction = "allow",
        AclSubscribeShareNameDefaultAction = "allow", AclSubscribeTopicDefaultAction = "allow",
        ClientProfileAllowGuaranteedMsgSendEnabled = true,
        ClientProfileAllowGuaranteedMsgReceiveEnabled = true,
        ClientProfileCompressionEnabled = false,
        ClientProfileReplicationAllowClientConnectWhenStandbyEnabled = false,
        ClientProfileAllowTransactedSessionsEnabled = false,
        ClientProfileAllowBridgeConnectionsEnabled = false,
        ClientProfileAllowGuaranteedEndpointCreateEnabled = true,
        ClientProfileAllowSharedSubscriptionsEnabled = false,
        ClientUserEnabled = true,
        ClientUserGuaranteedEndpointPermissionOverrideEnabled = false,
        ClientUserSubscriptionManagerEnabled = false,
        RepoOwner = "o", RepoName = "r", FilePath = "f.json", Branch = "main",
        BaseBranch = "main", FeatureBranchName = "feature/x",
        RequesterEmail = "req@example.com", RecipientEmail = "rec@example.com",
        CommitMessage = "msg", ServiceExists = true,
    };

    [Fact]
    public void Solace_Complete_WithMessageTypes_NoMissing()
    {
        Assert.Empty(MissingFieldRules.ForSolace(CompleteSolace(), messageTypeCount: 1));
    }

    [Fact]
    public void Solace_ZeroMessageTypeRows_IsMissing_MessageTypes()
    {
        Assert.Equal(new[] { "messageTypes" }, MissingFieldRules.ForSolace(CompleteSolace(), messageTypeCount: 0));
    }

    [Fact]
    public void Solace_EmptyOutboundRow_ListsWorkflowRequiredFields_InWorkflowOrder()
    {
        var row = new SolaceClient { Brand = "b", Env = "e", SystemName = "s", ThreePLCode = "t", Direction = "Outbound" };
        Assert.Equal(
            new[]
            {
                "action",
                "aclClientConnectDefaultAction", "aclPublishTopicDefaultAction",
                "aclSubscribeShareNameDefaultAction", "aclSubscribeTopicDefaultAction",
                "clientProfileAllowGuaranteedMsgSendEnabled", "clientProfileAllowGuaranteedMsgReceiveEnabled",
                "clientProfileCompressionEnabled", "clientProfileReplicationAllowClientConnectWhenStandbyEnabled",
                "clientProfileAllowTransactedSessionsEnabled", "clientProfileAllowBridgeConnectionsEnabled",
                "clientProfileAllowGuaranteedEndpointCreateEnabled", "clientProfileAllowSharedSubscriptionsEnabled",
                "clientUserEnabled", "clientUserGuaranteedEndpointPermissionOverrideEnabled",
                "clientUserSubscriptionManagerEnabled",
                "repoOwner", "repoName", "filePath", "branch", "baseBranch", "featureBranchName",
                "requesterEmail", "recipientEmail", "commitMessage", "serviceExists", "messageTypes",
            },
            MissingFieldRules.ForSolace(row, messageTypeCount: 0));
    }

    [Fact]
    public void Solace_Inbound_ShortCircuits_EvenWithNoMessageTypes()
    {
        var row = new SolaceClient { Brand = "b", Env = "e", SystemName = "s", ThreePLCode = "t", Direction = "Inbound" };
        Assert.Empty(MissingFieldRules.ForSolace(row, messageTypeCount: 0));
    }

    private static MuleSoftPartner CompleteMule() => new()
    {
        CountryKey = "royal-canin-france", Direction = "Outbound",
        CountryCode = "fr", PartnerComment = "c", CreatedBy = "cb",
        NavProtocol = "https", NavPort = "7124", NavUsername = "u", NavDomain = "d",
        NavService = "svc", NavSoapPort = "7124", NavUseCommonCert = true,
        TranslationReceiverName = "trn",
        RepoOwner = "o", RepoName = "r", FilePathPrefix = "p", Branch = "main",
        BaseBranch = "main", FeatureBranchName = "feature/x",
        RequesterEmail = "req@example.com", RecipientEmail = "rec@example.com",
        CommitMessage = "msg", ServiceExists = false,
    };

    [Fact]
    public void MuleSoft_Complete_WithAllChildRows_NoMissing()
    {
        Assert.Empty(MissingFieldRules.ForMuleSoft(CompleteMule(), 1, 1, 1, 1, 1));
    }

    [Fact]
    public void MuleSoft_MissingChildTables_ListedIndividually()
    {
        Assert.Equal(
            new[] { "environments", "transactionTypes", "messageTypes", "sourceDestinations", "uomMappings" },
            MissingFieldRules.ForMuleSoft(CompleteMule(), 0, 0, 0, 0, 0));
    }

    [Fact]
    public void MuleSoft_EmptyOutboundRow_ListsWorkflowRequiredFields_InWorkflowOrder()
    {
        var row = new MuleSoftPartner { CountryKey = "k", Direction = "Outbound" };
        Assert.Equal(
            new[]
            {
                "countryCode", "partnerComment", "createdBy",
                "navProtocol", "navPort", "navUsername", "navDomain", "navService", "navSoapPort",
                "translationReceiverName", "navUseCommonCert",
                "repoOwner", "repoName", "filePathPrefix", "branch", "baseBranch", "featureBranchName",
                "requesterEmail", "recipientEmail", "commitMessage", "serviceExists",
                "environments", "transactionTypes", "messageTypes", "sourceDestinations", "uomMappings",
            },
            MissingFieldRules.ForMuleSoft(row, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void MuleSoft_Inbound_ShortCircuits_NoMissingFields()
    {
        var row = new MuleSoftPartner { CountryKey = "k", Direction = "Inbound" };
        Assert.Empty(MissingFieldRules.ForMuleSoft(row, 0, 0, 0, 0, 0));
    }
}
