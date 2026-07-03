using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SolaceConfigGenerator.Services;

namespace SolaceConfigGenerator.Functions;

// ============================================================================
// MergeSolaceConfigFunction — Logic Apps entry point for merging a freshly
// generated Solace config onto the existing one read from GitHub
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GenerateSolaceConfig only ever builds from the CSV in isolation — it has
//   no notion of "existing" config, unlike GenerateMuleSoftConfig. This is
//   the second step of the new Solace update pipeline: given this run's
//   freshly generated config (from GenerateSolaceConfig) and the existing
//   config content already on GitHub (from FetchExistingConfigFile), it
//   upserts the new entries onto the existing ones so a repeat onboarding
//   for the same partner adds to their config instead of overwriting it.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName "MergeSolaceConfig",
//   chained after GenerateSolaceConfig and FetchExistingConfigFile in the
//   same workflow — pass newConfigJson as
//   @string(body('Call_a_local_function...')?['Results']?[0]?['SolaceConfig'])
//   and existingConfigJson as body('Fetch_Existing_Solace_Config')?['content'].
//   See three3pllogicapp/solace-config-generation/workflow.json.
// IMPORTANT NOTES:
//   existingConfigJson may be blank (nothing to merge against yet) — that's
//   the expected first-onboarding case, not an error; SolaceConfigMerger
//   returns newConfigJson unchanged in that case. All the actual merge logic
//   lives in SolaceConfigMerger, kept separate so it stays testable without
//   the Logic Apps custom-code host.
// ============================================================================
public class MergeSolaceConfigFunction
{
    private readonly ILogger<MergeSolaceConfigFunction> _logger = NullLogger<MergeSolaceConfigFunction>.Instance;

    [Function("MergeSolaceConfig")]
    public Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string newConfigJson,
        string existingConfigJson)
    {
        _logger.LogInformation(
            "MergeSolaceConfig invoked, newConfigJson length={NewLen}, existingConfigJson length={ExistingLen}",
            newConfigJson?.Length ?? 0, existingConfigJson?.Length ?? 0);

        var merged = SolaceConfigMerger.Merge(existingConfigJson, newConfigJson ?? "");

        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
        {
            ["mergedConfigJson"] = merged
        });
    }
}
