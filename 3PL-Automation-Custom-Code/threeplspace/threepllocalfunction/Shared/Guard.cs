using System;

namespace ThreePlLocalFunction.Shared;

// ============================================================================
// Guard — shared input-validation helper
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Every function across the Solace, BTP and MuleSoft domains needs to reject
//   blank required parameters the same way (fail fast with a clear message,
//   instead of a null-reference exception three calls deeper). Before this
//   existed, TriggerBtpDeploymentFunction had its own private copy of this
//   check; keeping one copy here means all three domains validate identically
//   and a future domain doesn't need to reinvent it.
// HOW TO USE:
//   Guard.RequireNotBlank(value, nameof(value)); at the top of any function or
//   service method, for every required string parameter.
// IMPORTANT NOTES:
//   Stateless and domain-agnostic (no Solace/BTP/MuleSoft knowledge) — safe to
//   share across all three without creating coupling between them.
// ============================================================================
public static class Guard
{
    public static void RequireNotBlank(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} must not be empty.");
    }

    /// <summary>
    /// Rejects repo-relative paths that could escape the intended directory (e.g. a
    /// "../../.github/workflows/x.yml" prefix reaching outside the configs folder) once
    /// handed to GitHub's Contents API. Requires a relative path using '/' separators,
    /// with no empty, '.', or '..' segments.
    /// </summary>
    public static void RequireSafeRepoPath(string? value, string paramName)
    {
        RequireNotBlank(value, paramName);

        if (value!.StartsWith('/') || value.Contains('\\'))
            throw new ArgumentException($"{paramName} must be a relative repo path using '/' separators.");

        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
                throw new ArgumentException($"{paramName} must not contain empty, '.', or '..' path segments.");
        }
    }
}
