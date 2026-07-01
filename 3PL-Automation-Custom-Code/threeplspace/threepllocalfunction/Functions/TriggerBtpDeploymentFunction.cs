using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BtpAutomation.Services;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;

namespace BtpAutomation.Functions;

/// <summary>
/// Dispatches a GitHub Actions workflow_dispatch event for a BTP pipeline (e.g. the
/// "btp-Api-management-deploy.yml" app-creation workflow). Every value from the
/// triggering request is a parameter here, including the GitHub PAT (githubToken) —
/// if left blank, GitHubWorkflowDispatcher falls back to the GITHUB_TOKEN app
/// setting instead of failing.
/// </summary>
public class TriggerBtpDeploymentFunction
{
    private static readonly GitHubWorkflowDispatcher Dispatcher = new();

    [Function("TriggerBtpDeployment")]
    public Task<IDictionary<string, object>> Run(
        [WorkflowActionTrigger] string repoOwner,
        string repoName,
        string workflowFileName,
        string branchRef,
        string githubToken,
        string subAccount,
        string mode,
        string environment,
        string developerId,
        string title,
        string shortText,
        string productName)
    {
        Require(repoOwner, nameof(repoOwner));
        Require(repoName, nameof(repoName));
        Require(workflowFileName, nameof(workflowFileName));
        Require(branchRef, nameof(branchRef));
        Require(subAccount, nameof(subAccount));
        Require(mode, nameof(mode));
        Require(environment, nameof(environment));
        Require(developerId, nameof(developerId));
        Require(title, nameof(title));
        Require(productName, nameof(productName));

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

        return Dispatcher.DispatchAsync(repoOwner, repoName, workflowFileName, branchRef, githubToken, inputs);
    }

    private static void Require(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} must not be empty.");
    }
}
