// -----------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------

// ============================================================================
// Program — custom-code worker host entry point (shared by all 3 domains)
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Logic Apps Standard "Custom Code" actions run in a separate .NET worker
//   process (CustomCodeNetFxWorker) hosted alongside the Node.js Logic Apps
//   runtime. This Main() is that worker process's entry point — it's what
//   boots and hosts every [Function]-attributed class in this project
//   (Solace, BTP, and MuleSoft alike), not per-domain code.
// HOW TO USE:
//   Nothing calls this directly — the Logic Apps host launches it. Don't add
//   per-domain services here; if a function needs a dependency, prefer
//   NullLogger<T>.Instance over constructor injection (see next note).
// IMPORTANT NOTES:
//   AddApplicationInsightsTelemetryWorkerService() wires up the *outer* host's
//   DI container. Critically, the per-function-invocation DI container used
//   when a [Function] class is actually activated is a SEPARATE container
//   that this SDK version (1.3.0) does NOT feed from here — it does not
//   register ILogger<T>/ILoggerFactory, so no [Function] class in this
//   project takes either as a constructor parameter (that was tried and
//   fails to activate); every function instead uses a NullLogger<T>.Instance
//   field. AddLogging() was tried here too and confirmed to have zero effect
//   on that inner container.
// ============================================================================
namespace threepllocalfunctionspace
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    internal static class Program
    {
        private static void Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWebApplication()
                .ConfigureServices(services =>
                {
                    services.AddApplicationInsightsTelemetryWorkerService();
                })
                .Build();

            host.Run();
        }
    }
}
