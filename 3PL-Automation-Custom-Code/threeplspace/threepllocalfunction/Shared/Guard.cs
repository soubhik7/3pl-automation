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
}
