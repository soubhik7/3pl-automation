using System.Collections.Generic;
using SolaceConfigGenerator.Models;

namespace SolaceConfigGenerator.Services;

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

    public SolaceConfig Build(SolaceOnboardingRecord record)
    {
        var prefix  = record.NamingPrefix;      // e.g. petc-rc-navision-3plpnp-sys
        var cpName  = $"{prefix}-cp";           // client profile name
        var aclName = $"{prefix}-acl";          // ACL profile name
        var cuName  = $"{prefix}-cu";           // client username

        return new SolaceConfig
        {
            ClientProfile = BuildClientProfile(cpName),
            ACL           = BuildAcl(aclName),
            ClientUser    = BuildClientUser(aclName, cpName, cuName, record.EncryptedPassword),
            Queue         = BuildQueues(prefix, cuName),
            Subscription  = BuildSubscriptions(prefix, record)
        };
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private static ClientProfileSection BuildClientProfile(string cpName) => new()
    {
        Create = [new ClientProfileEntry { Name = cpName }]
    };

    private static AclSection BuildAcl(string aclName) => new()
    {
        CreateAcl = [new AclEntry { AclProfileName = aclName }]
    };

    private static ClientUserSection BuildClientUser(
        string aclName, string cpName, string cuName, string encryptedPassword) => new()
    {
        Create =
        [
            new ClientUserEntry
            {
                AclProfileName    = aclName,
                ClientProfileName = cpName,
                ClientUsername    = cuName,
                Password          = encryptedPassword
            }
        ]
    };

    private static QueueSection BuildQueues(string prefix, string cuName)
    {
        var queues = new List<QueueEntry>(MessageTypes.Length * 2);

        // Dead message queues first (no DMQ reference, permission = consume)
        foreach (var msgType in MessageTypes)
        {
            queues.Add(new QueueEntry
            {
                QueueName   = $"dmq-{prefix}-{msgType}",
                Owner       = cuName,
                Permission  = "consume",
                EgressEnabled = true
            });
        }

        // Regular queues pointing to their DMQ (permission = no-access, maxRedeliveryCount = 4)
        foreach (var msgType in MessageTypes)
        {
            queues.Add(new QueueEntry
            {
                QueueName          = $"q-{prefix}-{msgType}",
                DeadMsgQueue       = $"dmq-{prefix}-{msgType}",
                Owner              = cuName,
                Permission         = "no-access",
                EgressEnabled      = true,
                MaxRedeliveryCount = 4
            });
        }

        return new QueueSection { Create = queues };
    }

    private static SubscriptionSection BuildSubscriptions(string prefix, SolaceOnboardingRecord record)
    {
        var entries = new List<SubscriptionEntry>(MessageTypes.Length);

        AddIfAny(entries, $"q-{prefix}-despatchstock",  record.DespatchStockTopics);
        AddIfAny(entries, $"q-{prefix}-sourcingstock",  record.SourcingStockTopics);
        AddIfAny(entries, $"q-{prefix}-returnstock",    record.ReturnStockTopics);
        AddIfAny(entries, $"q-{prefix}-stockmovement",  record.StockMovementTopics);

        return new SubscriptionSection { Create = entries };
    }

    private static void AddIfAny(List<SubscriptionEntry> entries, string queueName, IReadOnlyList<string> topics)
    {
        if (topics.Count == 0) return;

        entries.Add(new SubscriptionEntry
        {
            QueueName        = queueName,
            SubscriptionList = [.. topics]
        });
    }
}
