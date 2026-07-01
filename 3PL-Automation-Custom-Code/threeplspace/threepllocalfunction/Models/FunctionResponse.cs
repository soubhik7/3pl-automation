using System.Collections.Generic;

namespace SolaceConfigGenerator.Models;

public sealed class GenerateSolaceConfigResponse
{
    public List<SolaceConfigResult> Results { get; init; } = [];
}

public sealed class SolaceConfigResult
{
    public required string NamingPrefix { get; init; }

    // Built by SolaceConfigBuilder as a plain dictionary — see that class for why.
    public IDictionary<string, object>? SolaceConfig { get; init; }

    public string? Error { get; init; }
}
