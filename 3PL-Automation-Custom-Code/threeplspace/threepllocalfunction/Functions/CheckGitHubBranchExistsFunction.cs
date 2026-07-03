using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ThreePlLocalFunction.Shared;

namespace GitHubConfigAutomation.Functions;

// ============================================================================
// CheckGitHubBranchExistsFunction — Logic Apps entry point for polling
// whether a branch exists yet, shared by the MuleSoft and Solace domains
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   The "request a feature branch via Teams, then wait for it" flow needs to
//   poll this repeatedly inside an Until loop without the loop itself
//   failing while the branch legitimately doesn't exist yet — so this never
//   throws for "not found," only for a real GitHub API problem (bad token,
//   bad repo, network error), matching FetchExistingConfigFile's contract.
//   See EnsureGitHubBranchExistsFunction for the throwing counterpart used
//   once after the wait loop ends.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName
//   "CheckGitHubBranchExists" and repoOwner/repoName/branch/githubToken. See
//   three3pllogicapp/mulesoft-config-generation/workflow.json and
//   three3pllogicapp/solace-config-generation/workflow.json.
// ============================================================================
public class CheckGitHubBranchExistsFunction
{
    private readonly ILogger<CheckGitHubBranchExistsFunction> _logger =
        NullLogger<CheckGitHubBranchExistsFunction>.Instance;

    [Function("CheckGitHubBranchExists")]
    public Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string repoOwner,
        string repoName,
        string branch,
        string githubToken)
    {
        _logger.LogInformation(
            "CheckGitHubBranchExists invoked for {Repo}@{Branch}", $"{repoOwner}/{repoName}", branch);

        return GitHubBranchChecker.CheckBranchExistsAsync(repoOwner, repoName, branch, githubToken);
    }
}
