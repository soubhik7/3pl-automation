namespace ThreePl.Core.Entities;

/// <summary>dbo.SolaceMessageType — one MessageType/Topic/Queue row per client.</summary>
public class SolaceMessageType
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string MessageType { get; set; } = null!;
    public string? Topic { get; set; }
    public string? QueuePermission { get; set; }
    public bool? QueueEgressEnabled { get; set; }
    public int? QueueMaxRedeliveryCount { get; set; }
    public DateTime CreatedAt { get; set; }

    public SolaceClient? Client { get; set; }
}
