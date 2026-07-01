using System.Collections.Generic;

namespace SolaceConfigGenerator.Models;

// ============================================================================
// GenerateSolaceConfigResponse / SolaceConfigResult — Solace function output shape
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GenerateSolaceConfigFunction can process multiple onboarding records in
//   one CSV (grouped by Brand/Env/SystemName/ThreePLCode). This is the
//   per-record result envelope: one entry per record, each independently
//   either a successful SolaceConfig or an Error message — so one malformed
//   record in a batch doesn't fail the whole call.
// HOW TO USE:
//   Returned directly from GenerateSolaceConfigFunction.Run(); a caller
//   should check each result's Error field before trusting SolaceConfig.
// IMPORTANT NOTES:
//   No [JsonPropertyName]/[JsonIgnore] attributes here on purpose — the
//   custom-code host's serializer ignores them entirely and reflects over
//   raw property names, so SolaceConfig itself is built as a plain
//   Dictionary<string,object> by SolaceConfigBuilder rather than a further
//   typed POCO (dictionary keys are guaranteed to become the exact JSON
//   keys; a typed class here would silently emit wrong casing and nulls).
// ============================================================================
public sealed class GenerateSolaceConfigResponse
{
    public List<SolaceConfigResult> Results { get; init; } = [];
}

public sealed class SolaceConfigResult
{
    public required string NamingPrefix { get; init; }

    // Built by SolaceConfigBuilder as a plain dictionary — see that class for why.
    public IDictionary<string, object>? SolaceConfig { get; init; }

    public string? Error { get; init; }
}
