using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SolaceConfigGenerator.Models;
using SolaceConfigGenerator.Services;

namespace SolaceConfigGenerator.Functions;

public class GenerateSolaceConfigFunction
{
    private static readonly CsvParser CsvParser = new();
    private static readonly SolaceConfigBuilder ConfigBuilder = new();

    private readonly ILogger<GenerateSolaceConfigFunction> _logger;

    public GenerateSolaceConfigFunction(ILogger<GenerateSolaceConfigFunction> logger)
    {
        _logger = logger;
    }

    [Function("GenerateSolaceConfig")]
    public Task<GenerateSolaceConfigResponse> Run([WorkflowActionTrigger] string csvContent)
    {
        _logger.LogInformation("GenerateSolaceConfig invoked, csvContent length={Len}", csvContent?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(csvContent))
            throw new ArgumentException("csvContent must not be empty.");

        var records = CsvParser.Parse(csvContent);
        var results = records.Select(BuildResult).ToList();

        return Task.FromResult(new GenerateSolaceConfigResponse { Results = results });
    }

    private static SolaceConfigResult BuildResult(SolaceOnboardingRecord record)
    {
        try
        {
            return new SolaceConfigResult
            {
                NamingPrefix = record.NamingPrefix,
                SolaceConfig = ConfigBuilder.Build(record)
            };
        }
        catch (Exception ex)
        {
            return new SolaceConfigResult
            {
                NamingPrefix = record.NamingPrefix,
                Error        = ex.Message
            };
        }
    }
}
