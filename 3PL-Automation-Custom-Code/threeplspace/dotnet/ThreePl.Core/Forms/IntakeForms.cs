using System.Text.Json.Nodes;

namespace ThreePl.Core.Forms;

/// <summary>
/// Intake form models. Each mirrors one of the current HTML intake forms and
/// serializes to the exact JSON field set the HTML's saveDomain() posts to
/// data-enrichment: text/select values with '' sent as null, the
/// "Select…"/True/False selects as bool-or-null, checkboxes always as bool,
/// and the child-row arrays under their existing names.
/// </summary>
public static class PayloadJson
{
    /// <summary>'' → null, mirroring getFormData in the current HTML.</summary>
    public static JsonNode? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value);

    /// <summary>"true"/"false"/"" select → true/false/null, mirroring getFormData.</summary>
    public static JsonNode? TriState(string? value) => value switch
    {
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        _ => null,
    };
}

public class CommonForm
{
    public string? InterfaceId { get; set; }
    public string? EaRef { get; set; }
    public string? SourceApp { get; set; }
    public string? TargetApp { get; set; }
    public string? BusinessObject { get; set; }
    public string? Country { get; set; }
    public string? SourceFormat { get; set; }
    public string? SourceInterfaceType { get; set; }
    public string? TargetFormat { get; set; }
    public string? TargetInterfaceType { get; set; }
    public string? FunctionalDescription { get; set; }
    public string? Volume { get; set; }
    public string? SizePerMessage { get; set; }
    public string? PeakVolume { get; set; }
    public string? ThreePlPartnerId { get; set; }
    public string? NavInstanceId { get; set; }
    public string? CountryCodeIso { get; set; }
    public string? RegionIso { get; set; }
    public string? SubscriptionRules { get; set; }

    public JsonObject ToPayloadFields() => new()
    {
        ["interfaceId"] = PayloadJson.Text(InterfaceId),
        ["eaRef"] = PayloadJson.Text(EaRef),
        ["sourceApp"] = PayloadJson.Text(SourceApp),
        ["targetApp"] = PayloadJson.Text(TargetApp),
        ["businessObject"] = PayloadJson.Text(BusinessObject),
        ["country"] = PayloadJson.Text(Country),
        ["sourceFormat"] = PayloadJson.Text(SourceFormat),
        ["sourceInterfaceType"] = PayloadJson.Text(SourceInterfaceType),
        ["targetFormat"] = PayloadJson.Text(TargetFormat),
        ["targetInterfaceType"] = PayloadJson.Text(TargetInterfaceType),
        ["functionalDescription"] = PayloadJson.Text(FunctionalDescription),
        ["volume"] = PayloadJson.Text(Volume),
        ["sizePerMessage"] = PayloadJson.Text(SizePerMessage),
        ["peakVolume"] = PayloadJson.Text(PeakVolume),
        ["threePlPartnerId"] = PayloadJson.Text(ThreePlPartnerId),
        ["navInstanceId"] = PayloadJson.Text(NavInstanceId),
        ["countryCodeIso"] = PayloadJson.Text(CountryCodeIso),
        ["regionIso"] = PayloadJson.Text(RegionIso),
        ["subscriptionRules"] = PayloadJson.Text(SubscriptionRules),
    };

    public IReadOnlyDictionary<string, string?> FieldValues => new Dictionary<string, string?>
    {
        ["interfaceId"] = InterfaceId,
        ["eaRef"] = EaRef,
        ["sourceApp"] = SourceApp,
        ["targetApp"] = TargetApp,
        ["businessObject"] = BusinessObject,
        ["country"] = Country,
        ["sourceFormat"] = SourceFormat,
        ["sourceInterfaceType"] = SourceInterfaceType,
        ["targetFormat"] = TargetFormat,
        ["targetInterfaceType"] = TargetInterfaceType,
        ["functionalDescription"] = FunctionalDescription,
        ["volume"] = Volume,
        ["sizePerMessage"] = SizePerMessage,
        ["peakVolume"] = PeakVolume,
        ["threePlPartnerId"] = ThreePlPartnerId,
        ["navInstanceId"] = NavInstanceId,
        ["countryCodeIso"] = CountryCodeIso,
        ["regionIso"] = RegionIso,
        ["subscriptionRules"] = SubscriptionRules,
    };
}

public class BtpForm
{
    public string Direction { get; set; } = "Outbound";
    public string? SubAccount { get; set; }
    public string? ProductName { get; set; }
    public string? Environment { get; set; }
    public string? Mode { get; set; }
    public string? DeveloperId { get; set; }
    public string? Title { get; set; }
    public string? ShortText { get; set; }
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? WorkflowFileName { get; set; }
    public string? BranchRef { get; set; }
    /// <summary>Select…/True/False select — "", "true" or "false".</summary>
    public string ServiceExists { get; set; } = "";
    public string? RecipientEmail { get; set; }

    public JsonObject ToPayloadFields() => new()
    {
        ["direction"] = PayloadJson.Text(Direction),
        ["subAccount"] = PayloadJson.Text(SubAccount),
        ["productName"] = PayloadJson.Text(ProductName),
        ["environment"] = PayloadJson.Text(Environment),
        ["mode"] = PayloadJson.Text(Mode),
        ["developerId"] = PayloadJson.Text(DeveloperId),
        ["title"] = PayloadJson.Text(Title),
        ["shortText"] = PayloadJson.Text(ShortText),
        ["repoOwner"] = PayloadJson.Text(RepoOwner),
        ["repoName"] = PayloadJson.Text(RepoName),
        ["workflowFileName"] = PayloadJson.Text(WorkflowFileName),
        ["branchRef"] = PayloadJson.Text(BranchRef),
        ["serviceExists"] = PayloadJson.TriState(ServiceExists),
        ["recipientEmail"] = PayloadJson.Text(RecipientEmail),
    };

    public IReadOnlyDictionary<string, string?> FieldValues => new Dictionary<string, string?>
    {
        ["subAccount"] = SubAccount,
        ["productName"] = ProductName,
        ["environment"] = Environment,
        ["mode"] = Mode,
        ["developerId"] = DeveloperId,
        ["title"] = Title,
        ["shortText"] = ShortText,
        ["repoOwner"] = RepoOwner,
        ["repoName"] = RepoName,
        ["workflowFileName"] = WorkflowFileName,
        ["branchRef"] = BranchRef,
        ["serviceExists"] = ServiceExists,
        ["recipientEmail"] = RecipientEmail,
    };
}

public class SolaceMessageTypeRow
{
    public string? MessageType { get; set; }
    public string? Topic { get; set; }
    public string? QueuePermission { get; set; } = "consume";
    public bool QueueEgressEnabled { get; set; } = true;
    public int? QueueMaxRedeliveryCount { get; set; } = 3;

    public bool IsBlank => string.IsNullOrWhiteSpace(MessageType) && string.IsNullOrWhiteSpace(Topic);

    public JsonObject ToJson() => new()
    {
        ["messageType"] = MessageType ?? "",
        ["topic"] = Topic ?? "",
        ["queuePermission"] = QueuePermission ?? "",
        ["queueEgressEnabled"] = QueueEgressEnabled,
        ["queueMaxRedeliveryCount"] = QueueMaxRedeliveryCount is null ? null : JsonValue.Create(QueueMaxRedeliveryCount.Value),
    };
}

public class SolaceForm
{
    public string Direction { get; set; } = "Outbound";
    public string? Brand { get; set; }
    public string? Env { get; set; }
    public string? SystemName { get; set; }
    public string? ThreePLCode { get; set; }
    /// <summary>Password input — never prefilled, blank leaves the stored value unchanged.</summary>
    public string? EncryptedPassword { get; set; }
    public string? Action { get; set; }
    public string AclClientConnectDefaultAction { get; set; } = "allow";
    public string AclPublishTopicDefaultAction { get; set; } = "allow";
    public string AclSubscribeShareNameDefaultAction { get; set; } = "allow";
    public string AclSubscribeTopicDefaultAction { get; set; } = "allow";
    public bool ClientProfileAllowGuaranteedMsgSendEnabled { get; set; }
    public bool ClientProfileAllowGuaranteedMsgReceiveEnabled { get; set; }
    public bool ClientProfileCompressionEnabled { get; set; }
    public bool ClientProfileReplicationAllowClientConnectWhenStandbyEnabled { get; set; }
    public bool ClientProfileAllowTransactedSessionsEnabled { get; set; }
    public bool ClientProfileAllowBridgeConnectionsEnabled { get; set; }
    public bool ClientProfileAllowGuaranteedEndpointCreateEnabled { get; set; }
    public bool ClientProfileAllowSharedSubscriptionsEnabled { get; set; }
    public bool ClientUserEnabled { get; set; }
    public bool ClientUserGuaranteedEndpointPermissionOverrideEnabled { get; set; }
    public bool ClientUserSubscriptionManagerEnabled { get; set; }
    public string ServiceExists { get; set; } = "";
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? FilePath { get; set; }
    public string? Branch { get; set; }
    public string? BaseBranch { get; set; }
    public string? FeatureBranchName { get; set; }
    public string? RequesterEmail { get; set; }
    public string? RecipientEmail { get; set; }
    public string? CommitMessage { get; set; }
    public List<SolaceMessageTypeRow> MessageTypes { get; set; } = new();

    /// <summary>Drops rows the user added but left entirely blank (cleanDomainTables parity).</summary>
    public void PruneBlankRows() => MessageTypes.RemoveAll(r => r.IsBlank);

    public JsonObject ToPayloadFields()
    {
        var fields = new JsonObject
        {
            ["direction"] = PayloadJson.Text(Direction),
            ["brand"] = PayloadJson.Text(Brand),
            ["env"] = PayloadJson.Text(Env),
            ["systemName"] = PayloadJson.Text(SystemName),
            ["threePLCode"] = PayloadJson.Text(ThreePLCode),
            ["encryptedPassword"] = PayloadJson.Text(EncryptedPassword),
            ["action"] = PayloadJson.Text(Action),
            ["aclClientConnectDefaultAction"] = PayloadJson.Text(AclClientConnectDefaultAction),
            ["aclPublishTopicDefaultAction"] = PayloadJson.Text(AclPublishTopicDefaultAction),
            ["aclSubscribeShareNameDefaultAction"] = PayloadJson.Text(AclSubscribeShareNameDefaultAction),
            ["aclSubscribeTopicDefaultAction"] = PayloadJson.Text(AclSubscribeTopicDefaultAction),
            ["clientProfileAllowGuaranteedMsgSendEnabled"] = ClientProfileAllowGuaranteedMsgSendEnabled,
            ["clientProfileAllowGuaranteedMsgReceiveEnabled"] = ClientProfileAllowGuaranteedMsgReceiveEnabled,
            ["clientProfileCompressionEnabled"] = ClientProfileCompressionEnabled,
            ["clientProfileReplicationAllowClientConnectWhenStandbyEnabled"] = ClientProfileReplicationAllowClientConnectWhenStandbyEnabled,
            ["clientProfileAllowTransactedSessionsEnabled"] = ClientProfileAllowTransactedSessionsEnabled,
            ["clientProfileAllowBridgeConnectionsEnabled"] = ClientProfileAllowBridgeConnectionsEnabled,
            ["clientProfileAllowGuaranteedEndpointCreateEnabled"] = ClientProfileAllowGuaranteedEndpointCreateEnabled,
            ["clientProfileAllowSharedSubscriptionsEnabled"] = ClientProfileAllowSharedSubscriptionsEnabled,
            ["clientUserEnabled"] = ClientUserEnabled,
            ["clientUserGuaranteedEndpointPermissionOverrideEnabled"] = ClientUserGuaranteedEndpointPermissionOverrideEnabled,
            ["clientUserSubscriptionManagerEnabled"] = ClientUserSubscriptionManagerEnabled,
            ["serviceExists"] = PayloadJson.TriState(ServiceExists),
            ["repoOwner"] = PayloadJson.Text(RepoOwner),
            ["repoName"] = PayloadJson.Text(RepoName),
            ["filePath"] = PayloadJson.Text(FilePath),
            ["branch"] = PayloadJson.Text(Branch),
            ["baseBranch"] = PayloadJson.Text(BaseBranch),
            ["featureBranchName"] = PayloadJson.Text(FeatureBranchName),
            ["requesterEmail"] = PayloadJson.Text(RequesterEmail),
            ["recipientEmail"] = PayloadJson.Text(RecipientEmail),
            ["commitMessage"] = PayloadJson.Text(CommitMessage),
        };
        var mts = new JsonArray();
        foreach (var row in MessageTypes) mts.Add(row.ToJson());
        fields["messageTypes"] = mts;
        return fields;
    }

    public IReadOnlyDictionary<string, string?> FieldValues => new Dictionary<string, string?>
    {
        ["brand"] = Brand,
        ["env"] = Env,
        ["systemName"] = SystemName,
        ["threePLCode"] = ThreePLCode,
        ["encryptedPassword"] = EncryptedPassword,
        ["action"] = Action,
        ["serviceExists"] = ServiceExists,
        ["repoOwner"] = RepoOwner,
        ["repoName"] = RepoName,
        ["filePath"] = FilePath,
        ["branch"] = Branch,
        ["baseBranch"] = BaseBranch,
        ["featureBranchName"] = FeatureBranchName,
        ["requesterEmail"] = RequesterEmail,
        ["recipientEmail"] = RecipientEmail,
        ["commitMessage"] = CommitMessage,
    };

    public IReadOnlyDictionary<string, int> TableCounts => new Dictionary<string, int>
    {
        ["messageTypes"] = MessageTypes.Count,
    };
}

public class MuleEnvironmentRow
{
    public string? Environment { get; set; }
    public string? NavHost { get; set; }
    public string? NavCompany { get; set; }
    public string? NavSoapPath { get; set; }
    public string? NavRoutingCode { get; set; }

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Environment) && string.IsNullOrWhiteSpace(NavHost)
        && string.IsNullOrWhiteSpace(NavCompany) && string.IsNullOrWhiteSpace(NavSoapPath)
        && string.IsNullOrWhiteSpace(NavRoutingCode);

    public JsonObject ToJson() => new()
    {
        ["environment"] = Environment ?? "",
        ["navHost"] = NavHost ?? "",
        ["navCompany"] = NavCompany ?? "",
        ["navSoapPath"] = NavSoapPath ?? "",
        ["navRoutingCode"] = NavRoutingCode ?? "",
    };
}

public class MuleTransactionTypeRow
{
    public string? TransactionTypeCode { get; set; }
    public bool TransactionTypeEnabled { get; set; } = true;
    public string? TransactionTypeLabel { get; set; }

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(TransactionTypeCode) && string.IsNullOrWhiteSpace(TransactionTypeLabel);

    public JsonObject ToJson() => new()
    {
        ["transactionTypeCode"] = TransactionTypeCode ?? "",
        ["transactionTypeEnabled"] = TransactionTypeEnabled,
        ["transactionTypeLabel"] = TransactionTypeLabel ?? "",
    };
}

public class MuleMessageTypeRow
{
    public string? MessageType { get; set; }
    public bool IsBlank => string.IsNullOrWhiteSpace(MessageType);
    public JsonObject ToJson() => new() { ["messageType"] = MessageType ?? "" };
}

public class MuleSourceDestinationRow
{
    public string? SourceDestinationFrom { get; set; }
    public string? SourceDestinationTo { get; set; }

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(SourceDestinationFrom) && string.IsNullOrWhiteSpace(SourceDestinationTo);

    public JsonObject ToJson() => new()
    {
        ["sourceDestinationFrom"] = SourceDestinationFrom ?? "",
        ["sourceDestinationTo"] = SourceDestinationTo ?? "",
    };
}

public class MuleUomMappingRow
{
    public string? UomFrom { get; set; }
    public string? UomTo { get; set; }
    public bool IsBlank => string.IsNullOrWhiteSpace(UomFrom) && string.IsNullOrWhiteSpace(UomTo);
    public JsonObject ToJson() => new() { ["uomFrom"] = UomFrom ?? "", ["uomTo"] = UomTo ?? "" };
}

public class MuleSoftForm
{
    public string Direction { get; set; } = "Outbound";
    public string? CountryKey { get; set; }
    public string? CountryCode { get; set; }
    public string? PartnerComment { get; set; }
    public string? CreatedBy { get; set; }
    public string? NavProtocol { get; set; }
    public string? NavPort { get; set; }
    public string? NavUsername { get; set; }
    public string? NavDomain { get; set; }
    public string? NavService { get; set; }
    public string? NavSoapPort { get; set; }
    public bool NavUseCommonCert { get; set; }
    public string? TranslationReceiverName { get; set; }
    public string ServiceExists { get; set; } = "";
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? FilePathPrefix { get; set; }
    public string? Branch { get; set; }
    public string? BaseBranch { get; set; }
    public string? FeatureBranchName { get; set; }
    public string? RequesterEmail { get; set; }
    public string? RecipientEmail { get; set; }
    public string? CommitMessage { get; set; }
    public List<MuleEnvironmentRow> Environments { get; set; } = new();
    public List<MuleTransactionTypeRow> TransactionTypes { get; set; } = new();
    public List<MuleMessageTypeRow> MessageTypes { get; set; } = new();
    public List<MuleSourceDestinationRow> SourceDestinations { get; set; } = new();
    public List<MuleUomMappingRow> UomMappings { get; set; } = new();

    public void PruneBlankRows()
    {
        Environments.RemoveAll(r => r.IsBlank);
        TransactionTypes.RemoveAll(r => r.IsBlank);
        MessageTypes.RemoveAll(r => r.IsBlank);
        SourceDestinations.RemoveAll(r => r.IsBlank);
        UomMappings.RemoveAll(r => r.IsBlank);
    }

    public JsonObject ToPayloadFields()
    {
        var fields = new JsonObject
        {
            ["direction"] = PayloadJson.Text(Direction),
            ["countryKey"] = PayloadJson.Text(CountryKey),
            ["countryCode"] = PayloadJson.Text(CountryCode),
            ["partnerComment"] = PayloadJson.Text(PartnerComment),
            ["createdBy"] = PayloadJson.Text(CreatedBy),
            ["navProtocol"] = PayloadJson.Text(NavProtocol),
            ["navPort"] = PayloadJson.Text(NavPort),
            ["navUsername"] = PayloadJson.Text(NavUsername),
            ["navDomain"] = PayloadJson.Text(NavDomain),
            ["navService"] = PayloadJson.Text(NavService),
            ["navSoapPort"] = PayloadJson.Text(NavSoapPort),
            ["navUseCommonCert"] = NavUseCommonCert,
            ["translationReceiverName"] = PayloadJson.Text(TranslationReceiverName),
            ["serviceExists"] = PayloadJson.TriState(ServiceExists),
            ["repoOwner"] = PayloadJson.Text(RepoOwner),
            ["repoName"] = PayloadJson.Text(RepoName),
            ["filePathPrefix"] = PayloadJson.Text(FilePathPrefix),
            ["branch"] = PayloadJson.Text(Branch),
            ["baseBranch"] = PayloadJson.Text(BaseBranch),
            ["featureBranchName"] = PayloadJson.Text(FeatureBranchName),
            ["requesterEmail"] = PayloadJson.Text(RequesterEmail),
            ["recipientEmail"] = PayloadJson.Text(RecipientEmail),
            ["commitMessage"] = PayloadJson.Text(CommitMessage),
        };
        var envs = new JsonArray(); foreach (var r in Environments) envs.Add(r.ToJson());
        var tts = new JsonArray(); foreach (var r in TransactionTypes) tts.Add(r.ToJson());
        var mts = new JsonArray(); foreach (var r in MessageTypes) mts.Add(r.ToJson());
        var sds = new JsonArray(); foreach (var r in SourceDestinations) sds.Add(r.ToJson());
        var uoms = new JsonArray(); foreach (var r in UomMappings) uoms.Add(r.ToJson());
        fields["environments"] = envs;
        fields["transactionTypes"] = tts;
        fields["messageTypes"] = mts;
        fields["sourceDestinations"] = sds;
        fields["uomMappings"] = uoms;
        return fields;
    }

    public IReadOnlyDictionary<string, string?> FieldValues => new Dictionary<string, string?>
    {
        ["countryKey"] = CountryKey,
        ["countryCode"] = CountryCode,
        ["partnerComment"] = PartnerComment,
        ["createdBy"] = CreatedBy,
        ["navProtocol"] = NavProtocol,
        ["navPort"] = NavPort,
        ["navUsername"] = NavUsername,
        ["navDomain"] = NavDomain,
        ["navService"] = NavService,
        ["navSoapPort"] = NavSoapPort,
        ["translationReceiverName"] = TranslationReceiverName,
        ["serviceExists"] = ServiceExists,
        ["repoOwner"] = RepoOwner,
        ["repoName"] = RepoName,
        ["filePathPrefix"] = FilePathPrefix,
        ["branch"] = Branch,
        ["baseBranch"] = BaseBranch,
        ["featureBranchName"] = FeatureBranchName,
        ["requesterEmail"] = RequesterEmail,
        ["recipientEmail"] = RecipientEmail,
        ["commitMessage"] = CommitMessage,
    };

    public IReadOnlyDictionary<string, int> TableCounts => new Dictionary<string, int>
    {
        ["environments"] = Environments.Count,
        ["transactionTypes"] = TransactionTypes.Count,
        ["messageTypes"] = MessageTypes.Count,
        ["sourceDestinations"] = SourceDestinations.Count,
        ["uomMappings"] = UomMappings.Count,
    };
}
