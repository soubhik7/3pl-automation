using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ThreePlLocalFunction.Shared;

// ============================================================================
// GitHubApiException — carries a GitHub REST API failure's status code + detail
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GitHubBranchPublisher makes several sequential GitHub API calls (check
//   branch, read base branch sha, create branch, check file, write file). Any
//   one of them can fail for an operator-actionable reason (missing repo,
//   missing branch, bad token, path already a directory, etc.) and the caller
//   needs the real GitHub status code + message, not just "request failed".
// HOW TO USE:
//   throw await GitHubApiException.FromResponseAsync(response, "creating branch 'x'");
//   after checking !response.IsSuccessStatusCode. Catch it at the top-level
//   orchestration method and turn it into a structured success:false result —
//   never let it escape as an unhandled exception, since the caller (a Logic
//   Apps action) can't act on a raw stack trace.
// IMPORTANT NOTES:
//   Never includes the request body/headers (which could contain the PAT) —
//   only the response body's own "message" field or reason phrase.
// ============================================================================
public sealed class GitHubApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    private GitHubApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static async Task<GitHubApiException> FromResponseAsync(HttpResponseMessage response, string action)
    {
        var body = await response.Content.ReadAsStringAsync();
        var detail = ExtractMessage(body) ?? response.ReasonPhrase ?? "no further details";
        return new GitHubApiException(response.StatusCode, $"GitHub API error while {action}: {(int)response.StatusCode} {detail}");
    }

    private static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
