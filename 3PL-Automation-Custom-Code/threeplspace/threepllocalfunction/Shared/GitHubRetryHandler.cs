using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ThreePlLocalFunction.Shared;

// ============================================================================
// GitHubRetryHandler — retries idempotent (GET) GitHub API calls on transient
// failures, so a momentary network blip or rate limit doesn't fail an entire
// Logic Apps action outright
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GitHub occasionally returns 502/503/504 during brief incidents, or 429
//   when the API's secondary rate limit is hit — both are typically resolved
//   by waiting a moment and retrying the exact same request. Without this,
//   every Fetch/Check/Ensure call (including the ones polled every 30s by the
//   branch-request wait loop) would surface a hard failure for what's usually
//   a few-second blip.
// HOW TO USE:
//   Wired into GitHubApiClient.Http's handler pipeline (see GitHubApiClient) —
//   no call-site changes needed; every GET request made through that
//   HttpClient gets this automatically.
// IMPORTANT NOTES:
//   Deliberately GET-only. POST (branch creation) and PUT (file writes) are
//   NOT retried here: if such a request actually succeeded server-side but
//   its response was lost to a network blip, blindly retrying at the
//   transport layer could surface a spurious "already exists" or sha-conflict
//   error. Those operations already have their own idempotency built in one
//   layer up (EnsureFeatureBranchAsync checks existence before creating;
//   re-running the whole publish workflow is safe) — that's the correct place
//   to retry writes, not here.
//   A HttpRequestMessage can only be sent once, so each retry attempt clones
//   the original request (method, URI, headers — GET has no body to clone).
//   The overall HttpClient.Timeout (30s, set in GitHubApiClient) wraps every
//   attempt plus backoff combined, not each attempt individually.
// ============================================================================
public sealed class GitHubRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan[] BackoffDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)];

    public GitHubRetryHandler() : base(new HttpClientHandler())
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
            return await base.SendAsync(request, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            var attemptRequest = attempt == 1 ? request : CloneGetRequest(request);
            HttpResponseMessage? response = null;

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);
                if (!IsTransientFailure(response.StatusCode) || attempt >= MaxAttempts)
                    return response;

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                // Network-level failure (DNS, connection reset, etc.) — fall through to retry.
            }

            await Task.Delay(BackoffDelays[attempt - 1], cancellationToken);
        }
    }

    private static HttpRequestMessage CloneGetRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    private static bool IsTransientFailure(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;
}
