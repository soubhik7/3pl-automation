namespace ThreePl.Core.Writes;

/// <summary>
/// Logic App HTTP trigger invoke URLs (SAS-signed). These live in
/// appsettings/user-secrets only — never committed, this repo had a prior
/// secret-leak incident.
/// </summary>
public class LogicAppOptions
{
    public const string SectionName = "LogicApps";

    /// <summary>data-enrichment trigger URL (per-domain saves).</summary>
    public string DataEnrichmentUrl { get; set; } = "";

    /// <summary>onboarding-launcher trigger URL (gated launch).</summary>
    public string OnboardingLauncherUrl { get; set; } = "";

    /// <summary>Identity stamped on launch requests (launchedBy).</summary>
    public string LaunchedBy { get; set; } = "3pl-portal-ui@example.com";
}
