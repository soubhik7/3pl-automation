using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuleSoftAutomation.Services;

namespace MuleSoftAutomation.Functions;

// ============================================================================
// PublishMuleSoftConfigToFeatureBranchFunction — commits generated MuleSoft
// NAV config YAML files onto a GitHub feature branch
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GenerateMuleSoftConfig only returns app/dev/tst/prod.yaml content in the
//   workflow run — it doesn't persist anywhere. This function is the second
//   step in the MuleSoft pipeline: given that content plus a target repo/
//   branch/path prefix, it commits each non-blank file to a feature branch
//   (creating the branch from a base branch if it doesn't already exist), so
//   the change is reviewable in GitHub instead of only visible in Logic Apps
//   run history.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName
//   "PublishMuleSoftConfigToFeatureBranch", chained after
//   "GenerateMuleSoftConfig" in the same workflow — pass appYaml/devYaml/
//   tstYaml/prodYaml as body('Call_a_local_function...')?['app.yaml'] etc.
//   (blank/missing keys from that response mean "not generated this run" —
//   pass "" through for those). See
//   three3pllogicapp/publish-mulesoft-config/workflow.json for a standalone
//   test harness with safe coalesce() defaults.
// IMPORTANT NOTES:
//   Nothing here is hardcoded: repoOwner/repoName/baseBranch/
//   featureBranchName/filePathPrefix/commitMessage are all parameters. No
//   githubToken parameter — the GitHub PAT is resolved internally from the
//   "githubToken" app setting (see GitHubBranchPublisher /
//   GitHubApiClient.ResolveToken) rather than being passed through as a
//   Logic Apps action input, so it never appears in workflow run history.
//   Lives entirely in the MuleSoftAutomation namespace — no shared state
//   with BtpAutomation or SolaceConfigGenerator, only the stateless
//   GitHubBranchPublisher/Guard helpers.
// ============================================================================
public class PublishMuleSoftConfigToFeatureBranchFunction
{
    private static readonly MuleSoftGitHubPublisher Publisher = new();

    private readonly ILogger<PublishMuleSoftConfigToFeatureBranchFunction> _logger =
        NullLogger<PublishMuleSoftConfigToFeatureBranchFunction>.Instance;

    [Function("PublishMuleSoftConfigToFeatureBranch")]
    public Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string repoOwner,
        string repoName,
        string baseBranch,
        string featureBranchName,
        string filePathPrefix,
        string appYaml,
        string devYaml,
        string tstYaml,
        string prodYaml,
        string commitMessage)
    {
        _logger.LogInformation(
            "PublishMuleSoftConfigToFeatureBranch invoked for {Repo}@{Branch} -> {Prefix}",
            $"{repoOwner}/{repoName}", featureBranchName, filePathPrefix);

        return Publisher.PublishAsync(
            repoOwner, repoName, baseBranch, featureBranchName, filePathPrefix,
            appYaml, devYaml, tstYaml, prodYaml, commitMessage, githubToken: null);
    }
}
