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
        string existingProdYaml)
    {
        _logger.LogInformation(
            "GenerateMuleSoftConfig invoked, csvContent length={Len}, updateExisting={UpdateExisting}",
            csvContent?.Length ?? 0, updateExisting);

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

        return Task.FromResult<IDictionary<string, object>>(
            files.ToDictionary(kv => kv.Key, kv => (object)kv.Value));
    }
}
