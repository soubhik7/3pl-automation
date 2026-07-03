using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SolaceConfigGenerator.Models;
using SolaceConfigGenerator.Services;

namespace SolaceConfigGenerator.Functions;

// ============================================================================
// GenerateSolaceConfigFunction — Logic Apps entry point for the Solace domain
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Turns a Solace onboarding CSV into the real Solace onboarding JSON shape,
//   from a Logic Apps "Call a local function" action — this is the first
//   step of the Solace pipeline (the second is
//   PublishSolaceConfigToFeatureBranch, which commits the result to GitHub).
//   All field values (client-profile flags, ACL defaults, queue permissions,
//   message types/topics) are CSV-driven via CsvParser + SolaceConfigBuilder
//   — nothing about a specific onboarding is hardcoded here.
// HOW TO USE:
//   Logic Apps action "InvokeFunction" with functionName
//   "GenerateSolaceConfig" and a single csvContent parameter (the whole CSV
//   as one string, header row included). See
//   three3pllogicapp/sample3pl/workflow.json for a worked example.
// IMPORTANT NOTES:
//   One CSV can describe multiple onboarding records (grouped by
//   Brand/Env/SystemName/ThreePLCode) — each record's success/failure is
//   independent (see BuildResult's try/catch), so one malformed record
//   doesn't fail the others in the same batch. A CSV that fails to parse at
//   all (wrong column count, unknown Action) throws and fails the whole
//   action — that's intentional fail-fast behavior for a structurally
//   invalid CSV, as opposed to one bad record within an otherwise-valid CSV.
// ============================================================================
public class GenerateSolaceConfigFunction
{
    private static readonly CsvParser CsvParser = new();
    private static readonly SolaceConfigBuilder ConfigBuilder = new();

    // The custom-code host's DI container (SDK 1.3.0) doesn't register ILogger<T>/ILoggerFactory,
    // so this can't take either as a constructor dependency without failing to activate.
    private readonly ILogger<GenerateSolaceConfigFunction> _logger = NullLogger<GenerateSolaceConfigFunction>.Instance;

    [Function("GenerateSolaceConfig")]
    public Task<GenerateSolaceConfigResponse> Run([WorkflowActionTrigger] string csvContent, string correlationId)
    {
        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] GenerateSolaceConfig invoked, csvContent length={Len}", correlationId, csvContent?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(csvContent))
                throw new ArgumentException("csvContent must not be empty.");

            var records = CsvParser.Parse(csvContent);
            var results = records.Select(BuildResult).ToList();

            return Task.FromResult(new GenerateSolaceConfigResponse { Results = results, CorrelationId = correlationId });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{correlationId}] GenerateSolaceConfig failed: {ex.Message}", ex);
        }
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
