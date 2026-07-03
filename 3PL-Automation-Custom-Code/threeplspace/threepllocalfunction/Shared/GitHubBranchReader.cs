using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ThreePlLocalFunction.Shared;

// ============================================================================
// GitHubBranchReader — reads one config file's current content from a GitHub
// branch, so a generator can merge new entries onto it instead of overwriting
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GenerateMuleSoftConfig already supports merging onto existing YAML
//   (updateExisting + existingAppYaml etc.), and the new Solace merge path
//   needs the same "existing content" input — but until now every caller had
//   to paste that existing content into the request body by hand. This is
//   the read-side counterpart to GitHubBranchPublisher (which only writes):
//   it fetches one file's current content from a branch via the Contents
//   API, so the Logic Apps workflow can wire GitHub's actual current state
//   straight into the generate step.
// HOW TO USE:
//   var result = await GitHubBranchReader.FetchFileFromBranchAsync(
//       repoOwner, repoName, branch, githubToken, filePath);
//   Returns a structured result dictionary (never throws for GitHub-side
//   failures) with: success, found, content, path, repo, branch, message,
//   and (on failure) statusCode.
// IMPORTANT NOTES:
//   - Deliberately tolerant of incomplete input: if repoOwner/repoName/
//     branch/filePath is blank, this returns success:true, found:false,
//     content:"" without calling GitHub at all, rather than throwing. That
//     makes it safe to wire unconditionally into a workflow that doesn't
//     always have GitHub read access configured (e.g. a brand-new onboarding
//     with no target repo yet) — the caller doesn't need an "if repo is
//     configured" branch in the workflow definition.
//   - A 404 (file doesn't exist yet on that branch) is success:true,
//     found:false — that's the expected, common "first time onboarding"
//     case, not an error. Only a non-404 failure (bad token, bad repo, rate
//     limit, network error) is success:false.
//   - Only handles files small enough for the Contents API to inline as
//     base64 in the same response (true for every config file this project
//     produces) — doesn't fall back to the blobs API for files >1MB.
// ============================================================================
public static class GitHubBranchReader
{
    public static async Task<IDictionary<string, object>> FetchFileFromBranchAsync(
        string? repoOwner,
        string? repoName,
        string? branch,
        string? githubToken,
        string? filePath)
    {
        if (string.IsNullOrWhiteSpace(repoOwner) || string.IsNullOrWhiteSpace(repoName) ||
            string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(filePath))
        {
            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["found"] = false,
                ["content"] = "",
                ["message"] = "Skipped GitHub fetch: repoOwner, repoName, branch, and filePath must all be supplied."
            };
        }

        Guard.RequireSafeRepoPath(filePath, nameof(filePath));

        var repo = $"{repoOwner}/{repoName}";

        try
        {
            var token = GitHubApiClient.ResolveToken(githubToken, "githubToken");
            var encodedPath = string.Join('/', filePath.Split('/').Select(Uri.EscapeDataString));

            using var request = GitHubApiClient.CreateRequest(
                HttpMethod.Get, $"https://api.github.com/repos/{repo}/contents/{encodedPath}?ref={branch}", token);
            using var response = await GitHubApiClient.Http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["found"] = false,
                    ["content"] = "",
                    ["path"] = filePath,
                    ["repo"] = repo,
                    ["branch"] = branch,
                    ["message"] = $"'{filePath}' does not exist yet on {repo}@{branch}."
                };
            }

            if (!response.IsSuccessStatusCode)
                throw await GitHubApiException.FromResponseAsync(response, $"reading file '{filePath}' from branch '{branch}'");

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return FailureResult(repo, branch, filePath, (int)response.StatusCode,
                    $"'{filePath}' is a directory on {repo}@{branch}, not a file.");

            var base64Content = root.GetProperty("content").GetString() ?? "";
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(base64Content));

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["found"] = true,
                ["content"] = content,
                ["path"] = filePath,
                ["repo"] = repo,
                ["branch"] = branch,
                ["message"] = $"Fetched '{filePath}' from {repo}@{branch}."
            };
        }
        catch (GitHubApiException ex)
        {
            return FailureResult(repo, branch, filePath, (int)ex.StatusCode, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FailureResult(repo, branch, filePath, 0, $"Network error talking to GitHub: {ex.Message}");
        }
    }

    private static Dictionary<string, object> FailureResult(
        string repo, string branch, string path, int statusCode, string message) => new()
    {
        ["success"] = false,
        ["found"] = false,
        ["content"] = "",
        ["path"] = path,
        ["repo"] = repo,
        ["branch"] = branch,
        ["statusCode"] = statusCode,
        ["message"] = message
    };
}
