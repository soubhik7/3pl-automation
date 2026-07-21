using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ThreePlLocalFunction.Shared;

// ============================================================================
// GitHubBranchChecker — checks whether a branch exists on GitHub, without
// reading or writing any file content
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   The new "request a feature branch from the DevOps owner via Teams, then
//   wait for it" flow (mulesoft-config-generation / solace-config-generation)
//   needs to poll GitHub for "does this branch exist yet" repeatedly, and
//   that's a different question from GitHubBranchReader's "does this *file*
//   exist on a branch" — a 404 from the Contents API can mean either "branch
//   doesn't exist" or "branch exists but file doesn't," and those need to be
//   told apart here (no branch yet = keep waiting; branch exists but no file
//   yet = proceed, nothing to merge onto). This calls the same git ref
//   endpoint GitHubBranchPublisher already uses internally to decide whether
//   to create a branch, just exposed as its own reusable, Logic-Apps-callable
//   check.
// HOW TO USE:
//   var result = await GitHubBranchChecker.CheckBranchExistsAsync(
//       repoOwner, repoName, branch, githubToken);
//   Returns a structured result (never throws for GitHub-side failures) with:
//   success, exists, repo, branch, message, and (on failure) statusCode. See
//   CheckGitHubBranchExistsFunction (non-throwing, used for polling) and
//   EnsureGitHubBranchExistsFunction (throws if still missing, used once
//   after a wait loop to make a timeout a real workflow failure).
// IMPORTANT NOTES:
//   Blank repoOwner/repoName/branch is treated as "nothing to check" (exists
//   = false, success = true) rather than an error, matching
//   GitHubBranchReader's tolerance — callers gate the whole request/wait flow
//   on whether a featureBranchName was actually supplied, not on this
//   function refusing to run.
// ============================================================================
public static class GitHubBranchChecker
{
    public static async Task<IDictionary<string, object>> CheckBranchExistsAsync(
        string? repoOwner,
        string? repoName,
        string? branch,
        string? githubToken)
    {
        if (string.IsNullOrWhiteSpace(repoOwner) || string.IsNullOrWhiteSpace(repoName) || string.IsNullOrWhiteSpace(branch))
        {
            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["exists"] = false,
                ["message"] = "Skipped GitHub branch check: repoOwner, repoName, and branch must all be supplied."
            };
        }

        var repo = $"{repoOwner}/{repoName}";

        try
        {
            var token = GitHubApiClient.ResolveToken(githubToken, "githubToken");

            using var request = GitHubApiClient.CreateRequest(
                HttpMethod.Get, $"https://api.github.com/repos/{repo}/git/ref/heads/{branch}", token);
            using var response = await GitHubApiClient.Http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["exists"] = false,
                    ["repo"] = repo,
                    ["branch"] = branch,
                    ["message"] = $"Branch '{branch}' does not exist yet on {repo}."
                };
            }

            if (!response.IsSuccessStatusCode)
                throw await GitHubApiException.FromResponseAsync(response, $"checking whether branch '{branch}' exists");

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["exists"] = true,
                ["repo"] = repo,
                ["branch"] = branch,
                ["message"] = $"Branch '{branch}' exists on {repo}."
            };
        }
        catch (GitHubApiException ex)
        {
            return FailureResult(repo, branch, (int)ex.StatusCode, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return FailureResult(repo, branch, 0, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FailureResult(repo, branch, 0, $"Network error talking to GitHub: {ex.Message}");
        }
    }

    private static Dictionary<string, object> FailureResult(string repo, string branch, int statusCode, string message) => new()
    {
        ["success"] = false,
        ["exists"] = false,
        ["repo"] = repo,
        ["branch"] = branch,
        ["statusCode"] = statusCode,
        ["message"] = message
    };
}
