using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ThreePlLocalFunction.Shared;

namespace GitHubConfigAutomation.Functions;

// ============================================================================
// FetchExistingConfigFileFunction — Logic Apps entry point for reading one
// existing config file from a GitHub branch, shared by the MuleSoft and
// Solace domains
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Both the MuleSoft and Solace generate steps can now merge onto an
//   existing config instead of only building fresh — but that only helps if
//   "existing" comes from the real file on GitHub instead of the caller
//   pasting it in by hand. This function is that read step: given a repo/
//   branch/path, it returns the file's current content (or found:false if it
//   doesn't exist yet). It has zero MuleSoft/Solace-specific knowledge (no
//   YAML or JSON awareness) — same "domain-agnostic, shared by both" role on
//   the read side that GitHubBranchPublisher plays on the write side.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName
//   "FetchExistingConfigFile" and repoOwner/repoName/branch/filePath. Chain
//   its "content" output into GenerateMuleSoftConfig's existingAppYaml (etc.)
//   or into MergeSolaceConfig's existingConfigJson. See
//   three3pllogicapp/mulesoft-config-generation/workflow.json and
//   three3pllogicapp/solace-config-generation/workflow.json.
// IMPORTANT NOTES:
//   Safe to call even when the caller hasn't wired up GitHub read access for
//   a given run — GitHubBranchReader treats blank repoOwner/repoName/branch/
//   filePath as "nothing to fetch" and returns found:false rather than
//   throwing, so this action never needs to be wrapped in a conditional.
// ============================================================================
public class FetchExistingConfigFileFunction
{
    private readonly ILogger<FetchExistingConfigFileFunction> _logger =
        NullLogger<FetchExistingConfigFileFunction>.Instance;

    [Function("FetchExistingConfigFile")]
    public async Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string repoOwner,
        string repoName,
        string branch,
        string filePath,
        string correlationId)
    {
        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] FetchExistingConfigFile invoked for {Repo}@{Branch} -> {Path}",
                correlationId, $"{repoOwner}/{repoName}", branch, filePath);

            var result = await GitHubBranchReader.FetchFileFromBranchAsync(repoOwner, repoName, branch, githubToken: null, filePath);
            result["correlationId"] = correlationId;
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{correlationId}] FetchExistingConfigFile failed: {ex.Message}", ex);
        }
    }
}
