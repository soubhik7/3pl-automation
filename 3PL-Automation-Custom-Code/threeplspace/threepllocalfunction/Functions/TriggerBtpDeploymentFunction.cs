using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BtpAutomation.Services;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ThreePlLocalFunction.Shared;

namespace BtpAutomation.Functions;

// ============================================================================
// TriggerBtpDeploymentFunction — Logic Apps entry point for the BTP domain
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Fires the "btp-Api-management-deploy.yml" GitHub Actions workflow (or any
//   workflow_dispatch-enabled workflow) for a BTP app-creation/deployment
//   pipeline, from a Logic Apps "Call a local function" action. Every field
//   from the original triggering request (repo location, branch, and all the
//   BTP-specific inputs: subAccount/mode/environment/developerId/title/
//   shortText/productName) is a parameter here — nothing about a specific
//   BTP pipeline run is hardcoded.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName "TriggerBtpDeployment"
//   and all 12 parameters below supplied from triggerBody(). See
//   three3pllogicapp/btp-app-creation/workflow.json for a worked example with
//   safe coalesce() defaults for portal testing.
// IMPORTANT NOTES:
//   No githubToken parameter — the GitHub PAT is resolved internally from
//   the "githubToken" app setting (see GitHubWorkflowDispatcher /
//   GitHubApiClient.ResolveToken) rather than being passed through as a
//   Logic Apps action input, so it never appears in workflow run history.
//   shortText is the only optional business field (GitHub Actions treats a
//   missing workflow input as blank, so an empty string is sent instead of
//   null).
//   This function only lives in the BtpAutomation namespace — it shares no
//   mutable state with the Solace or MuleSoft domains, only the stateless
//   Guard/GitHubApiClient helpers under ThreePlLocalFunction.Shared.
// ============================================================================
public class TriggerBtpDeploymentFunction
{
    private static readonly GitHubWorkflowDispatcher Dispatcher = new();

    private readonly ILogger<TriggerBtpDeploymentFunction> _logger =
        NullLogger<TriggerBtpDeploymentFunction>.Instance;

    [Function("TriggerBtpDeployment")]
    public async Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string repoOwner,
        string repoName,
        string workflowFileName,
        string branchRef,
        string subAccount,
        string mode,
        string environment,
        string developerId,
        string title,
        string shortText,
        string productName,
        string correlationId)
    {
        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] TriggerBtpDeployment invoked for {Repo} ref={Ref} mode={Mode} env={Env}",
                correlationId, $"{repoOwner}/{repoName}", branchRef, mode, environment);

            Guard.RequireNotBlank(repoOwner, nameof(repoOwner));
            Guard.RequireNotBlank(repoName, nameof(repoName));
            Guard.RequireNotBlank(workflowFileName, nameof(workflowFileName));
            Guard.RequireNotBlank(branchRef, nameof(branchRef));
            Guard.RequireNotBlank(subAccount, nameof(subAccount));
            Guard.RequireNotBlank(mode, nameof(mode));
            Guard.RequireNotBlank(environment, nameof(environment));
            Guard.RequireNotBlank(developerId, nameof(developerId));
            Guard.RequireNotBlank(title, nameof(title));
            Guard.RequireNotBlank(productName, nameof(productName));

            var inputs = new Dictionary<string, string>
            {
                ["sub_account"] = subAccount,
                ["mode"] = mode,
                ["environment"] = environment,
                ["developer_id"] = developerId,
                ["title"] = title,
                ["short_text"] = shortText ?? "",
                ["product_name"] = productName
            };

            var result = await Dispatcher.DispatchAsync(repoOwner, repoName, workflowFileName, branchRef, null, inputs);
            result["correlationId"] = correlationId;
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{correlationId}] TriggerBtpDeployment failed: {ex.Message}", ex);
        }
    }
}
