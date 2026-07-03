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
// EnsureGitHubBranchExistsFunction — Logic Apps entry point that turns "the
// requested branch still doesn't exist" into a real workflow failure
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   CheckGitHubBranchExists is deliberately non-throwing so it's safe to
//   poll inside an Until loop. But once that wait loop ends (either the
//   branch showed up, or the bounded wait timed out), the workflow needs a
//   hard gate: if the branch is still missing, this action's own failure is
//   what makes the containing Scope-Try fail and Scope-Catch produce a real
//   error response to the caller — instead of silently falling through to
//   GenerateMuleSoftConfig/GenerateSolaceConfig against a branch that was
//   never created.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName
//   "EnsureGitHubBranchExists", placed immediately after the Until loop that
//   waits for the DevOps owner to create the branch. See
//   three3pllogicapp/mulesoft-config-generation/workflow.json and
//   three3pllogicapp/solace-config-generation/workflow.json.
// IMPORTANT NOTES:
//   Reuses GitHubBranchChecker (the exact same check CheckGitHubBranchExists
//   wraps) — this is the one place in that shared logic's call chain that
//   converts its result into an exception rather than returning it.
// ============================================================================
public class EnsureGitHubBranchExistsFunction
{
    private readonly ILogger<EnsureGitHubBranchExistsFunction> _logger =
        NullLogger<EnsureGitHubBranchExistsFunction>.Instance;

    [Function("EnsureGitHubBranchExists")]
    public async Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string repoOwner,
        string repoName,
        string branch,
        string correlationId)
    {
        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] EnsureGitHubBranchExists invoked for {Repo}@{Branch}",
                correlationId, $"{repoOwner}/{repoName}", branch);

            var result = await GitHubBranchChecker.CheckBranchExistsAsync(repoOwner, repoName, branch, githubToken: null);

            if (result.TryGetValue("success", out var success) && success is false)
            {
                result.TryGetValue("message", out var failureMessage);
                throw new InvalidOperationException($"[{correlationId}] Could not verify branch '{branch}': {failureMessage}");
            }

            if (result.TryGetValue("exists", out var exists) && exists is false)
                throw new InvalidOperationException(
                    $"[{correlationId}] Branch '{branch}' still does not exist on {repoOwner}/{repoName} after waiting for the DevOps owner to create it.");

            result["correlationId"] = correlationId;
            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{correlationId}] EnsureGitHubBranchExists failed: {ex.Message}", ex);
        }
    }
}
