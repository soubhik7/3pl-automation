namespace ThreePl.Core.Entities;

/// <summary>dbo.MuleSoftEnvironment — per-environment NAV connection row.</summary>
public class MuleSoftEnvironment
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string Environment { get; set; } = null!;
    public string? NavHost { get; set; }
    public string? NavCompany { get; set; }
    public string? NavSoapPath { get; set; }
    public string? NavRoutingCode { get; set; }
    public DateTime CreatedAt { get; set; }

    public MuleSoftPartner? Partner { get; set; }
}

/// <summary>dbo.MuleSoftTransactionType.</summary>
public class MuleSoftTransactionType
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string TransactionTypeCode { get; set; } = null!;
    public bool? TransactionTypeEnabled { get; set; }
    public string? TransactionTypeLabel { get; set; }
    public DateTime CreatedAt { get; set; }

    public MuleSoftPartner? Partner { get; set; }
}

/// <summary>dbo.MuleSoftMessageType.</summary>
public class MuleSoftMessageType
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string MessageType { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public MuleSoftPartner? Partner { get; set; }
}

/// <summary>dbo.MuleSoftSourceDestination.</summary>
public class MuleSoftSourceDestination
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string SourceDestinationFrom { get; set; } = null!;
    public string SourceDestinationTo { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public MuleSoftPartner? Partner { get; set; }
}

/// <summary>dbo.MuleSoftUomMapping.</summary>
public class MuleSoftUomMapping
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string UomFrom { get; set; } = null!;
    public string UomTo { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public MuleSoftPartner? Partner { get; set; }
}
