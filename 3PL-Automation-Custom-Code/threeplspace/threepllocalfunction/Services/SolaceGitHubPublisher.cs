using System.Collections.Generic;
using System.Threading.Tasks;
using ThreePlLocalFunction.Shared;

namespace SolaceConfigGenerator.Services;

// ============================================================================
// SolaceGitHubPublisher — Solace-domain wrapper around GitHubBranchPublisher
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GenerateSolaceConfigFunction only produces the Solace onboarding JSON — it
//   doesn't know anything about GitHub. This class is the Solace-specific
//   translation layer: it takes the single generated config (already a JSON
//   string, one record's worth) plus where it should land in a repo, and
//   hands a single {path: content} pair to the shared, domain-agnostic
//   GitHubBranchPublisher. Keeping this thin (validation + one dictionary
//   entry) means the actual GitHub API logic lives in exactly one place,
//   shared with the MuleSoft equivalent (MuleSoftGitHubPublisher), without
//   the two domains' function/service classes referencing each other.
// HOW TO USE:
//   await new SolaceGitHubPublisher().PublishAsync(repoOwner, repoName,
//       baseBranch, featureBranchName, filePath, solaceConfigJson,
//       commitMessage, githubToken);
//   filePath is the full repo-relative path to write, e.g.
//   "solace-configs/petc-rc-navision-3plpnp-sys.json" — not hardcoded, since
//   different callers may want a different repo layout.
// IMPORTANT NOTES:
//   solaceConfigJson is expected to already be the serialized JSON produced
//   by GenerateSolaceConfig's output (body(...)['results'][n]['solaceConfig']
//   converted to a string in the calling workflow) — this class does not
//   re-parse the CSV or regenerate it, to avoid duplicating that logic here.
// ============================================================================
public sealed class SolaceGitHubPublisher
{
    public Task<IDictionary<string, object>> PublishAsync(
        string repoOwner,
        string repoName,
        string baseBranch,
        string featureBranchName,
        string filePath,
        string solaceConfigJson,
        string commitMessage,
        string? githubToken)
    {
        Guard.RequireNotBlank(repoOwner, nameof(repoOwner));
        Guard.RequireNotBlank(repoName, nameof(repoName));
        Guard.RequireNotBlank(baseBranch, nameof(baseBranch));
        Guard.RequireNotBlank(featureBranchName, nameof(featureBranchName));
        Guard.RequireNotBlank(filePath, nameof(filePath));
        Guard.RequireNotBlank(solaceConfigJson, nameof(solaceConfigJson));
        Guard.RequireNotBlank(commitMessage, nameof(commitMessage));

        return GitHubBranchPublisher.PushFilesToFeatureBranchAsync(
            repoOwner, repoName, baseBranch, featureBranchName, githubToken, commitMessage,
            new Dictionary<string, string> { [filePath] = solaceConfigJson });
    }
}
