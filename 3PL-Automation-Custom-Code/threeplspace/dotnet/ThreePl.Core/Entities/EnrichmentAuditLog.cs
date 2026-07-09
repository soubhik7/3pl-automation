namespace ThreePl.Core.Entities;

/// <summary>
/// dbo.EnrichmentAuditLog — append-only event trail across every
/// data-enrichment channel/workflow. ActorEmail is PII and must be masked
/// before it reaches the browser.
/// </summary>
public class EnrichmentAuditLog
{
    public long Id { get; set; }
    public string Domain { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
    public string? EntityKey { get; set; }
    public string Channel { get; set; } = null!;
    public string? ActorEmail { get; set; }
    public string EventType { get; set; } = null!;
    public string? EventDetail { get; set; }
    public DateTime CreatedAt { get; set; }
}
