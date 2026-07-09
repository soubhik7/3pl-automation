namespace ThreePl.Core.Admin;

/// <summary>Requirement level for one intake field (matches the old UI's always/outbound/optional).</summary>
public enum RequirementLevel
{
    Always,
    Outbound,
    Optional,
}

public sealed class FieldDefinition
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public RequirementLevel DefaultLevel { get; init; } = RequirementLevel.Optional;
    /// <summary>Natural keys — the enrichment workflow rejects saves without them; not configurable.</summary>
    public bool Locked { get; init; }
    /// <summary>Value, when present, must contain '@'.</summary>
    public bool Email { get; init; }
    /// <summary>Child-row table pseudo-field ("min 1 row" requirement instead of a value).</summary>
    public bool IsTable { get; init; }
}

public sealed class DomainDefinition
{
    public required string Domain { get; init; }
    public required string Title { get; init; }
    /// <summary>False for Common — "Outbound only" then behaves like Required.</summary>
    public bool HasDirection { get; init; }
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }

    public FieldDefinition? Find(string name) => Fields.FirstOrDefault(f => f.Name == name);
}

/// <summary>
/// One place defining which intake inputs exist per domain, their default
/// requirement levels (mirroring the data-enrichment workflow's
/// Compose_*_Enrichment_Status rules) and which are locked natural keys.
/// Shared by the Admin screen, the intake required-markers and save-time
/// validation, so UI and backend agree.
/// </summary>
public static class FieldDefinitions
{
    private static FieldDefinition F(string name, string label, RequirementLevel level = RequirementLevel.Optional,
        bool locked = false, bool email = false, bool table = false) =>
        new() { Name = name, Label = label, DefaultLevel = level, Locked = locked, Email = email, IsTable = table };

    public static readonly DomainDefinition Common = new()
    {
        Domain = "Common",
        Title = "Common",
        HasDirection = false,
        Fields = new[]
        {
            F("interfaceId", "Interface ID", RequirementLevel.Always, locked: true),
            F("eaRef", "EA Reference"),
            F("sourceApp", "Source Application"),
            F("targetApp", "Target Application"),
            F("businessObject", "Business Object"),
            F("country", "Country / Market"),
            F("sourceFormat", "Source Format"),
            F("sourceInterfaceType", "Source Interface Type"),
            F("targetFormat", "Target Format"),
            F("targetInterfaceType", "Target Interface Type"),
            F("functionalDescription", "Functional Description"),
            F("volume", "Volume"),
            F("sizePerMessage", "Size Per Message"),
            F("peakVolume", "Peak Volume"),
            F("threePlPartnerId", "3PL Partner ID"),
            F("navInstanceId", "1Nav Instance ID"),
            F("countryCodeIso", "Country Code ISO"),
            F("regionIso", "Region ISO"),
            F("subscriptionRules", "Subscription Rules"),
        },
    };

    public static readonly DomainDefinition Btp = new()
    {
        Domain = "Btp",
        Title = "SAP BTP",
        HasDirection = true,
        Fields = new[]
        {
            F("subAccount", "SubAccount", RequirementLevel.Always, locked: true),
            F("productName", "Product Name", RequirementLevel.Always, locked: true),
            F("environment", "Environment", RequirementLevel.Always, locked: true),
            F("mode", "Mode", RequirementLevel.Outbound),
            F("developerId", "Developer ID", RequirementLevel.Outbound),
            F("title", "Title", RequirementLevel.Outbound),
            F("shortText", "Short Text"),
            F("repoOwner", "Repo Owner", RequirementLevel.Outbound),
            F("repoName", "Repo Name", RequirementLevel.Outbound),
            F("workflowFileName", "Workflow File Name", RequirementLevel.Outbound),
            F("branchRef", "Branch Ref", RequirementLevel.Outbound),
            F("serviceExists", "Service Exists", RequirementLevel.Outbound),
            F("recipientEmail", "Recipient Email", email: true),
        },
    };

    public static readonly DomainDefinition Solace = new()
    {
        Domain = "Solace",
        Title = "Solace Client",
        HasDirection = true,
        Fields = new[]
        {
            F("brand", "Brand", RequirementLevel.Always, locked: true),
            F("env", "Env", RequirementLevel.Always, locked: true),
            F("systemName", "System Name", RequirementLevel.Always, locked: true),
            F("threePLCode", "ThreePL Code", RequirementLevel.Always, locked: true),
            F("encryptedPassword", "Encrypted Password"),
            F("action", "Action", RequirementLevel.Outbound),
            F("serviceExists", "Service Exists", RequirementLevel.Outbound),
            F("repoOwner", "Repo Owner", RequirementLevel.Outbound),
            F("repoName", "Repo Name", RequirementLevel.Outbound),
            F("filePath", "File Path", RequirementLevel.Outbound),
            F("branch", "Branch", RequirementLevel.Outbound),
            F("baseBranch", "Base Branch", RequirementLevel.Outbound),
            F("featureBranchName", "Feature Branch Name", RequirementLevel.Outbound),
            F("requesterEmail", "Requester Email", RequirementLevel.Outbound, email: true),
            F("recipientEmail", "Recipient Email", RequirementLevel.Outbound, email: true),
            F("commitMessage", "Commit Message", RequirementLevel.Outbound),
            F("messageTypes", "Message Types (min 1 row)", RequirementLevel.Outbound, table: true),
        },
    };

    public static readonly DomainDefinition MuleSoft = new()
    {
        Domain = "MuleSoft",
        Title = "MuleSoft Partner",
        HasDirection = true,
        Fields = new[]
        {
            F("countryKey", "Country Key", RequirementLevel.Always, locked: true),
            F("countryCode", "Country Code", RequirementLevel.Outbound),
            F("partnerComment", "Partner Comment", RequirementLevel.Outbound),
            F("createdBy", "Created By", RequirementLevel.Outbound),
            F("navProtocol", "Nav Protocol", RequirementLevel.Outbound),
            F("navPort", "Nav Port", RequirementLevel.Outbound),
            F("navUsername", "Nav Username", RequirementLevel.Outbound),
            F("navDomain", "Nav Domain", RequirementLevel.Outbound),
            F("navService", "Nav Service", RequirementLevel.Outbound),
            F("navSoapPort", "Nav Soap Port", RequirementLevel.Outbound),
            F("translationReceiverName", "Translation Receiver Name", RequirementLevel.Outbound),
            F("serviceExists", "Service Exists", RequirementLevel.Outbound),
            F("repoOwner", "Repo Owner", RequirementLevel.Outbound),
            F("repoName", "Repo Name", RequirementLevel.Outbound),
            F("filePathPrefix", "File Path Prefix", RequirementLevel.Outbound),
            F("branch", "Branch", RequirementLevel.Outbound),
            F("baseBranch", "Base Branch", RequirementLevel.Outbound),
            F("featureBranchName", "Feature Branch Name", RequirementLevel.Outbound),
            F("requesterEmail", "Requester Email", RequirementLevel.Outbound, email: true),
            F("recipientEmail", "Recipient Email", RequirementLevel.Outbound, email: true),
            F("commitMessage", "Commit Message", RequirementLevel.Outbound),
            F("environments", "Environments (min 1 row)", RequirementLevel.Outbound, table: true),
            F("transactionTypes", "Transaction Types (min 1 row)", RequirementLevel.Outbound, table: true),
            F("messageTypes", "Message Types (min 1 row)", RequirementLevel.Outbound, table: true),
            F("sourceDestinations", "Source/Destination Mappings (min 1 row)", RequirementLevel.Outbound, table: true),
            F("uomMappings", "UOM Mappings (min 1 row)", RequirementLevel.Outbound, table: true),
        },
    };

    public static readonly IReadOnlyList<DomainDefinition> All = new[] { Common, Btp, Solace, MuleSoft };

    public static DomainDefinition Get(string domain) =>
        All.FirstOrDefault(d => d.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown domain '{domain}'.", nameof(domain));
}
