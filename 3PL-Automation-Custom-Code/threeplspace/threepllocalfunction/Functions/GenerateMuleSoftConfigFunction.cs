using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuleSoftAutomation.Services;

namespace MuleSoftAutomation.Functions;

// ============================================================================
// GenerateMuleSoftConfigFunction — Logic Apps entry point for the MuleSoft domain
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Turns a MuleSoft NAV requirements CSV into app/dev/tst/prod.yaml content,
//   from a Logic Apps "Call a local function" action — this is the first
//   step of the MuleSoft pipeline (the second is
//   PublishMuleSoftConfigToFeatureBranch, which commits the result to
//   GitHub). All field values are CSV-driven via MuleSoftCsvParser +
//   MuleSoftYamlBuilder — nothing about a specific country/partner is
//   hardcoded here.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName
//   "GenerateMuleSoftConfig", csvContent (the whole CSV as one string), and
//   updateExisting (bool) — if true, also pass existingAppYaml/
//   existingDevYaml/existingTstYaml/existingProdYaml (the caller's current
//   file contents, blank if that file doesn't exist yet) so changes merge
//   incrementally instead of overwriting. See
//   three3pllogicapp/mulesoft-config-generation/workflow.json.
// IMPORTANT NOTES:
//   A malformed CSV throws ArgumentException/FormatException and fails the
//   action outright — there's exactly one record per CSV here (unlike
//   Solace's multi-record batching), so there's no "one bad record among
//   many good ones" case to isolate.
// ============================================================================
/// <summary>
/// Generates or incrementally patches the 4 MuleSoft NAV onboarding config files
/// (app.yaml, dev.yaml, tst.yaml, prod.yaml) from a requirements CSV.
///
/// updateExisting picks the mode: false builds all 4 files fresh from the CSV alone;
/// true merges the CSV onto the existingAppYaml/existingDevYaml/existingTstYaml/
/// existingProdYaml passed in — non-blank CSV scalars override, blank scalars keep
/// the existing value, and list items (transaction types, message types, mappings)
/// are upserted onto the existing list in incremental order rather than replacing it.
/// </summary>
public class GenerateMuleSoftConfigFunction
{
    private static readonly MuleSoftCsvParser CsvParser = new();
    private static readonly MuleSoftYamlBuilder YamlBuilder = new();

    // The custom-code host's DI container doesn't register ILogger<T>/ILoggerFactory,
    // so this can't take either as a constructor dependency without failing to activate.
    private readonly ILogger<GenerateMuleSoftConfigFunction> _logger =
        NullLogger<GenerateMuleSoftConfigFunction>.Instance;

    [Function("GenerateMuleSoftConfig")]
    public Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string csvContent,
        bool updateExisting,
        string existingAppYaml,
        string existingDevYaml,
        string existingTstYaml,
        string existingProdYaml,
        string correlationId)
    {
        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] GenerateMuleSoftConfig invoked, csvContent length={Len}, updateExisting={UpdateExisting}",
                correlationId, csvContent?.Length ?? 0, updateExisting);

            if (string.IsNullOrWhiteSpace(csvContent))
                throw new ArgumentException("csvContent must not be empty.");

            var record = CsvParser.Parse(csvContent);

            var files = updateExisting
                ? YamlBuilder.MergeIntoExisting(record, new Dictionary<string, string>
                  {
                      ["app.yaml"] = existingAppYaml ?? "",
                      ["dev.yaml"] = existingDevYaml ?? "",
                      ["tst.yaml"] = existingTstYaml ?? "",
                      ["prod.yaml"] = existingProdYaml ?? ""
                  })
                : YamlBuilder.BuildNew(record);

            var result = files.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            result["correlationId"] = correlationId;
            return Task.FromResult<IDictionary<string, object>>(result);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{correlationId}] GenerateMuleSoftConfig failed: {ex.Message}", ex);
        }
    }
}
