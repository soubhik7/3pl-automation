using System.Collections.Generic;

namespace SolaceConfigGenerator.Models;

/// <summary>
/// CSV columns (pipe-delimited topics within each topic cell):
/// Brand, Env, SystemName, ThreePLCode, EncryptedPassword,
/// DespatchStockTopics, SourcingStockTopics, ReturnStockTopics, StockMovementTopics
///
/// Example row:
/// petc,rc,navision,3plpnp,"![+ycEERpRxxm5RfqLRNxBXw==]","topic1|topic2","topic1|topic2","topic1","topic1"
/// </summary>
public sealed record SolaceOnboardingRecord(
    string Brand,
    string Env,
    string SystemName,
    string ThreePLCode,
    string EncryptedPassword,
    IReadOnlyList<string> DespatchStockTopics,
    IReadOnlyList<string> SourcingStockTopics,
    IReadOnlyList<string> ReturnStockTopics,
    IReadOnlyList<string> StockMovementTopics
)
{
    // Drives all Solace resource names: {Brand}-{Env}-{SystemName}-{ThreePLCode}-sys
    public string NamingPrefix => $"{Brand}-{Env}-{SystemName}-{ThreePLCode}-sys";
}
