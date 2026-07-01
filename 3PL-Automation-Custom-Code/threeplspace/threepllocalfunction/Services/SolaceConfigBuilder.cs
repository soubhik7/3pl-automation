using System;
using System.Collections.Generic;
using SolaceConfigGenerator.Models;

namespace SolaceConfigGenerator.Services;

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
    // Fixed message types that drive queue and subscription creation
    private static readonly string[] MessageTypes =
    [
        "despatchstock",
        "sourcingstock",
        "returnstock",
        "stockmovement"
    ];

    // Built as plain Dictionary<string, object> rather than typed POCOs: the workflow
    // host's serializer reflects over raw property names and always writes nulls,
    // ignoring System.Text.Json's [JsonPropertyName]/[JsonIgnore] attributes entirely.
    // A dictionary's keys ARE the JSON keys, so this is the only reliable way to get
    // exact Solace field names (e.g. "create_ACL", "SUBSCRIPTION_LIST") and to omit a
    // field/section entirely rather than emit it as null.
    public IDictionary<string, object> Build(SolaceOnboardingRecord record)
    {
        var sections = ResolveSections(record.Action);

        var prefix  = record.NamingPrefix;      // e.g. petc-rc-navision-3plpnp-sys
        var cpName  = $"{prefix}-cp";           // client profile name
        var aclName = $"{prefix}-acl";          // ACL profile name
        var cuName  = $"{prefix}-cu";           // client username

        var config = new Dictionary<string, object>();

        if (sections.HasFlag(SolaceSections.ClientProfile))
            config["clientProfile"] = BuildClientProfile(cpName);

        if (sections.HasFlag(SolaceSections.Acl))
            config["ACL"] = BuildAcl(aclName);

        if (sections.HasFlag(SolaceSections.ClientUser))
            config["ClientUser"] = BuildClientUser(aclName, cpName, cuName, record.EncryptedPassword);

        if (sections.HasFlag(SolaceSections.Queue))
            config["Queue"] = BuildQueues(prefix, cuName);

        if (sections.HasFlag(SolaceSections.Subscription))
            config["Subscription"] = BuildSubscriptions(prefix, record);

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

    private static Dictionary<string, object> BuildClientProfile(string cpName) => new()
    {
        ["create"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["name"] = cpName,
                ["allowGuaranteedMsgSendEnabled"] = true,
                ["allowGuaranteedMsgReceiveEnabled"] = true,
                ["compressionEnabled"] = true,
                ["replicationAllowClientConnectWhenStandbyEnabled"] = false,
                ["allowTransactedSessionsEnabled"] = true,
                ["allowBridgeConnectionsEnabled"] = true,
                ["allowGuaranteedEndpointCreateEnabled"] = true,
                ["allowSharedSubscriptionsEnabled"] = false
            }
        }
    };

    private static Dictionary<string, object> BuildAcl(string aclName) => new()
    {
        ["create_ACL"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["aclProfileName"] = aclName,
                ["clientConnectDefaultAction"] = "allow",
                ["publishTopicDefaultAction"] = "disallow",
                ["subscribeShareNameDefaultAction"] = "allow",
                ["subscribeTopicDefaultAction"] = "disallow"
            }
        }
    };

    private static Dictionary<string, object> BuildClientUser(
        string aclName, string cpName, string cuName, string encryptedPassword) => new()
    {
        ["create"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["aclProfileName"] = aclName,
                ["clientProfileName"] = cpName,
                ["clientUsername"] = cuName,
                ["enabled"] = true,
                ["guaranteedEndpointPermissionOverrideEnabled"] = true,
                ["password"] = encryptedPassword,
                ["subscriptionManagerEnabled"] = true
            }
        }
    };

    private static Dictionary<string, object> BuildQueues(string prefix, string cuName)
    {
        var queues = new List<object>(MessageTypes.Length * 2);

        // Dead message queues first (no deadMsgQueue/maxRedeliveryCount keys at all, permission = consume)
        foreach (var msgType in MessageTypes)
        {
            queues.Add(new Dictionary<string, object>
            {
                ["queueName"] = $"dmq-{prefix}-{msgType}",
                ["owner"] = cuName,
                ["permission"] = "consume",
                ["egressEnabled"] = true
            });
        }

        // Regular queues pointing to their DMQ (permission = no-access, maxRedeliveryCount = 4)
        foreach (var msgType in MessageTypes)
        {
            queues.Add(new Dictionary<string, object>
            {
                ["queueName"] = $"q-{prefix}-{msgType}",
                ["deadMsgQueue"] = $"dmq-{prefix}-{msgType}",
                ["owner"] = cuName,
                ["permission"] = "no-access",
                ["egressEnabled"] = true,
                ["maxRedeliveryCount"] = 4
            });
        }

        return new Dictionary<string, object> { ["create"] = queues };
    }

    private static Dictionary<string, object> BuildSubscriptions(string prefix, SolaceOnboardingRecord record)
    {
        var entries = new List<object>(MessageTypes.Length);

        AddIfAny(entries, $"q-{prefix}-despatchstock",  record.DespatchStockTopics);
        AddIfAny(entries, $"q-{prefix}-sourcingstock",  record.SourcingStockTopics);
        AddIfAny(entries, $"q-{prefix}-returnstock",    record.ReturnStockTopics);
        AddIfAny(entries, $"q-{prefix}-stockmovement",  record.StockMovementTopics);

        return new Dictionary<string, object> { ["create"] = entries };
    }

    private static void AddIfAny(List<object> entries, string queueName, IReadOnlyList<string> topics)
    {
        if (topics.Count == 0) return;

        entries.Add(new Dictionary<string, object>
        {
            ["queueName"] = queueName,
            ["SUBSCRIPTION_LIST"] = new List<string>(topics)
        });
    }
}
