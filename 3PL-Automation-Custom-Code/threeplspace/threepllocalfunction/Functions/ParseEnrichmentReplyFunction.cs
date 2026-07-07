using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using ThreePlLocalFunction.Shared;

namespace EnrichmentAutomation.Functions;

public class ParseEnrichmentReplyRequest
{
    public string? Domain { get; set; }        // "Btp" | "Solace" | "MuleSoft"
    public string? Body { get; set; }           // raw triggerBody()?['Body'] from the mail trigger
    public string? CorrelationId { get; set; }  // logging only
}

// ============================================================================
// ParseEnrichmentReplyFunction — turns an SME's plain-text reply into the same
// lowerCamelCase field dictionary data-enrichment's API already accepts
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Logic Apps' 2016-06-01 expression language has no regex/match function, so
//   scanning a multi-line reply body for "field: value" lines robustly belongs
//   in C#, not nested split()/indexOf() chains — the same reasoning that put
//   CSV building in C# rather than workflow expressions.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName "ParseEnrichmentReply",
//   called from data-enrichment-mail-intake once per Tracking-flagged reply,
//   before invoking data-enrichment. Only fields in the domain's whitelist are
//   ever returned — unrecognized lines (quoted signatures, "Regards: John",
//   etc.) are silently ignored rather than merged.
// IMPORTANT NOTES:
//   The reply is expected to echo the notifier email's [[FIELDS]]...[[/FIELDS]]
//   block with values filled in after each colon. A field left blank parses to
//   nothing (not included in the output), so data-enrichment's existing
//   coalesce(triggerBody()?['x'], variables('xRow')?['X'], '') fallback chains
//   fall through to the current DB value exactly like today. Child-table array
//   fields (Solace messageTypes; MuleSoft environments/transactionTypes/
//   messageTypes/sourceDestinations/uomMappings) are not representable as a
//   single "field: value" line and are deliberately excluded from every
//   domain's whitelist — those can only be supplied via the UI.
// ============================================================================
public sealed class ParseEnrichmentReplyFunction
{
    private static readonly Dictionary<string, string[]> StringFieldsByDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Btp"] = new[] { "mode", "developerId", "title", "shortText", "repoOwner", "repoName", "workflowFileName", "branchRef" },
        ["Solace"] = new[]
        {
            "action", "aclClientConnectDefaultAction", "aclPublishTopicDefaultAction",
            "aclSubscribeShareNameDefaultAction", "aclSubscribeTopicDefaultAction",
            "repoOwner", "repoName", "filePath", "branch", "baseBranch", "featureBranchName",
            "requesterEmail", "recipientEmail", "commitMessage"
        },
        ["MuleSoft"] = new[]
        {
            "countryCode", "partnerComment", "createdBy", "navProtocol", "navPort", "navUsername",
            "navDomain", "navService", "navSoapPort", "translationReceiverName",
            "repoOwner", "repoName", "filePathPrefix", "branch", "baseBranch", "featureBranchName",
            "requesterEmail", "recipientEmail", "commitMessage"
        }
    };

    private static readonly Dictionary<string, string[]> BoolFieldsByDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Btp"] = new[] { "serviceExists" },
        ["Solace"] = new[]
        {
            "clientProfileAllowGuaranteedMsgSendEnabled", "clientProfileAllowGuaranteedMsgReceiveEnabled",
            "clientProfileCompressionEnabled", "clientProfileReplicationAllowClientConnectWhenStandbyEnabled",
            "clientProfileAllowTransactedSessionsEnabled", "clientProfileAllowBridgeConnectionsEnabled",
            "clientProfileAllowGuaranteedEndpointCreateEnabled", "clientProfileAllowSharedSubscriptionsEnabled",
            "clientUserEnabled", "clientUserGuaranteedEndpointPermissionOverrideEnabled",
            "clientUserSubscriptionManagerEnabled", "serviceExists"
        },
        ["MuleSoft"] = new[] { "navUseCommonCert", "serviceExists" }
    };

    private static readonly Regex FieldLine = new(@"^\s*([A-Za-z][A-Za-z0-9_]*)\s*:\s*(.*)$", RegexOptions.Compiled);

    [Function("ParseEnrichmentReply")]
    public IDictionary<string, object> Run([WorkflowActionTrigger] ParseEnrichmentReplyRequest request)
    {
        Guard.RequireNotBlank(request?.Domain, nameof(request.Domain));

        var domain = request!.Domain!;
        var body = request.Body ?? string.Empty;
        var warnings = new List<string>();
        var fields = new Dictionary<string, object?>();

        var begin = body.IndexOf("[[FIELDS]]", StringComparison.OrdinalIgnoreCase);
        var end = begin >= 0 ? body.IndexOf("[[/FIELDS]]", begin, StringComparison.OrdinalIgnoreCase) : -1;
        if (begin < 0 || end < 0)
        {
            warnings.Add("No [[FIELDS]]...[[/FIELDS]] block found in reply body.");
        }
        else
        {
            var block = body.Substring(begin + "[[FIELDS]]".Length, end - (begin + "[[FIELDS]]".Length));
            var strings = StringFieldsByDomain.TryGetValue(domain, out var s) ? s : Array.Empty<string>();
            var bools = BoolFieldsByDomain.TryGetValue(domain, out var b) ? b : Array.Empty<string>();

            foreach (var rawLine in block.Split('\n'))
            {
                var match = FieldLine.Match(rawLine.TrimEnd('\r'));
                if (!match.Success) continue;

                var name = match.Groups[1].Value;
                var value = match.Groups[2].Value.Trim();
                if (value.Length == 0) continue; // left blank -> not answered, don't overwrite

                var stringMatch = Array.Find(strings, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
                var boolMatch = Array.Find(bools, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

                if (stringMatch != null)
                {
                    fields[stringMatch] = value;
                }
                else if (boolMatch != null)
                {
                    var upper = value.ToUpperInvariant();
                    if (upper is "YES" or "TRUE" or "1") fields[boolMatch] = true;
                    else if (upper is "NO" or "FALSE" or "0") fields[boolMatch] = false;
                    else warnings.Add($"Could not parse boolean value for '{name}': '{value}'.");
                }
                else
                {
                    warnings.Add($"Ignored unrecognized field '{name}' for domain '{domain}'.");
                }
            }
        }

        return new Dictionary<string, object>
        {
            ["fields"] = fields,
            ["matchedCount"] = fields.Count,
            ["warnings"] = warnings,
            ["correlationId"] = request.CorrelationId ?? ""
        };
    }
}
