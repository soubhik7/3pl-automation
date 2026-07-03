using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SolaceConfigGenerator.Services;

// ============================================================================
// SolaceConfigMerger — upserts a freshly generated Solace config's entries
// onto an existing Solace onboarding config, instead of overwriting it
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Until now, GenerateSolaceConfig always produced a config from the CSV
//   alone — publishing it straight to GitHub meant a second onboarding run
//   for the same partner (e.g. adding one more MessageType/queue) would
//   overwrite everything that partner already had configured. This is the
//   Solace-domain counterpart to MuleSoftYamlBuilder.MergeIntoExisting: given
//   the existing config JSON read from GitHub (via GitHubBranchReader) and
//   the newly generated config for this run, it unions them per section
//   rather than replacing wholesale.
// HOW TO USE:
//   SolaceConfigMerger.Merge(existingConfigJson, newConfigJson) — both are
//   JSON strings; existingConfigJson may be blank (nothing to merge against
//   yet, e.g. first-time onboarding), in which case newConfigJson is
//   returned unchanged. See MergeSolaceConfigFunction and
//   three3pllogicapp/solace-config-generation/workflow.json.
// IMPORTANT NOTES:
//   Every section SolaceConfigBuilder can emit (clientProfile, ACL,
//   ClientUser, Queue, Subscription) has its own "list of entries" key
//   ("create" for most, "create_ACL" for ACL — matching SolaceConfigBuilder's
//   exact field names) and identity field used to decide "update this
//   existing entry" vs "append a new one" (e.g. Queue entries are matched by
//   queueName). Subscription is the one section merged more finely still:
//   a matching queueName's SUBSCRIPTION_LIST is unioned (deduped) rather than
//   replaced outright, so re-onboarding a queue that already has topics
//   doesn't silently drop the ones from a prior run.
//   Sections present in the existing config but absent from the new config
//   are left completely untouched — this only ever adds/updates, never
//   removes, an entry.
// ============================================================================
public static class SolaceConfigMerger
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private sealed record SectionSpec(string ListKey, string IdentityField, bool UnionSubscriptionList);

    // Matches the exact section/list-key names SolaceConfigBuilder emits — see that
    // class's BuildClientProfile/BuildAcl/BuildClientUser/BuildQueues/BuildSubscriptions.
    private static readonly IReadOnlyDictionary<string, SectionSpec> Sections = new Dictionary<string, SectionSpec>
    {
        ["clientProfile"] = new("create", "name", UnionSubscriptionList: false),
        ["ACL"] = new("create_ACL", "aclProfileName", UnionSubscriptionList: false),
        ["ClientUser"] = new("create", "clientUsername", UnionSubscriptionList: false),
        ["Queue"] = new("create", "queueName", UnionSubscriptionList: false),
        ["Subscription"] = new("create", "queueName", UnionSubscriptionList: true)
    };

    public static string Merge(string? existingConfigJson, string newConfigJson)
    {
        if (string.IsNullOrWhiteSpace(newConfigJson))
            throw new ArgumentException("newConfigJson must not be empty.");

        // Nothing to merge against (e.g. first-time onboarding) — return the new
        // config as-is rather than round-tripping it through JsonNode, which would
        // otherwise reorder/reformat it for no reason and create a noisy diff.
        if (string.IsNullOrWhiteSpace(existingConfigJson))
            return newConfigJson;

        var incomingRoot = ParseAsObject(newConfigJson, nameof(newConfigJson));
        var merged = ParseAsObject(existingConfigJson, nameof(existingConfigJson)).DeepClone().AsObject();

        foreach (var (sectionKey, sectionNode) in incomingRoot)
        {
            if (sectionNode is not JsonObject incomingSection) continue;
            if (!Sections.TryGetValue(sectionKey, out var spec))
                throw new FormatException($"Unrecognized Solace config section '{sectionKey}'.");

            var incomingList = incomingSection[spec.ListKey] as JsonArray ?? [];

            var mergedSection = merged[sectionKey] as JsonObject;
            var sectionAlreadyPresent = mergedSection is not null;
            mergedSection ??= new JsonObject();

            var existingList = mergedSection[spec.ListKey] as JsonArray ?? [];
            mergedSection[spec.ListKey] = UpsertItems(existingList, incomingList, spec.IdentityField, spec.UnionSubscriptionList);

            if (!sectionAlreadyPresent)
                merged[sectionKey] = mergedSection;
        }

        return merged.ToJsonString(WriteOptions);
    }

    // Wraps JsonNode.Parse so both a syntax error (invalid JSON) and a structurally
    // wrong shape (valid JSON that isn't an object) surface as the same clear,
    // parameter-named FormatException instead of a raw JsonException with no context.
    private static JsonObject ParseAsObject(string json, string paramName)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{paramName} is not valid JSON: {ex.Message}", ex);
        }

        return node as JsonObject ?? throw new FormatException($"{paramName} is not a valid JSON object.");
    }

    private static JsonArray UpsertItems(
        JsonArray existingList, JsonArray incomingList, string identityField, bool unionSubscriptionList)
    {
        var result = new List<JsonObject>();
        foreach (var item in existingList)
            if (item is JsonObject obj) result.Add(obj.DeepClone().AsObject());

        foreach (var incomingItem in incomingList)
        {
            if (incomingItem is not JsonObject incomingObj) continue;

            var identityValue = incomingObj[identityField]?.GetValue<string>();
            var matchIndex = identityValue is null
                ? -1
                : result.FindIndex(o => o[identityField]?.GetValue<string>() == identityValue);

            if (matchIndex < 0)
            {
                result.Add(incomingObj.DeepClone().AsObject());
                continue;
            }

            result[matchIndex] = unionSubscriptionList
                ? MergeSubscriptionTopics(result[matchIndex], incomingObj)
                : incomingObj.DeepClone().AsObject();
        }

        var merged = new JsonArray();
        foreach (var item in result) merged.Add(item.DeepClone());
        return merged;
    }

    // Unions (deduped) an existing Subscription entry's topics with the incoming
    // entry's topics for the same queueName, instead of replacing the list — so
    // re-onboarding one more topic on an already-subscribed queue keeps the old ones.
    private static JsonObject MergeSubscriptionTopics(JsonObject existingItem, JsonObject incomingItem)
    {
        var existingTopics = (existingItem["SUBSCRIPTION_LIST"] as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? "").ToList() ?? [];
        var incomingTopics = (incomingItem["SUBSCRIPTION_LIST"] as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? "").ToList() ?? [];

        var union = new List<string>(existingTopics);
        foreach (var topic in incomingTopics)
            if (!union.Contains(topic)) union.Add(topic);

        var merged = incomingItem.DeepClone().AsObject();
        var topicArray = new JsonArray();
        foreach (var topic in union) topicArray.Add(JsonValue.Create(topic));
        merged["SUBSCRIPTION_LIST"] = topicArray;

        return merged;
    }
}
