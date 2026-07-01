using System;
using System.Collections.Generic;
using System.Globalization;
using SolaceConfigGenerator.Models;

namespace SolaceConfigGenerator.Services;

// ============================================================================
// SolaceConfigBuilder — turns a SolaceOnboardingRecord into the Solace JSON shape
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   This is the single place that knows the real Solace onboarding JSON
//   schema (clientProfile/ACL/ClientUser/Queue/Subscription, exact field
//   names like "create_ACL"/"SUBSCRIPTION_LIST") and which sections a given
//   Action needs — verified byte-for-byte against a real production Solace
//   config the CSV/JSON schema was built from. Separating this from
//   CsvParser means the CSV's shape can evolve without touching the JSON
//   schema knowledge, and vice versa.
// HOW TO USE:
//   var json = new SolaceConfigBuilder().Build(record); — record comes from
//   CsvParser.Parse(). Every business-rule default below (Default* constants)
//   is used ONLY when the corresponding CSV cell was blank — the CSV always
//   wins, so nothing here is a hidden override of an explicit CSV value.
// IMPORTANT NOTES:
//   Every Default* constant and the Action->sections mapping (ResolveSections)
//   is intentionally still C# code, not CSV data — these encode Solace
//   *business policy* (e.g. "publish defaults to disallow"), not values that
//   vary per onboarding request. The DMQ's "consume" permission likewise
//   isn't a policy default — it's the structural definition of a dead-message
//   queue, so it can never be an override.
// ============================================================================
// Which JSON sections a row's Action produces. A row only ever describes one change
// (e.g. "just add a subscription to an existing queue"), so most Actions need just
// a slice of the full onboarding shape, not every section every time.
[Flags]
internal enum SolaceSections
{
    None          = 0,
    ClientProfile = 1 << 0,
    Acl           = 1 << 1,
    ClientUser    = 1 << 2,
    Queue         = 1 << 3,
    Subscription  = 1 << 4,
    All           = ClientProfile | Acl | ClientUser | Queue | Subscription
}

public sealed class SolaceConfigBuilder
{
    // Every value below is a fallback used only when the CSV leaves that field blank —
    // the CSV always wins. See CsvParser for the columns that supply these overrides.
    private const bool DefaultAllowGuaranteedMsgSendEnabled = true;
    private const bool DefaultAllowGuaranteedMsgReceiveEnabled = true;
    private const bool DefaultCompressionEnabled = true;
    private const bool DefaultReplicationAllowClientConnectWhenStandbyEnabled = false;
    private const bool DefaultAllowTransactedSessionsEnabled = true;
    private const bool DefaultAllowBridgeConnectionsEnabled = true;
    private const bool DefaultAllowGuaranteedEndpointCreateEnabled = true;
    private const bool DefaultAllowSharedSubscriptionsEnabled = false;

    private const string DefaultClientConnectDefaultAction = "allow";
    private const string DefaultPublishTopicDefaultAction = "disallow";
    private const string DefaultSubscribeShareNameDefaultAction = "allow";
    private const string DefaultSubscribeTopicDefaultAction = "disallow";

    private const bool DefaultClientUserEnabled = true;
    private const bool DefaultGuaranteedEndpointPermissionOverrideEnabled = true;
    private const bool DefaultSubscriptionManagerEnabled = true;

    private const string DefaultQueuePermission = "no-access";
    private const bool DefaultQueueEgressEnabled = true;
    private const int DefaultQueueMaxRedeliveryCount = 4;

    // Built as plain Dictionary<string, object> rather than typed POCOs: the workflow
    // host's serializer reflects over raw property names and always writes nulls,
    // ignoring System.Text.Json's [JsonPropertyName]/[JsonIgnore] attributes entirely.
    // A dictionary's keys ARE the JSON keys, so this is the only reliable way to get
    // exact Solace field names (e.g. "create_ACL", "SUBSCRIPTION_LIST") and to omit a
    // field/section entirely rather than emit it as null.
    public IDictionary<string, object> Build(SolaceOnboardingRecord record)
    {
        var sections = ResolveSections(record.Action);

        if (sections.HasFlag(SolaceSections.Queue) && record.MessageTypes.Count == 0)
            throw new FormatException(
                $"Action '{record.Action}' needs at least one MessageType/Topic row " +
                $"for record '{record.NamingPrefix}', but none were given.");

        var prefix  = record.NamingPrefix;      // e.g. petc-rc-navision-3plpnp-sys
        var cpName  = $"{prefix}-cp";           // client profile name
        var aclName = $"{prefix}-acl";          // ACL profile name
        var cuName  = $"{prefix}-cu";           // client username

        var config = new Dictionary<string, object>();

        if (sections.HasFlag(SolaceSections.ClientProfile))
            config["clientProfile"] = BuildClientProfile(cpName, record.ClientProfile);

        if (sections.HasFlag(SolaceSections.Acl))
            config["ACL"] = BuildAcl(aclName, record.Acl);

        if (sections.HasFlag(SolaceSections.ClientUser))
            config["ClientUser"] = BuildClientUser(aclName, cpName, cuName, record.EncryptedPassword, record.ClientUser);

        if (sections.HasFlag(SolaceSections.Queue))
            config["Queue"] = BuildQueues(prefix, cuName, record.MessageTypes);

        if (sections.HasFlag(SolaceSections.Subscription))
            config["Subscription"] = BuildSubscriptions(prefix, record.MessageTypes);

        return config;
    }

    // Recognized Action values (case-insensitive) and the sections each one emits.
    // Blank/absent Action defaults to FullOnboarding — the CSV's Action column is optional.
    private static SolaceSections ResolveSections(string action) => action.Trim().ToLowerInvariant() switch
    {
        "" or "fullonboarding" => SolaceSections.All,
        "addsubscription"      => SolaceSections.Subscription,
        "addqueue"             => SolaceSections.Queue | SolaceSections.Subscription,
        "addclientuser"        => SolaceSections.ClientUser | SolaceSections.Queue | SolaceSections.Subscription,
        "updateacl"            => SolaceSections.Acl,
        _ => throw new FormatException(
            $"Unrecognized Action '{action}'. Expected one of: FullOnboarding, AddSubscription, " +
            "AddQueue, AddClientUser, UpdateAcl.")
    };

    // ── Sections ──────────────────────────────────────────────────────────────

    private static Dictionary<string, object> BuildClientProfile(string cpName, ClientProfileOverrides o) => new()
    {
        ["create"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["name"] = cpName,
                ["allowGuaranteedMsgSendEnabled"] = ResolveBool(
                    o.AllowGuaranteedMsgSendEnabled, DefaultAllowGuaranteedMsgSendEnabled,
                    nameof(o.AllowGuaranteedMsgSendEnabled)),
                ["allowGuaranteedMsgReceiveEnabled"] = ResolveBool(
                    o.AllowGuaranteedMsgReceiveEnabled, DefaultAllowGuaranteedMsgReceiveEnabled,
                    nameof(o.AllowGuaranteedMsgReceiveEnabled)),
                ["compressionEnabled"] = ResolveBool(
                    o.CompressionEnabled, DefaultCompressionEnabled, nameof(o.CompressionEnabled)),
                ["replicationAllowClientConnectWhenStandbyEnabled"] = ResolveBool(
                    o.ReplicationAllowClientConnectWhenStandbyEnabled,
                    DefaultReplicationAllowClientConnectWhenStandbyEnabled,
                    nameof(o.ReplicationAllowClientConnectWhenStandbyEnabled)),
                ["allowTransactedSessionsEnabled"] = ResolveBool(
                    o.AllowTransactedSessionsEnabled, DefaultAllowTransactedSessionsEnabled,
                    nameof(o.AllowTransactedSessionsEnabled)),
                ["allowBridgeConnectionsEnabled"] = ResolveBool(
                    o.AllowBridgeConnectionsEnabled, DefaultAllowBridgeConnectionsEnabled,
                    nameof(o.AllowBridgeConnectionsEnabled)),
                ["allowGuaranteedEndpointCreateEnabled"] = ResolveBool(
                    o.AllowGuaranteedEndpointCreateEnabled, DefaultAllowGuaranteedEndpointCreateEnabled,
                    nameof(o.AllowGuaranteedEndpointCreateEnabled)),
                ["allowSharedSubscriptionsEnabled"] = ResolveBool(
                    o.AllowSharedSubscriptionsEnabled, DefaultAllowSharedSubscriptionsEnabled,
                    nameof(o.AllowSharedSubscriptionsEnabled))
            }
        }
    };

    private static Dictionary<string, object> BuildAcl(string aclName, AclOverrides o) => new()
    {
        ["create_ACL"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["aclProfileName"] = aclName,
                ["clientConnectDefaultAction"] = ResolveString(
                    o.ClientConnectDefaultAction, DefaultClientConnectDefaultAction),
                ["publishTopicDefaultAction"] = ResolveString(
                    o.PublishTopicDefaultAction, DefaultPublishTopicDefaultAction),
                ["subscribeShareNameDefaultAction"] = ResolveString(
                    o.SubscribeShareNameDefaultAction, DefaultSubscribeShareNameDefaultAction),
                ["subscribeTopicDefaultAction"] = ResolveString(
                    o.SubscribeTopicDefaultAction, DefaultSubscribeTopicDefaultAction)
            }
        }
    };

    private static Dictionary<string, object> BuildClientUser(
        string aclName, string cpName, string cuName, string encryptedPassword, ClientUserOverrides o) => new()
    {
        ["create"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["aclProfileName"] = aclName,
                ["clientProfileName"] = cpName,
                ["clientUsername"] = cuName,
                ["enabled"] = ResolveBool(o.Enabled, DefaultClientUserEnabled, nameof(o.Enabled)),
                ["guaranteedEndpointPermissionOverrideEnabled"] = ResolveBool(
                    o.GuaranteedEndpointPermissionOverrideEnabled,
                    DefaultGuaranteedEndpointPermissionOverrideEnabled,
                    nameof(o.GuaranteedEndpointPermissionOverrideEnabled)),
                ["password"] = encryptedPassword,
                ["subscriptionManagerEnabled"] = ResolveBool(
                    o.SubscriptionManagerEnabled, DefaultSubscriptionManagerEnabled,
                    nameof(o.SubscriptionManagerEnabled))
            }
        }
    };

    // One DMQ + one main queue per distinct MessageType present in the record's rows —
    // not a fixed list, so a brand-new MessageType value just needs new CSV rows.
    // The DMQ's "consume" permission isn't an override: that's what makes it a DMQ.
    private static Dictionary<string, object> BuildQueues(
        string prefix, string cuName, IReadOnlyDictionary<string, MessageTypeEntry> messageTypes)
    {
        var queues = new List<object>();

        foreach (var (messageType, _) in messageTypes)
        {
            queues.Add(new Dictionary<string, object>
            {
                ["queueName"] = $"dmq-{prefix}-{Slugify(messageType)}",
                ["owner"] = cuName,
                ["permission"] = "consume",
                ["egressEnabled"] = true
            });
        }

        foreach (var (messageType, entry) in messageTypes)
        {
            var slug = Slugify(messageType);

            queues.Add(new Dictionary<string, object>
            {
                ["queueName"] = $"q-{prefix}-{slug}",
                ["deadMsgQueue"] = $"dmq-{prefix}-{slug}",
                ["owner"] = cuName,
                ["permission"] = ResolveString(entry.QueuePermission, DefaultQueuePermission),
                ["egressEnabled"] = ResolveBool(
                    entry.QueueEgressEnabled, DefaultQueueEgressEnabled, nameof(entry.QueueEgressEnabled)),
                ["maxRedeliveryCount"] = ResolveInt(
                    entry.QueueMaxRedeliveryCount, DefaultQueueMaxRedeliveryCount,
                    nameof(entry.QueueMaxRedeliveryCount))
            });
        }

        return new Dictionary<string, object> { ["create"] = queues };
    }

    private static Dictionary<string, object> BuildSubscriptions(
        string prefix, IReadOnlyDictionary<string, MessageTypeEntry> messageTypes)
    {
        var entries = new List<object>();

        foreach (var (messageType, entry) in messageTypes)
        {
            if (entry.Topics.Count == 0) continue;

            entries.Add(new Dictionary<string, object>
            {
                ["queueName"] = $"q-{prefix}-{Slugify(messageType)}",
                ["SUBSCRIPTION_LIST"] = new List<string>(entry.Topics)
            });
        }

        return new Dictionary<string, object> { ["create"] = entries };
    }

    // Normalizes a MessageType value (e.g. "DespatchStock") into the lowercase slug
    // used in resource names (e.g. "despatchstock") — case-insensitive on input.
    private static string Slugify(string messageType) => messageType.Trim().ToLowerInvariant();

    // ── CSV override resolution — blank cell = default, non-blank = must parse ────

    private static bool ResolveBool(string raw, bool defaultValue, string fieldName)
    {
        if (raw.Length == 0) return defaultValue;

        if (bool.TryParse(raw, out var parsed)) return parsed;

        throw new FormatException($"'{raw}' is not a valid true/false value for {fieldName}.");
    }

    private static int ResolveInt(string raw, int defaultValue, string fieldName)
    {
        if (raw.Length == 0) return defaultValue;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;

        throw new FormatException($"'{raw}' is not a valid whole number for {fieldName}.");
    }

    private static string ResolveString(string raw, string defaultValue) => raw.Length == 0 ? defaultValue : raw;
}
