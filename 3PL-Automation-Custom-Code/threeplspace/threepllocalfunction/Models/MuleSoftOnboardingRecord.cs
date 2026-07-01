using System.Collections.Generic;

namespace MuleSoftAutomation.Models;

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
