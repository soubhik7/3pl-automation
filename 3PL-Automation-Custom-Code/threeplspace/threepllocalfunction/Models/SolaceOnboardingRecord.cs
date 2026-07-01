using System.Collections.Generic;

namespace SolaceConfigGenerator.Models;

// ============================================================================
// Solace onboarding models — CSV-shaped, pre-default-resolution data
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   These records hold the raw CSV cell values (as literal strings, "" when
//   blank) for one onboarding/change request, with zero business defaults
//   baked in — that decision belongs entirely to SolaceConfigBuilder, which
//   is the only place that knows what a blank field should fall back to.
//   Keeping that logic out of the models means every value here traces back
//   to an actual CSV column (see CsvParser), satisfying "no hardcoded
//   mapping values" at the data layer.
// HOW TO USE:
//   Produced by CsvParser.Parse(), consumed by SolaceConfigBuilder.Build().
//   Not constructed directly outside those two classes.
// IMPORTANT NOTES:
//   NamingPrefix is the one piece of derived (not CSV-sourced) data — it's a
//   fixed naming convention ({Brand}-{Env}-{SystemName}-{ThreePLCode}-sys),
//   not a business policy value, so it's a computed property, not a default.
// ============================================================================
/// <summary>
/// Raw client-profile flag overrides from the CSV. Each field is the literal cell value
/// ("" if not supplied) — SolaceConfigBuilder decides the default when a field is blank,
/// nothing is hardcoded at the model layer.
/// </summary>
public sealed record ClientProfileOverrides(
    string AllowGuaranteedMsgSendEnabled,
    string AllowGuaranteedMsgReceiveEnabled,
    string CompressionEnabled,
    string ReplicationAllowClientConnectWhenStandbyEnabled,
    string AllowTransactedSessionsEnabled,
    string AllowBridgeConnectionsEnabled,
    string AllowGuaranteedEndpointCreateEnabled,
    string AllowSharedSubscriptionsEnabled);

/// <summary>Raw ACL default-action overrides from the CSV — "" means not supplied.</summary>
public sealed record AclOverrides(
    string ClientConnectDefaultAction,
    string PublishTopicDefaultAction,
    string SubscribeShareNameDefaultAction,
    string SubscribeTopicDefaultAction);

/// <summary>Raw client-user flag overrides from the CSV — "" means not supplied.</summary>
public sealed record ClientUserOverrides(
    string Enabled,
    string GuaranteedEndpointPermissionOverrideEnabled,
    string SubscriptionManagerEnabled);

/// <summary>
/// One MessageType's topics plus its main queue's overrides ("" means not supplied,
/// falls back to SolaceConfigBuilder's default). The dead-message queue's permission
/// ("consume") is not an override — that's the structural definition of a DMQ, not a
/// business policy value.
/// </summary>
public sealed record MessageTypeEntry(
    IReadOnlyList<string> Topics,
    string QueuePermission,
    string QueueEgressEnabled,
    string QueueMaxRedeliveryCount);

/// <summary>
/// One onboarding/change record, built by grouping CSV rows that share the same
/// Brand/Env/SystemName/ThreePLCode. See CsvParser for the CSV's row format —
/// it's one row per topic, not one row per record.
/// </summary>
public sealed record SolaceOnboardingRecord(
    string Brand,
    string Env,
    string SystemName,
    string ThreePLCode,
    string EncryptedPassword,
    string Action,
    ClientProfileOverrides ClientProfile,
    AclOverrides Acl,
    ClientUserOverrides ClientUser,
    IReadOnlyDictionary<string, MessageTypeEntry> MessageTypes
)
{
    // Drives all Solace resource names: {Brand}-{Env}-{SystemName}-{ThreePLCode}-sys
    public string NamingPrefix => $"{Brand}-{Env}-{SystemName}-{ThreePLCode}-sys";
}
