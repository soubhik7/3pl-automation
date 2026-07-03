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
// GitHubBranchPublisher — pushes generated config file(s) onto a GitHub
// feature branch (creating the branch from a base branch if it doesn't exist)
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Both the Solace and MuleSoft domains generate config content (a Solace
//   JSON blob, or MuleSoft YAML files) that needs to land as a commit on a
//   review-able feature branch rather than being emitted only as a function
//   response. This is the one place that talks to GitHub's Git/Contents REST
//   API to do that — written once, shared by both domains' publish functions,
//   so a bug fix or GitHub API change only needs fixing in one place. It has
//   zero Solace/MuleSoft-specific knowledge (no field names, no YAML/JSON
//   awareness) — it only knows "push these repo-path -> text-content pairs".
// HOW TO USE:
//   var result = await GitHubBranchPublisher.PushFilesToFeatureBranchAsync(
//       repoOwner, repoName, baseBranch, featureBranchName, githubToken,
//       commitMessage, new Dictionary<string,string> { ["path/in/repo.json"] = content });
//   Returns a structured result dictionary (never throws for GitHub-side
//   failures) with: success, repo, baseBranch, featureBranch, branchCreated,
//   filesPushed, compareUrl, message, and (on failure) statusCode.
// IMPORTANT NOTES:
//   - Idempotent by design: re-running with the same featureBranchName reuses
//     the existing branch (doesn't error or duplicate it), and re-pushing the
//     same file path updates it in place (fetches the existing blob sha first,
//     as the Contents API requires for updates) instead of failing with a
//     409 sha-mismatch.
//   - Each file is written via one Contents API PUT call (one commit per
//     file). This is deliberately simpler/more robust than orchestrating a
//     single multi-file commit through the low-level Git Data API (trees +
//     commits), at the cost of one commit per file instead of one combined
//     commit — an acceptable trade for an automation tool, not a human PR.
//   - Only GitHub's own error message/status code is ever surfaced — the PAT
//     is never included in any returned or logged value.
// ============================================================================
public static class GitHubBranchPublisher
{
    public static async Task<IDictionary<string, object>> PushFilesToFeatureBranchAsync(
        string repoOwner,
        string repoName,
        string baseBranch,
        string featureBranchName,
        string? githubToken,
        string commitMessage,
        IReadOnlyDictionary<string, string> filesByRepoPath)
    {
        Guard.RequireNotBlank(repoOwner, nameof(repoOwner));
        Guard.RequireNotBlank(repoName, nameof(repoName));
        Guard.RequireNotBlank(baseBranch, nameof(baseBranch));
        Guard.RequireNotBlank(featureBranchName, nameof(featureBranchName));
        Guard.RequireNotBlank(commitMessage, nameof(commitMessage));

        if (filesByRepoPath.Count == 0)
            throw new ArgumentException("At least one file must be supplied to push.", nameof(filesByRepoPath));

        foreach (var path in filesByRepoPath.Keys)
            Guard.RequireSafeRepoPath(path, nameof(filesByRepoPath));

        var repo = $"{repoOwner}/{repoName}";
        var filesPushed = new List<string>();

        try
        {
            var token = GitHubApiClient.ResolveToken(githubToken, "githubToken");
            var branchCreated = await EnsureFeatureBranchAsync(repo, baseBranch, featureBranchName, token);

            foreach (var (path, content) in filesByRepoPath)
            {
                await PutFileAsync(repo, path, content, featureBranchName, commitMessage, token);
                filesPushed.Add(path);
            }

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["repo"] = repo,
                ["baseBranch"] = baseBranch,
                ["featureBranch"] = featureBranchName,
                ["branchCreated"] = branchCreated,
                ["filesPushed"] = filesPushed,
                ["compareUrl"] = $"https://github.com/{repo}/compare/{baseBranch}...{featureBranchName}",
                ["message"] = $"Pushed {filesPushed.Count} file(s) to {repo}@{featureBranchName}."
            };
        }
        catch (GitHubApiException ex)
        {
            return FailureResult(repo, baseBranch, featureBranchName, filesPushed, (int)ex.StatusCode, ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return FailureResult(repo, baseBranch, featureBranchName, filesPushed, 0,
                $"Unexpected response from GitHub: {ex.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FailureResult(repo, baseBranch, featureBranchName, filesPushed, 0,
                $"Network error talking to GitHub: {ex.Message}");
        }
    }

    private static Dictionary<string, object> FailureResult(
        string repo, string baseBranch, string featureBranch, List<string> filesPushed, int statusCode, string message) => new()
    {
        ["success"] = false,
        ["repo"] = repo,
        ["baseBranch"] = baseBranch,
        ["featureBranch"] = featureBranch,
        ["filesPushed"] = filesPushed,
        ["statusCode"] = statusCode,
        ["message"] = message
    };

    // Returns true if the feature branch had to be created (didn't already exist).
    private static async Task<bool> EnsureFeatureBranchAsync(string repo, string baseBranch, string featureBranch, string token)
    {
        if (await BranchExistsAsync(repo, featureBranch, token))
            return false;

        var baseSha = await GetBranchHeadShaAsync(repo, baseBranch, token);

        using var request = GitHubApiClient.CreateRequest(
            HttpMethod.Post, $"https://api.github.com/repos/{repo}/git/refs", token);
        request.Content = JsonBody(new Dictionary<string, object>
        {
            ["ref"] = $"refs/heads/{featureBranch}",
            ["sha"] = baseSha
        });

        using var response = await GitHubApiClient.Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw await GitHubApiException.FromResponseAsync(response, $"creating branch '{featureBranch}' from '{baseBranch}'");

        return true;
    }

    private static async Task<bool> BranchExistsAsync(string repo, string branch, string token)
    {
        using var request = GitHubApiClient.CreateRequest(
            HttpMethod.Get, $"https://api.github.com/repos/{repo}/git/ref/heads/{branch}", token);
        using var response = await GitHubApiClient.Http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode)
            throw await GitHubApiException.FromResponseAsync(response, $"checking whether branch '{branch}' exists");

        return true;
    }

    private static async Task<string> GetBranchHeadShaAsync(string repo, string branch, string token)
    {
        using var request = GitHubApiClient.CreateRequest(
            HttpMethod.Get, $"https://api.github.com/repos/{repo}/git/ref/heads/{branch}", token);
        using var response = await GitHubApiClient.Http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            throw await GitHubApiException.FromResponseAsync(response, $"reading base branch '{branch}'");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("object", out var objectElement) ||
            !objectElement.TryGetProperty("sha", out var shaElement) ||
            shaElement.GetString() is not { Length: > 0 } sha)
        {
            throw new FormatException(
                $"GitHub returned an unexpected response reading branch '{branch}' (missing object.sha).");
        }

        return sha;
    }

    private static async Task PutFileAsync(
        string repo, string path, string content, string branch, string commitMessage, string token)
    {
        var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var contentsUrl = $"https://api.github.com/repos/{repo}/contents/{encodedPath}";

        // The Contents API requires the current blob sha to update an existing file;
        // omitting it (or getting it wrong) on an update fails with 409/422, so this
        // always checks first rather than assuming the file is new.
        var existingSha = await TryGetExistingFileShaAsync(repo, encodedPath, branch, token);

        var payload = new Dictionary<string, object>
        {
            ["message"] = commitMessage,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            ["branch"] = branch
        };
        if (existingSha is not null)
            payload["sha"] = existingSha;

        using var request = GitHubApiClient.CreateRequest(HttpMethod.Put, contentsUrl, token);
        request.Content = JsonBody(payload);

        using var response = await GitHubApiClient.Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw await GitHubApiException.FromResponseAsync(response, $"writing file '{path}' to branch '{branch}'");
    }

    private static async Task<string?> TryGetExistingFileShaAsync(string repo, string encodedPath, string branch, string token)
    {
        using var request = GitHubApiClient.CreateRequest(
            HttpMethod.Get, $"https://api.github.com/repos/{repo}/contents/{encodedPath}?ref={branch}", token);
        using var response = await GitHubApiClient.Http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw await GitHubApiException.FromResponseAsync(response, $"checking existing file at '{encodedPath}'");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("sha", out var shaElement))
            throw new FormatException(
                $"GitHub returned an unexpected response checking existing file '{encodedPath}' (missing sha).");

        return shaElement.GetString();
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
