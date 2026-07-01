using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ThreePlLocalFunction.Shared;

// ============================================================================
// GitHubApiClient — shared HttpClient + auth/header setup for GitHub REST calls
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Both GitHubWorkflowDispatcher (BTP domain) and GitHubBranchPublisher
//   (Solace + MuleSoft domains) talk to the GitHub REST API and need the same
//   headers (Bearer auth, Accept, API version, User-Agent — GitHub rejects
//   requests with no User-Agent, and HttpClient doesn't send one by default).
//   Centralizing this avoids three copies of the same header-setup code
//   silently drifting (e.g. one forgetting the API-version header).
// HOW TO USE:
//   using var request = GitHubApiClient.CreateRequest(HttpMethod.Get, url, token);
//   using var response = await GitHubApiClient.Http.SendAsync(request);
//   Token resolution: var token = GitHubApiClient.ResolveToken(callerSuppliedToken);
//   — uses the caller-supplied value if non-blank, else falls back to the
//   GITHUB_TOKEN app setting/environment variable, else throws.
// IMPORTANT NOTES:
//   Http is a single static instance reused across every call in the process
//   (per .NET guidance — a fresh HttpClient per call risks socket exhaustion).
//   A 30s timeout is set so a hung GitHub request fails the Logic Apps action
//   in a bounded time instead of hanging indefinitely.
// ============================================================================
public static class GitHubApiClient
{
    private const string ApiVersion = "2022-11-28";
    private const string DefaultTokenEnvVar = "GITHUB_TOKEN";

    public static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("3PL-Automation-Function", "1.0"));
        return request;
    }

    // Caller-supplied token wins when present; otherwise falls back to the
    // GITHUB_TOKEN app setting so callers that don't need a per-call override
    // don't have to pass a PAT through Logic App run history at all.
    public static string ResolveToken(string? callerSuppliedToken, string envVarName = DefaultTokenEnvVar)
    {
        var token = string.IsNullOrWhiteSpace(callerSuppliedToken)
            ? Environment.GetEnvironmentVariable(envVarName)
            : callerSuppliedToken;

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"No GitHub PAT supplied: pass githubToken or configure the '{envVarName}' app setting.");

        return token;
    }
}
