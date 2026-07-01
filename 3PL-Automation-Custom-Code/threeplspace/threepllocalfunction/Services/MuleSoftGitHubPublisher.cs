using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ThreePlLocalFunction.Shared;

namespace MuleSoftAutomation.Services;

// ============================================================================
// MuleSoftGitHubPublisher — MuleSoft-domain wrapper around GitHubBranchPublisher
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   GenerateMuleSoftConfig returns up to 4 files (app.yaml always, dev/tst/
//   prod.yaml only for the environments the CSV/existing YAML actually
//   covers) — never a fixed set of 4. This class turns whichever of those
//   were supplied (blank = "this environment wasn't generated, don't push
//   it") into the {path: content} map GitHubBranchPublisher needs, under a
//   caller-supplied path prefix. The actual GitHub API calls are not
//   duplicated here — they live once, in the shared GitHubBranchPublisher,
//   reused by SolaceGitHubPublisher.
// HOW TO USE:
//   await new MuleSoftGitHubPublisher().PublishAsync(repoOwner, repoName,
//       baseBranch, featureBranchName, filePathPrefix, appYaml, devYaml,
//       tstYaml, prodYaml, commitMessage, githubToken);
//   filePathPrefix is a repo-relative folder, e.g.
//   "mulesoft-configs/royal-canin-france/" (trailing slash optional, added
//   automatically) — not hardcoded, so different callers can lay the repo
//   out differently.
// IMPORTANT NOTES:
//   appYaml/devYaml/tstYaml/prodYaml are each optional — pass "" (or leave
//   blank) for any file GenerateMuleSoftConfig didn't produce for this call.
//   At least one must be non-blank, or this throws (nothing to push).
// ============================================================================
public sealed class MuleSoftGitHubPublisher
{
    private const string AppFileName = "app.yaml";
    private const string DevFileName = "dev.yaml";
    private const string TstFileName = "tst.yaml";
    private const string ProdFileName = "prod.yaml";

    public Task<IDictionary<string, object>> PublishAsync(
        string repoOwner,
        string repoName,
        string baseBranch,
        string featureBranchName,
        string filePathPrefix,
        string appYaml,
        string devYaml,
        string tstYaml,
        string prodYaml,
        string commitMessage,
        string githubToken)
    {
        Guard.RequireNotBlank(repoOwner, nameof(repoOwner));
        Guard.RequireNotBlank(repoName, nameof(repoName));
        Guard.RequireNotBlank(baseBranch, nameof(baseBranch));
        Guard.RequireNotBlank(featureBranchName, nameof(featureBranchName));
        Guard.RequireNotBlank(filePathPrefix, nameof(filePathPrefix));
        Guard.RequireNotBlank(commitMessage, nameof(commitMessage));

        var prefix = filePathPrefix.EndsWith('/') ? filePathPrefix : filePathPrefix + "/";

        var files = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(appYaml)) files[prefix + AppFileName] = appYaml;
        if (!string.IsNullOrWhiteSpace(devYaml)) files[prefix + DevFileName] = devYaml;
        if (!string.IsNullOrWhiteSpace(tstYaml)) files[prefix + TstFileName] = tstYaml;
        if (!string.IsNullOrWhiteSpace(prodYaml)) files[prefix + ProdFileName] = prodYaml;

        if (files.Count == 0)
            throw new ArgumentException(
                "At least one of appYaml/devYaml/tstYaml/prodYaml must be non-blank.");

        return GitHubBranchPublisher.PushFilesToFeatureBranchAsync(
            repoOwner, repoName, baseBranch, featureBranchName, githubToken, commitMessage, files);
    }
}
