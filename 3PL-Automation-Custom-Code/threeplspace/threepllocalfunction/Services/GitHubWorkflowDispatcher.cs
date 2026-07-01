using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BtpAutomation.Services;

/// <summary>
/// Triggers a GitHub Actions workflow_dispatch event via the REST API
/// (POST /repos/{owner}/{repo}/actions/workflows/{workflow_file}/dispatches).
///
/// The PAT is taken from the caller-supplied githubToken when given; otherwise it
/// falls back to the GITHUB_TOKEN app setting/environment variable. Note that a
/// PAT passed as githubToken is visible in the Logic App's run history — the
/// env-var fallback exists so callers can avoid that when they don't need to
/// override it per call.
/// </summary>
public sealed class GitHubWorkflowDispatcher
{
    private const string GitHubTokenEnvVar = "GITHUB_TOKEN";

    // Reused across invocations per .NET HttpClient guidance (avoids socket exhaustion).
    private static readonly HttpClient Http = new();

    public async Task<IDictionary<string, object>> DispatchAsync(
        string repoOwner,
        string repoName,
        string workflowFileName,
        string branchRef,
        string githubToken,
        IReadOnlyDictionary<string, string> inputs)
    {
        var token = string.IsNullOrWhiteSpace(githubToken)
            ? Environment.GetEnvironmentVariable(GitHubTokenEnvVar)
            : githubToken;

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"No GitHub PAT supplied: pass githubToken or configure the '{GitHubTokenEnvVar}' app setting.");

        var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/actions/workflows/{workflowFileName}/dispatches";
        var payload = new Dictionary<string, object> { ["ref"] = branchRef, ["inputs"] = inputs };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("3PL-Automation-BTP-Function", "1.0"));

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // GitHub returns 204 No Content on success and a JSON {"message": "..."} on failure.
        return new Dictionary<string, object>
        {
            ["success"] = response.IsSuccessStatusCode,
            ["statusCode"] = (int)response.StatusCode,
            ["message"] = body.Length > 0 ? body : response.ReasonPhrase ?? "",
            ["workflow"] = $"{repoOwner}/{repoName}/actions/workflows/{workflowFileName}",
            ["ref"] = branchRef,
            ["inputs"] = inputs
        };
    }
}
