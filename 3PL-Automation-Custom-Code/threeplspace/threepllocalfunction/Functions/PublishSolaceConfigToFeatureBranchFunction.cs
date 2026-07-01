using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SolaceConfigGenerator.Services;

namespace SolaceConfigGenerator.Functions;

// ============================================================================
// PublishSolaceConfigToFeatureBranchFunction — commits a generated Solace
// onboarding config onto a GitHub feature branch
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GenerateSolaceConfig only returns the onboarding JSON in the workflow run
//   — it doesn't persist anywhere. This function is the second step in the
//   Solace pipeline: given that JSON plus a target repo/branch/path, it
//   commits the file to a feature branch (creating the branch from a base
//   branch if it doesn't already exist), so the change is reviewable in
//   GitHub rather than only visible in Logic Apps run history.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName
//   "PublishSolaceConfigToFeatureBranch", chained after "GenerateSolaceConfig"
//   in the same workflow — pass solaceConfigJson as
//   @string(body('Call_a_local_function...')?['results'][0]?['solaceConfig']).
//   See three3pllogicapp/publish-solace-config/workflow.json for a
//   standalone test harness with safe coalesce() defaults.
// IMPORTANT NOTES:
//   Nothing here is hardcoded: repoOwner/repoName/baseBranch/
//   featureBranchName/filePath/commitMessage are all parameters, so this can
//   target any repo/branch/path without a code change. githubToken is
//   optional (falls back to the GITHUB_TOKEN app setting — see
//   GitHubApiClient.ResolveToken). Lives entirely in the SolaceConfigGenerator
//   namespace — no shared state with BtpAutomation or MuleSoftAutomation,
//   only the stateless GitHubBranchPublisher/Guard helpers.
// ============================================================================
public class PublishSolaceConfigToFeatureBranchFunction
{
    private static readonly SolaceGitHubPublisher Publisher = new();

    private readonly ILogger<PublishSolaceConfigToFeatureBranchFunction> _logger =
        NullLogger<PublishSolaceConfigToFeatureBranchFunction>.Instance;

    [Function("PublishSolaceConfigToFeatureBranch")]
    public Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string repoOwner,
        string repoName,
        string baseBranch,
        string featureBranchName,
        string filePath,
        string solaceConfigJson,
        string commitMessage,
        string githubToken)
    {
        _logger.LogInformation(
            "PublishSolaceConfigToFeatureBranch invoked for {Repo}@{Branch} -> {Path}",
            $"{repoOwner}/{repoName}", featureBranchName, filePath);

        return Publisher.PublishAsync(
            repoOwner, repoName, baseBranch, featureBranchName, filePath, solaceConfigJson, commitMessage, githubToken);
    }
}
