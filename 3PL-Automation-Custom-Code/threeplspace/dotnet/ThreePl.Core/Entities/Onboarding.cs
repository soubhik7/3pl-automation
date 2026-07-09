namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.Onboarding — master anchor row, one per CorrelationId, plus the
/// "Common" tab columns describing the interface being onboarded as a whole.
/// </summary>
public class Onboarding
{
    public string CorrelationId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Common interface details (one set per CorrelationId)
    public string? InterfaceId { get; set; }
    public string? EaRef { get; set; }
    public string? SourceApp { get; set; }
    public string? TargetApp { get; set; }
    public string? BusinessObject { get; set; }
    public string? Country { get; set; }
    public string? SourceFormat { get; set; }
    public string? SourceInterfaceType { get; set; }
    public string? TargetFormat { get; set; }
    public string? TargetInterfaceType { get; set; }
    public string? FunctionalDescription { get; set; }
    public string? Volume { get; set; }
    public string? SizePerMessage { get; set; }
    public string? PeakVolume { get; set; }
    public string? ThreePlPartnerId { get; set; }
    public string? NavInstanceId { get; set; }
    public string? CountryCodeIso { get; set; }
    public string? RegionIso { get; set; }
    public string? SubscriptionRules { get; set; }
}
