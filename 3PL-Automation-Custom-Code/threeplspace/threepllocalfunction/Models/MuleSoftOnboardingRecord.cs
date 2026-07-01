using System.Collections.Generic;

namespace MuleSoftAutomation.Models;

// ============================================================================
// MuleSoft onboarding models — CSV-shaped data for one NAV requirements doc
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Holds exactly what MuleSoftCsvParser reads from one requirements CSV
//   (always one country/partner per CSV, unlike Solace's multi-record CSV),
//   with zero YAML rendering or business-default logic — that lives entirely
//   in MuleSoftYamlBuilder. Lists (transaction types, message types,
//   mappings) are plain IReadOnlyList/IReadOnlyDictionary so a new entry just
//   needs a new CSV row, never a code change.
// HOW TO USE:
//   Produced by MuleSoftCsvParser.Parse(), consumed by
//   MuleSoftYamlBuilder.BuildNew()/MergeIntoExisting(). Not constructed
//   directly outside those two classes.
// IMPORTANT NOTES:
//   Entirely separate from SolaceOnboardingRecord (different namespace,
//   different shape, different CSV) — the two domains share no model types.
// ============================================================================
/// <summary>One environment's NAV connector overrides (app/dev/tst/prod) — see MuleSoftCsvParser.</summary>
public sealed record EnvironmentOverride(
    string Host,
    string Company,
    string SoapPath,
    string RoutingCode);

/// <summary>One NAV transaction type entry, e.g. sal_008 -> { enabled: true, type: SHIPMENT }.</summary>
public sealed record TransactionTypeEntry(
    string Code,
    string Enabled,
    string Label);

/// <summary>One translation source/destination combination mapping.</summary>
public sealed record SourceDestinationMapping(string From, string To);

/// <summary>One translation unit-of-measure mapping.</summary>
public sealed record UomMapping(string From, string To);

/// <summary>
/// One MuleSoft NAV onboarding requirements record, parsed from a single CSV
/// (always one country/partner per CSV — see MuleSoftCsvParser). Everything
/// needed to produce or patch app.yaml/dev.yaml/tst.yaml/prod.yaml.
/// </summary>
public sealed record MuleSoftOnboardingRecord(
    string CountryKey,
    string CountryCode,
    string PartnerComment,
    string CreatedBy,
    string NavProtocol,
    string NavPort,
    string NavUsername,
    string NavDomain,
    string NavService,
    string NavSoapPort,
    string NavUseCommonCert,
    string TranslationReceiverName,
    IReadOnlyDictionary<string, EnvironmentOverride> EnvironmentOverrides, // key: app|dev|tst|prod
    IReadOnlyList<TransactionTypeEntry> TransactionTypes,
    IReadOnlyList<string> MessageTypes,
    IReadOnlyList<SourceDestinationMapping> SourceDestinationMappings,
    IReadOnlyList<UomMapping> UomMappings);
