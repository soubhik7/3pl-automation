using System;
using System.Collections.Generic;
using System.Linq;
using SolaceConfigGenerator.Models;
using ThreePlLocalFunction.Shared;

namespace SolaceConfigGenerator.Services;

// ============================================================================
// CsvParser (Solace) — turns the Solace onboarding CSV into typed records
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   This is the only place that knows the Solace CSV's column layout. It
//   exists so the CSV can stay a plain, spreadsheet-friendly long/normalized
//   format (one row per topic) while still producing exactly the structured
//   data SolaceConfigBuilder needs — without either of those two classes
//   knowing about the other's internals.
// HOW TO USE:
//   var records = new CsvParser().Parse(csvContent); — see the column list
//   and worked example below. Malformed rows (wrong column count, unknown
//   RowType-equivalent Action) throw FormatException/ArgumentException with
//   the offending row's index and content, so a bad CSV fails the Logic Apps
//   action clearly instead of silently producing wrong config.
// IMPORTANT NOTES:
//   This parser is entirely separate from MuleSoftCsvParser — different
//   column layout, different namespace (SolaceConfigGenerator.Services vs
//   MuleSoftAutomation.Services) — the two only share the stateless,
//   format-level CsvLineSplitter helper.
// ============================================================================
/// <summary>
/// Parses the onboarding CSV into SolaceOnboardingRecord instances.
///
/// Long format — one row per topic, not one row per record. Record-level columns
/// (everything except MessageType/Topic/Queue*) repeat on every row belonging to the
/// same record; rows are grouped by (Brand, Env, SystemName, ThreePLCode) into one
/// SolaceOnboardingRecord per group. This keeps the CSV easy to fill in a spreadsheet
/// (one topic per line, no pipe-delimited cells) and lets new MessageType values
/// (queue types) show up with no code change — see SolaceConfigBuilder.
///
/// Columns (order matters, header row required — all but the first 4 are optional,
/// leave blank to use SolaceConfigBuilder's default):
///   Brand, Env, SystemName, ThreePLCode, EncryptedPassword, Action,
///   ClientProfileAllowGuaranteedMsgSendEnabled, ClientProfileAllowGuaranteedMsgReceiveEnabled,
///   ClientProfileCompressionEnabled, ClientProfileReplicationAllowClientConnectWhenStandbyEnabled,
///   ClientProfileAllowTransactedSessionsEnabled, ClientProfileAllowBridgeConnectionsEnabled,
///   ClientProfileAllowGuaranteedEndpointCreateEnabled, ClientProfileAllowSharedSubscriptionsEnabled,
///   AclClientConnectDefaultAction, AclPublishTopicDefaultAction,
///   AclSubscribeShareNameDefaultAction, AclSubscribeTopicDefaultAction,
///   ClientUserEnabled, ClientUserGuaranteedEndpointPermissionOverrideEnabled,
///   ClientUserSubscriptionManagerEnabled,
///   MessageType, Topic, QueuePermission, QueueEgressEnabled, QueueMaxRedeliveryCount
///
/// EncryptedPassword/Action/ClientProfile*/Acl*/ClientUser* are read from each group's
/// first row — leave them blank on later rows for the same record. QueuePermission/
/// QueueEgressEnabled/QueueMaxRedeliveryCount apply per MessageType (the main queue for
/// that type), taken from the first row of that MessageType that supplies each field —
/// so different topics of the same MessageType don't need to repeat them.
///
/// Example (one FullOnboarding record, all overrides left at their defaults):
///   Brand,Env,SystemName,ThreePLCode,EncryptedPassword,Action,,,,,,,,,,,,,,,,MessageType,Topic,,,
///   petc,rc,navision,3plpnp,"![+ycEERpRxxm5RfqLRNxBXw==]",FullOnboarding,,,,,,,,,,,,,,,,,DespatchStock,scx/whm/.../wmsfml_pl/...,,,
///   petc,rc,navision,3plpnp,,,,,,,,,,,,,,,,,,DespatchStock,scx/whm/.../wmsfml_cz/...,,,
/// </summary>
public sealed class CsvParser
{
    private const int ColumnCount = 26;

    public IReadOnlyList<SolaceOnboardingRecord> Parse(string csvContent)
    {
        var lines = csvContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
            throw new ArgumentException("CSV must contain a header row and at least one data row.");

        var rowsByGroupKey = new Dictionary<string, List<CsvRow>>();
        var groupKeyOrder = new List<string>();

        for (var i = 1; i < lines.Length; i++)
        {
            var row = ParseRow(lines[i], i);
            var groupKey = $"{row.Brand}{row.Env}{row.SystemName}{row.ThreePLCode}";

            if (!rowsByGroupKey.TryGetValue(groupKey, out var rows))
            {
                rows = [];
                rowsByGroupKey[groupKey] = rows;
                groupKeyOrder.Add(groupKey);
            }

            rows.Add(row);
        }

        return groupKeyOrder.Select(key => BuildRecord(rowsByGroupKey[key])).ToList();
    }

    private static CsvRow ParseRow(string line, int rowIndex)
    {
        var fields = CsvLineSplitter.Split(line);

        if (fields.Count < ColumnCount)
            throw new FormatException(
                $"Row {rowIndex + 1} has {fields.Count} column(s); expected {ColumnCount}. " +
                $"Row content: {line}");

        for (var i = 0; i < fields.Count; i++)
            fields[i] = i == 4 ? fields[i] : fields[i].Trim(); // EncryptedPassword (index 4) preserved exactly

        return new CsvRow(
            Brand:                fields[0],
            Env:                  fields[1],
            SystemName:           fields[2],
            ThreePLCode:          fields[3],
            EncryptedPassword:    fields[4],
            Action:               fields[5],
            CpSendEnabled:        fields[6],
            CpReceiveEnabled:     fields[7],
            CpCompression:        fields[8],
            CpReplicationStandby: fields[9],
            CpTransactedSessions: fields[10],
            CpBridgeConnections:  fields[11],
            CpEndpointCreate:     fields[12],
            CpSharedSubs:         fields[13],
            AclClientConnect:     fields[14],
            AclPublishTopic:      fields[15],
            AclSubscribeShare:    fields[16],
            AclSubscribeTopic:    fields[17],
            CuEnabled:            fields[18],
            CuEndpointOverride:   fields[19],
            CuSubscriptionMgr:    fields[20],
            MessageType:          fields[21],
            Topic:                fields[22],
            QueuePermission:      fields[23],
            QueueEgressEnabled:   fields[24],
            QueueMaxRedeliveryCount: fields[25]
        );
    }

    private static SolaceOnboardingRecord BuildRecord(List<CsvRow> rows)
    {
        var first = rows[0];
        var messageTypes = new Dictionary<string, MessageTypeAccumulator>();

        foreach (var row in rows)
        {
            if (row.MessageType.Length == 0) continue;

            if (!messageTypes.TryGetValue(row.MessageType, out var acc))
            {
                acc = new MessageTypeAccumulator();
                messageTypes[row.MessageType] = acc;
            }

            if (row.Topic.Length > 0)
                acc.Topics.Add(row.Topic);

            acc.Permission        ??= row.QueuePermission.Length        > 0 ? row.QueuePermission        : null;
            acc.EgressEnabled     ??= row.QueueEgressEnabled.Length     > 0 ? row.QueueEgressEnabled     : null;
            acc.MaxRedeliveryCount ??= row.QueueMaxRedeliveryCount.Length > 0 ? row.QueueMaxRedeliveryCount : null;
        }

        return new SolaceOnboardingRecord(
            Brand:             first.Brand,
            Env:               first.Env,
            SystemName:        first.SystemName,
            ThreePLCode:       first.ThreePLCode,
            EncryptedPassword: first.EncryptedPassword,
            Action:            first.Action,
            ClientProfile: new ClientProfileOverrides(
                AllowGuaranteedMsgSendEnabled:    first.CpSendEnabled,
                AllowGuaranteedMsgReceiveEnabled: first.CpReceiveEnabled,
                CompressionEnabled:               first.CpCompression,
                ReplicationAllowClientConnectWhenStandbyEnabled: first.CpReplicationStandby,
                AllowTransactedSessionsEnabled:   first.CpTransactedSessions,
                AllowBridgeConnectionsEnabled:    first.CpBridgeConnections,
                AllowGuaranteedEndpointCreateEnabled: first.CpEndpointCreate,
                AllowSharedSubscriptionsEnabled:  first.CpSharedSubs),
            Acl: new AclOverrides(
                ClientConnectDefaultAction:      first.AclClientConnect,
                PublishTopicDefaultAction:       first.AclPublishTopic,
                SubscribeShareNameDefaultAction: first.AclSubscribeShare,
                SubscribeTopicDefaultAction:     first.AclSubscribeTopic),
            ClientUser: new ClientUserOverrides(
                Enabled:                                     first.CuEnabled,
                GuaranteedEndpointPermissionOverrideEnabled:  first.CuEndpointOverride,
                SubscriptionManagerEnabled:                   first.CuSubscriptionMgr),
            MessageTypes: messageTypes.ToDictionary(
                kv => kv.Key,
                kv => new MessageTypeEntry(
                    Topics:                 kv.Value.Topics,
                    QueuePermission:        kv.Value.Permission ?? "",
                    QueueEgressEnabled:     kv.Value.EgressEnabled ?? "",
                    QueueMaxRedeliveryCount: kv.Value.MaxRedeliveryCount ?? ""))
        );
    }

    private sealed class MessageTypeAccumulator
    {
        public List<string> Topics { get; } = [];
        public string? Permission { get; set; }
        public string? EgressEnabled { get; set; }
        public string? MaxRedeliveryCount { get; set; }
    }

    private readonly record struct CsvRow(
        string Brand, string Env, string SystemName, string ThreePLCode,
        string EncryptedPassword, string Action,
        string CpSendEnabled, string CpReceiveEnabled, string CpCompression, string CpReplicationStandby,
        string CpTransactedSessions, string CpBridgeConnections, string CpEndpointCreate, string CpSharedSubs,
        string AclClientConnect, string AclPublishTopic, string AclSubscribeShare, string AclSubscribeTopic,
        string CuEnabled, string CuEndpointOverride, string CuSubscriptionMgr,
        string MessageType, string Topic,
        string QueuePermission, string QueueEgressEnabled, string QueueMaxRedeliveryCount);
}
