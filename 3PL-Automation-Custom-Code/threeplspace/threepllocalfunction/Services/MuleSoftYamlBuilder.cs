using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MuleSoftAutomation.Models;

namespace MuleSoftAutomation.Services;

/// <summary>
/// Builds or incrementally patches the 4 MuleSoft NAV config files (app.yaml, dev.yaml,
/// tst.yaml, prod.yaml) matching the shape in
/// 3PL-Automation-AI-Foundry/templates/mulesoft-template.yaml. app.yaml holds the
/// shared/base config; dev/tst/prod.yaml hold only that environment's NAV overrides.
///
/// This is a purpose-built writer/reader for exactly this fixed shape, not a general
/// YAML library — ParseExisting* only understands files this builder itself produced
/// (or files that follow the same key layout/indentation).
/// </summary>
public sealed class MuleSoftYamlBuilder
{
    private const string PasswordPlaceholder = "<SET_VIA_KEY_VAULT>";

    // ── New config ──────────────────────────────────────────────────────────────

    public IDictionary<string, string> BuildNew(MuleSoftOnboardingRecord record)
    {
        var files = new Dictionary<string, string>
        {
            ["app.yaml"] = RenderAppYaml(record, existingTransactionTypes: [], existingMessageTypes: [],
                existingSourceDestinations: [], existingUomMappings: [])
        };

        foreach (var env in new[] { "dev", "tst", "prod" })
        {
            if (record.EnvironmentOverrides.TryGetValue(env, out var overrides))
                files[$"{env}.yaml"] = RenderEnvYaml(overrides);
        }

        return files;
    }

    // ── Incremental update ──────────────────────────────────────────────────────

    // Merge semantics: a non-blank CSV value overrides the existing scalar; a blank
    // CSV value keeps whatever the existing file already had. List items (transaction
    // types, message types, source/destination and UOM mappings) are upserted by key
    // onto the existing list — new keys are appended in incremental order, matching
    // keys are updated in place, and untouched entries are left exactly as they were.
    public IDictionary<string, string> MergeIntoExisting(
        MuleSoftOnboardingRecord record, IReadOnlyDictionary<string, string> existingByFile)
    {
        var files = new Dictionary<string, string>();

        var hasExistingApp = existingByFile.TryGetValue("app.yaml", out var existingApp) && existingApp.Length > 0;
        var existingAppData = hasExistingApp ? ParseAppYaml(existingApp!) : null;

        var mergedRecord = hasExistingApp ? MergeAppScalars(record, existingAppData!) : record;

        var transactionTypes = UpsertTransactionTypes(
            existingAppData?.TransactionTypes ?? [], record.TransactionTypes);
        var messageTypes = UpsertMessageTypes(
            existingAppData?.MessageTypes ?? [], record.MessageTypes);
        var sourceDestinations = UpsertSourceDestinations(
            existingAppData?.SourceDestinationMappings ?? [], record.SourceDestinationMappings);
        var uomMappings = UpsertUomMappings(
            existingAppData?.UomMappings ?? [], record.UomMappings);

        files["app.yaml"] = RenderAppYaml(mergedRecord, transactionTypes, messageTypes, sourceDestinations, uomMappings);

        foreach (var env in new[] { "dev", "tst", "prod" })
        {
            var hasExistingEnv = existingByFile.TryGetValue($"{env}.yaml", out var existingEnv) && existingEnv!.Length > 0;
            var hasCsvOverride = record.EnvironmentOverrides.TryGetValue(env, out var csvOverride);

            if (!hasExistingEnv && !hasCsvOverride) continue;

            var merged = hasExistingEnv
                ? MergeEnvScalars(hasCsvOverride ? csvOverride! : new EnvironmentOverride("", "", "", ""), ParseEnvYaml(existingEnv!))
                : csvOverride!;

            files[$"{env}.yaml"] = RenderEnvYaml(merged);
        }

        return files;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static string RenderAppYaml(
        MuleSoftOnboardingRecord record,
        IReadOnlyList<TransactionTypeEntry> existingTransactionTypes,
        IReadOnlyList<string> existingMessageTypes,
        IReadOnlyList<SourceDestinationMapping> existingSourceDestinations,
        IReadOnlyList<UomMapping> existingUomMappings)
    {
        var transactionTypes = record.TransactionTypes.Count > 0 || existingTransactionTypes.Count == 0
            ? record.TransactionTypes : existingTransactionTypes;
        var messageTypes = record.MessageTypes.Count > 0 || existingMessageTypes.Count == 0
            ? record.MessageTypes : existingMessageTypes;
        var sourceDestinations = record.SourceDestinationMappings.Count > 0 || existingSourceDestinations.Count == 0
            ? record.SourceDestinationMappings : existingSourceDestinations;
        var uomMappings = record.UomMappings.Count > 0 || existingUomMappings.Count == 0
            ? record.UomMappings : existingUomMappings;

        var appOverride = record.EnvironmentOverrides.GetValueOrDefault("app") ?? new EnvironmentOverride("", "", "", "");

        var sb = new StringBuilder();
        sb.Append("country_key: ").Append(record.CountryKey).Append('\n');
        sb.Append("country_code: ").Append(record.CountryCode).Append('\n');
        sb.Append("partner_comment: ").Append(record.PartnerComment).Append('\n');
        sb.Append("created_by: ").Append(record.CreatedBy).Append('\n');
        sb.Append("nav:\n");
        sb.Append("  protocol: ").Append(record.NavProtocol).Append('\n');
        sb.Append("  host: ").Append(appOverride.Host).Append('\n');
        sb.Append("  port: \"").Append(record.NavPort).Append("\"\n");
        sb.Append("  username: ").Append(record.NavUsername).Append('\n');
        sb.Append("  password: \"").Append(PasswordPlaceholder).Append("\"\n");
        sb.Append("  domain: ").Append(record.NavDomain).Append('\n');
        sb.Append("  company: ").Append(appOverride.Company).Append('\n');
        sb.Append("  service: ").Append(record.NavService).Append('\n');
        sb.Append("  soap_port: \"").Append(record.NavSoapPort).Append("\"\n");
        sb.Append("  soap_path: ").Append(appOverride.SoapPath).Append('\n');
        sb.Append("  routing_code: ").Append(appOverride.RoutingCode).Append('\n');
        sb.Append("  use_common_cert: ").Append(record.NavUseCommonCert.ToLowerInvariant()).Append('\n');

        if (transactionTypes.Count > 0)
        {
            sb.Append("  transaction_types:\n");
            foreach (var t in transactionTypes)
                sb.Append("    ").Append(t.Code).Append(": { enabled: ").Append(t.Enabled.ToLowerInvariant())
                  .Append(", type: ").Append(t.Label).Append(" }\n");
        }

        sb.Append("translation:\n");
        sb.Append("  receiver_name: ").Append(record.TranslationReceiverName).Append('\n');
        sb.Append("  message_types: [").Append(string.Join(", ", messageTypes)).Append("]\n");

        if (sourceDestinations.Count > 0)
        {
            sb.Append("  source_destination_combinations:\n");
            foreach (var m in sourceDestinations)
                sb.Append("    - value_from: ").Append(m.From).Append('\n')
                  .Append("      value_to: ").Append(m.To).Append('\n');
        }

        if (uomMappings.Count > 0)
        {
            sb.Append("  uom_mappings:\n");
            foreach (var m in uomMappings)
                sb.Append("    - value_from: ").Append(m.From).Append('\n')
                  .Append("      value_to: ").Append(m.To).Append('\n');
        }

        return sb.ToString();
    }

    private static string RenderEnvYaml(EnvironmentOverride env) =>
        new StringBuilder()
            .Append("nav:\n")
            .Append("  host: ").Append(env.Host).Append('\n')
            .Append("  company: ").Append(env.Company).Append('\n')
            .Append("  soap_path: ").Append(env.SoapPath).Append('\n')
            .Append("  routing_code: ").Append(env.RoutingCode).Append('\n')
            .ToString();

    // ── Merge helpers ─────────────────────────────────────────────────────────

    private static MuleSoftOnboardingRecord MergeAppScalars(MuleSoftOnboardingRecord csv, AppYamlData existing) => csv with
    {
        CountryKey              = Coalesce(csv.CountryKey, existing.CountryKey),
        CountryCode             = Coalesce(csv.CountryCode, existing.CountryCode),
        PartnerComment          = Coalesce(csv.PartnerComment, existing.PartnerComment),
        CreatedBy               = Coalesce(csv.CreatedBy, existing.CreatedBy),
        NavProtocol             = Coalesce(csv.NavProtocol, existing.NavProtocol),
        NavPort                 = Coalesce(csv.NavPort, existing.NavPort),
        NavUsername             = Coalesce(csv.NavUsername, existing.NavUsername),
        NavDomain               = Coalesce(csv.NavDomain, existing.NavDomain),
        NavService              = Coalesce(csv.NavService, existing.NavService),
        NavSoapPort             = Coalesce(csv.NavSoapPort, existing.NavSoapPort),
        NavUseCommonCert        = Coalesce(csv.NavUseCommonCert, existing.NavUseCommonCert),
        TranslationReceiverName = Coalesce(csv.TranslationReceiverName, existing.TranslationReceiverName),
        EnvironmentOverrides    = MergeAppEnvOverride(csv.EnvironmentOverrides, existing.AppOverride)
    };

    private static IReadOnlyDictionary<string, EnvironmentOverride> MergeAppEnvOverride(
        IReadOnlyDictionary<string, EnvironmentOverride> csvOverrides, EnvironmentOverride existingApp)
    {
        var merged = new Dictionary<string, EnvironmentOverride>(csvOverrides);
        merged["app"] = MergeEnvScalars(merged.GetValueOrDefault("app") ?? new EnvironmentOverride("", "", "", ""), existingApp);
        return merged;
    }

    private static EnvironmentOverride MergeEnvScalars(EnvironmentOverride csv, EnvironmentOverride existing) => new(
        Host:        Coalesce(csv.Host, existing.Host),
        Company:     Coalesce(csv.Company, existing.Company),
        SoapPath:    Coalesce(csv.SoapPath, existing.SoapPath),
        RoutingCode: Coalesce(csv.RoutingCode, existing.RoutingCode));

    private static string Coalesce(string csvValue, string existingValue) => csvValue.Length > 0 ? csvValue : existingValue;

    private static List<TransactionTypeEntry> UpsertTransactionTypes(
        IReadOnlyList<TransactionTypeEntry> existing, IReadOnlyList<TransactionTypeEntry> updates)
    {
        var result = existing.ToList();
        foreach (var update in updates)
        {
            var index = result.FindIndex(t => t.Code == update.Code);
            if (index >= 0) result[index] = update; else result.Add(update);
        }
        return result;
    }

    private static List<string> UpsertMessageTypes(IReadOnlyList<string> existing, IReadOnlyList<string> updates)
    {
        var result = existing.ToList();
        foreach (var update in updates)
            if (!result.Contains(update)) result.Add(update);
        return result;
    }

    private static List<SourceDestinationMapping> UpsertSourceDestinations(
        IReadOnlyList<SourceDestinationMapping> existing, IReadOnlyList<SourceDestinationMapping> updates)
    {
        var result = existing.ToList();
        foreach (var update in updates)
        {
            var index = result.FindIndex(m => m.From == update.From);
            if (index >= 0) result[index] = update; else result.Add(update);
        }
        return result;
    }

    private static List<UomMapping> UpsertUomMappings(IReadOnlyList<UomMapping> existing, IReadOnlyList<UomMapping> updates)
    {
        var result = existing.ToList();
        foreach (var update in updates)
        {
            var index = result.FindIndex(m => m.From == update.From);
            if (index >= 0) result[index] = update; else result.Add(update);
        }
        return result;
    }

    // ── Minimal existing-YAML reader (only understands this builder's own output shape) ──

    private static EnvironmentOverride ParseEnvYaml(string yaml) => new(
        Host:        ExtractScalar(yaml, "host"),
        Company:     ExtractScalar(yaml, "company"),
        SoapPath:    ExtractScalar(yaml, "soap_path"),
        RoutingCode: ExtractScalar(yaml, "routing_code"));

    private static AppYamlData ParseAppYaml(string yaml) => new(
        CountryKey:              ExtractScalar(yaml, "country_key"),
        CountryCode:             ExtractScalar(yaml, "country_code"),
        PartnerComment:          ExtractScalar(yaml, "partner_comment"),
        CreatedBy:               ExtractScalar(yaml, "created_by"),
        NavProtocol:             ExtractScalar(yaml, "protocol"),
        NavPort:                 ExtractScalar(yaml, "port"),
        NavUsername:             ExtractScalar(yaml, "username"),
        NavDomain:               ExtractScalar(yaml, "domain"),
        NavService:              ExtractScalar(yaml, "service"),
        NavSoapPort:             ExtractScalar(yaml, "soap_port"),
        NavUseCommonCert:        ExtractScalar(yaml, "use_common_cert"),
        TranslationReceiverName: ExtractScalar(yaml, "receiver_name"),
        AppOverride: new EnvironmentOverride(
            Host:        ExtractScalar(yaml, "host"),
            Company:     ExtractScalar(yaml, "company"),
            SoapPath:    ExtractScalar(yaml, "soap_path"),
            RoutingCode: ExtractScalar(yaml, "routing_code")),
        TransactionTypes:          ExtractTransactionTypes(yaml),
        MessageTypes:              ExtractMessageTypes(yaml),
        SourceDestinationMappings: ExtractPairList(yaml, "source_destination_combinations")
            .Select(p => new SourceDestinationMapping(p.From, p.To)).ToList(),
        UomMappings: ExtractPairList(yaml, "uom_mappings")
            .Select(p => new UomMapping(p.From, p.To)).ToList());

    private static string ExtractScalar(string yaml, string key)
    {
        var match = Regex.Match(yaml, $@"^\s*{Regex.Escape(key)}:\s*""?(.*?)""?\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static List<TransactionTypeEntry> ExtractTransactionTypes(string yaml)
    {
        var results = new List<TransactionTypeEntry>();
        foreach (Match m in Regex.Matches(
            yaml, @"^\s*(\w+):\s*\{\s*enabled:\s*(\w+)\s*,\s*type:\s*(\w+)\s*\}\s*$", RegexOptions.Multiline))
        {
            results.Add(new TransactionTypeEntry(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
        }
        return results;
    }

    private static List<string> ExtractMessageTypes(string yaml)
    {
        var match = Regex.Match(yaml, @"^\s*message_types:\s*\[(.*?)\]\s*$", RegexOptions.Multiline);
        if (!match.Success || match.Groups[1].Value.Trim().Length == 0) return [];
        return match.Groups[1].Value.Split(',').Select(s => s.Trim()).ToList();
    }

    private static List<(string From, string To)> ExtractPairList(string yaml, string sectionKey)
    {
        var results = new List<(string, string)>();
        var sectionMatch = Regex.Match(
            yaml, $@"^\s*{Regex.Escape(sectionKey)}:\s*\n((?:\s*-.*\n?|\s+value_(?:from|to):.*\n?)*)", RegexOptions.Multiline);
        if (!sectionMatch.Success) return results;

        foreach (Match m in Regex.Matches(
            sectionMatch.Groups[1].Value,
            @"value_from:\s*(.*?)\s*\n\s*value_to:\s*(.*?)\s*(?:\n|$)"))
        {
            results.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        }
        return results;
    }

    private sealed record AppYamlData(
        string CountryKey, string CountryCode, string PartnerComment, string CreatedBy,
        string NavProtocol, string NavPort, string NavUsername, string NavDomain, string NavService,
        string NavSoapPort, string NavUseCommonCert, string TranslationReceiverName,
        EnvironmentOverride AppOverride,
        IReadOnlyList<TransactionTypeEntry> TransactionTypes,
        IReadOnlyList<string> MessageTypes,
        IReadOnlyList<SourceDestinationMapping> SourceDestinationMappings,
        IReadOnlyList<UomMapping> UomMappings);
}
