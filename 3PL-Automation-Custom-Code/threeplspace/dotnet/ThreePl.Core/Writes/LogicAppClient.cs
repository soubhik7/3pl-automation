using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace ThreePl.Core.Writes;

public sealed class SaveResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    /// <summary>enrichmentStatus from the workflow's 200 body (e.g. Complete/AwaitingInput).</summary>
    public string? EnrichmentStatus { get; init; }
    /// <summary>Raw response body — on failure this is the workflow's clean 400/500 error body.</summary>
    public string? Body { get; init; }
    public string? Error { get; init; }
}

public sealed class LaunchResult
{
    /// <summary>True when the launcher answered 202 (accepted / awaiting approval).</summary>
    public bool Accepted { get; init; }
    public int StatusCode { get; init; }
    /// <summary>202 body "status": OrchestrationStarted or AwaitingArchitectureApproval.</summary>
    public string? Status { get; init; }
    public string? Note { get; init; }
    /// <summary>409/4xx body "error" — the per-domain gate failure reason.</summary>
    public string? Error { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Typed HttpClient wrapping the existing Logic App HTTP triggers. Payloads
/// are identical to what the current HTML posts — the workflows (validation,
/// enrichment-status computation, upsert, audit, orchestration, email flows)
/// stay unchanged behind it.
/// </summary>
public class LogicAppClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly HttpClient _http;
    private readonly LogicAppOptions _options;

    public LogicAppClient(HttpClient http, IOptions<LogicAppOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>
    /// POST data-enrichment: {domain, correlationId, ...fields} where fields
    /// comes from the form's ToPayloadFields() (incl. child arrays).
    /// </summary>
    public async Task<SaveResult> SaveDomainAsync(
        string domain, string correlationId, JsonObject fields, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.DataEnrichmentUrl))
            return new SaveResult { Success = false, StatusCode = 0, Error = "Data-enrichment endpoint is not configured (LogicApps:DataEnrichmentUrl)." };

        var payload = new JsonObject
        {
            ["domain"] = domain,
            ["correlationId"] = correlationId,
        };
        foreach (var kv in fields)
            payload[kv.Key] = kv.Value?.DeepClone();

        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(payload.ToJsonString(SerializerOptions), Encoding.UTF8, "application/json");
            response = await _http.PostAsync(_options.DataEnrichmentUrl, content, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SaveResult { Success = false, StatusCode = 0, Error = ex.Message };
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // Surface the workflow's clean 400/500 error body verbatim.
            return new SaveResult
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Body = body,
                Error = $"HTTP {(int)response.StatusCode}: {body}",
            };
        }

        string? enrichmentStatus = null;
        try
        {
            enrichmentStatus = JsonNode.Parse(body)?["enrichmentStatus"]?.GetValue<string>();
        }
        catch (JsonException) { /* non-JSON 2xx body — leave status null */ }

        return new SaveResult
        {
            Success = true,
            StatusCode = (int)response.StatusCode,
            EnrichmentStatus = enrichmentStatus,
            Body = body,
        };
    }

    /// <summary>
    /// POST onboarding-launcher: {correlationId, domains, launchedBy,
    /// forceRedeploy}. 202 = accepted (OrchestrationStarted or
    /// AwaitingArchitectureApproval); 409 = gate failure with reasons.
    /// </summary>
    public async Task<LaunchResult> LaunchAsync(
        string correlationId, IReadOnlyList<string> domains, bool forceRedeploy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OnboardingLauncherUrl))
            return new LaunchResult { Accepted = false, StatusCode = 0, Error = "Onboarding-launcher endpoint is not configured (LogicApps:OnboardingLauncherUrl)." };

        var domainsArray = new JsonArray();
        foreach (var d in domains) domainsArray.Add(d);
        var payload = new JsonObject
        {
            ["correlationId"] = correlationId,
            ["domains"] = domainsArray,
            ["launchedBy"] = _options.LaunchedBy,
            ["forceRedeploy"] = forceRedeploy,
        };

        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(payload.ToJsonString(SerializerOptions), Encoding.UTF8, "application/json");
            response = await _http.PostAsync(_options.OnboardingLauncherUrl, content, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new LaunchResult { Accepted = false, StatusCode = 0, Error = ex.Message };
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        string? status = null, note = null, error = null;
        try
        {
            var node = JsonNode.Parse(body);
            status = node?["status"]?.GetValue<string>();
            note = node?["note"]?.GetValue<string>();
            error = node?["error"]?.GetValue<string>();
        }
        catch (JsonException) { /* keep raw body */ }

        return new LaunchResult
        {
            Accepted = response.StatusCode == HttpStatusCode.Accepted,
            StatusCode = (int)response.StatusCode,
            Status = status,
            Note = note,
            Error = error ?? (response.StatusCode == HttpStatusCode.Accepted ? null : $"HTTP {(int)response.StatusCode}: {body}"),
            Body = body,
        };
    }
}
