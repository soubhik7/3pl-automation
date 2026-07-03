using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ThreePlLocalFunction.Shared;

namespace BtpAutomation.Services;

// ============================================================================
// GitHubWorkflowDispatcher — fires a GitHub Actions workflow_dispatch event
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   The BTP domain's deployment trigger is a GitHub Actions workflow (e.g.
//   "btp-Api-management-deploy.yml") in an external repo. This is the one
//   place that knows how to call GitHub's
//   POST /repos/{owner}/{repo}/actions/workflows/{file}/dispatches REST
//   endpoint — TriggerBtpDeploymentFunction only builds the `inputs` payload
//   and delegates the HTTP call here.
// HOW TO USE:
//   await new GitHubWorkflowDispatcher().DispatchAsync(
//       repoOwner, repoName, workflowFileName, branchRef, githubToken, inputs);
//   Returns a structured result dictionary (success, statusCode, message,
//   workflow, ref, inputs) — never throws for GitHub-side failures, so a bad
//   token or missing workflow file surfaces as success:false with GitHub's own
//   message rather than an unhandled exception.
// IMPORTANT NOTES:
//   githubToken is used if the caller supplies it (note: that value is then
//   visible in the Logic App's run history — an explicit, accepted trade-off
//   for callers who need a per-call override); otherwise this falls back to
//   the GITHUB_TOKEN app setting/environment variable so callers who don't
//   need an override never have to pass a PAT through run history at all.
// ============================================================================
public sealed class GitHubWorkflowDispatcher
{
    public async Task<IDictionary<string, object>> DispatchAsync(
        string repoOwner,
        string repoName,
        string workflowFileName,
        string branchRef,
        string? githubToken,
        IReadOnlyDictionary<string, string> inputs)
    {
        var workflow = $"{repoOwner}/{repoName}/actions/workflows/{workflowFileName}";

        try
        {
            var token = GitHubApiClient.ResolveToken(githubToken, "githubToken");
            var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/actions/workflows/{workflowFileName}/dispatches";
            var payload = new Dictionary<string, object> { ["ref"] = branchRef, ["inputs"] = inputs };

            using var request = GitHubApiClient.CreateRequest(HttpMethod.Post, url, token);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await GitHubApiClient.Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            // GitHub returns 204 No Content on success and a JSON {"message": "..."} on failure.
            return new Dictionary<string, object>
            {
                ["success"] = response.IsSuccessStatusCode,
                ["statusCode"] = (int)response.StatusCode,
                ["message"] = body.Length > 0 ? body : response.ReasonPhrase ?? "",
                ["workflow"] = workflow,
                ["ref"] = branchRef,
                ["inputs"] = inputs
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["statusCode"] = 0,
                ["message"] = $"Network error talking to GitHub: {ex.Message}",
                ["workflow"] = workflow,
                ["ref"] = branchRef,
                ["inputs"] = inputs
            };
        }
    }
}
