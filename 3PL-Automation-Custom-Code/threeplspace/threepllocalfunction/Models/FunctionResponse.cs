using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SolaceConfigGenerator.Models;

public sealed class GenerateSolaceConfigResponse
{
    [JsonPropertyName("results")]
    public List<SolaceConfigResult> Results { get; init; } = [];
}

public sealed class SolaceConfigResult
{
    [JsonPropertyName("namingPrefix")]
    public required string NamingPrefix { get; init; }

    [JsonPropertyName("solaceConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SolaceConfig? SolaceConfig { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}
