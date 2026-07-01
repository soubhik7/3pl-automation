using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SolaceConfigGenerator.Models;

// ── Root ──────────────────────────────────────────────────────────────────────

public sealed class SolaceConfig
{
    [JsonPropertyName("clientProfile")]
    public ClientProfileSection ClientProfile { get; init; } = new();

    [JsonPropertyName("ACL")]
    public AclSection ACL { get; init; } = new();

    [JsonPropertyName("ClientUser")]
    public ClientUserSection ClientUser { get; init; } = new();

    [JsonPropertyName("Queue")]
    public QueueSection Queue { get; init; } = new();

    [JsonPropertyName("Subscription")]
    public SubscriptionSection Subscription { get; init; } = new();
}

// ── clientProfile ─────────────────────────────────────────────────────────────

public sealed class ClientProfileSection
{
    [JsonPropertyName("create")]
    public List<ClientProfileEntry> Create { get; init; } = [];
}

public sealed class ClientProfileEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("allowGuaranteedMsgSendEnabled")]
    public bool AllowGuaranteedMsgSendEnabled { get; init; } = true;

    [JsonPropertyName("allowGuaranteedMsgReceiveEnabled")]
    public bool AllowGuaranteedMsgReceiveEnabled { get; init; } = true;

    [JsonPropertyName("compressionEnabled")]
    public bool CompressionEnabled { get; init; } = true;

    [JsonPropertyName("replicationAllowClientConnectWhenStandbyEnabled")]
    public bool ReplicationAllowClientConnectWhenStandbyEnabled { get; init; } = false;

    [JsonPropertyName("allowTransactedSessionsEnabled")]
    public bool AllowTransactedSessionsEnabled { get; init; } = true;

    [JsonPropertyName("allowBridgeConnectionsEnabled")]
    public bool AllowBridgeConnectionsEnabled { get; init; } = true;

    [JsonPropertyName("allowGuaranteedEndpointCreateEnabled")]
    public bool AllowGuaranteedEndpointCreateEnabled { get; init; } = true;

    [JsonPropertyName("allowSharedSubscriptionsEnabled")]
    public bool AllowSharedSubscriptionsEnabled { get; init; } = false;
}

// ── ACL ───────────────────────────────────────────────────────────────────────

public sealed class AclSection
{
    // JSON key is "create_ACL" — matches the Solace provisioning schema exactly
    [JsonPropertyName("create_ACL")]
    public List<AclEntry> CreateAcl { get; init; } = [];
}

public sealed class AclEntry
{
    [JsonPropertyName("aclProfileName")]
    public required string AclProfileName { get; init; }

    [JsonPropertyName("clientConnectDefaultAction")]
    public string ClientConnectDefaultAction { get; init; } = "allow";

    [JsonPropertyName("publishTopicDefaultAction")]
    public string PublishTopicDefaultAction { get; init; } = "disallow";

    [JsonPropertyName("subscribeShareNameDefaultAction")]
    public string SubscribeShareNameDefaultAction { get; init; } = "allow";

    [JsonPropertyName("subscribeTopicDefaultAction")]
    public string SubscribeTopicDefaultAction { get; init; } = "disallow";
}

// ── ClientUser ────────────────────────────────────────────────────────────────

public sealed class ClientUserSection
{
    [JsonPropertyName("create")]
    public List<ClientUserEntry> Create { get; init; } = [];
}

public sealed class ClientUserEntry
{
    [JsonPropertyName("aclProfileName")]
    public required string AclProfileName { get; init; }

    [JsonPropertyName("clientProfileName")]
    public required string ClientProfileName { get; init; }

    [JsonPropertyName("clientUsername")]
    public required string ClientUsername { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("guaranteedEndpointPermissionOverrideEnabled")]
    public bool GuaranteedEndpointPermissionOverrideEnabled { get; init; } = true;

    // Encrypted password passed through as-is from the CSV (encryption pipeline runs beforehand)
    [JsonPropertyName("password")]
    public required string Password { get; init; }

    [JsonPropertyName("subscriptionManagerEnabled")]
    public bool SubscriptionManagerEnabled { get; init; } = true;
}

// ── Queue ─────────────────────────────────────────────────────────────────────

public sealed class QueueSection
{
    [JsonPropertyName("create")]
    public List<QueueEntry> Create { get; init; } = [];
}

public sealed class QueueEntry
{
    [JsonPropertyName("queueName")]
    public required string QueueName { get; init; }

    // Absent on DMQ entries; present on regular queues pointing to their DMQ
    [JsonPropertyName("deadMsgQueue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeadMsgQueue { get; init; }

    [JsonPropertyName("owner")]
    public required string Owner { get; init; }

    [JsonPropertyName("permission")]
    public required string Permission { get; init; }

    [JsonPropertyName("egressEnabled")]
    public bool EgressEnabled { get; init; } = true;

    // Absent on DMQ entries (null); set to 4 on regular queues
    [JsonPropertyName("maxRedeliveryCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxRedeliveryCount { get; init; }
}

// ── Subscription ──────────────────────────────────────────────────────────────

public sealed class SubscriptionSection
{
    [JsonPropertyName("create")]
    public List<SubscriptionEntry> Create { get; init; } = [];
}

public sealed class SubscriptionEntry
{
    [JsonPropertyName("queueName")]
    public required string QueueName { get; init; }

    [JsonPropertyName("SUBSCRIPTION_LIST")]
    public required List<string> SubscriptionList { get; init; }
}
