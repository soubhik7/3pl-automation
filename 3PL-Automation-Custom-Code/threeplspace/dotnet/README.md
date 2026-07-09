# ThreePl — Blazor Server front end for 3PL onboarding

C# rewrite of the static HTML portal ([../ui/3pl-onboarding.html](../ui/3pl-onboarding.html)).
The Logic App workflows remain the backend for all business logic:

```
Blazor Server UI (C#)
 ├─ READS:  EF Core ──────────────→ Azure SQL      (status, prefill, sessions, audit, admin)
 └─ WRITES: HttpClient ──→ Logic App HTTP triggers  (business logic unchanged)
        ├─ data-enrichment      (save each domain)
        └─ onboarding-launcher  (gated launch)
```

| Project | Purpose |
|---|---|
| `ThreePl.Core` | EF entities + `OnboardingDbContext` (read-focused), read services (`StatusReadService`, `IntakePrefillService`, `SessionService`), `MissingFieldRules` (workflow-parity, direction-aware), `LogicAppClient` (typed write client), admin (`FieldRequirementService`) |
| `ThreePl.Web` | Blazor Server app — Intake / Status & Tracking / Launch / Admin, CSS ported from the HTML |
| `ThreePl.Tests` | xUnit — rules parity with the workflow expressions, DTO mapping (password exclusion, email masking), Logic App payload-shape guards, EF over SQLite in-memory |

## Configuration (never commit secrets)

Endpoints and connection strings live **outside** the repo (this repo had a
prior secret-leak incident). Use user-secrets or a gitignored
`ThreePl.Web/appsettings.Local.json`:

```jsonc
{
  "ConnectionStrings": {
    // Azure SQL (reads). Your client IP must be in the SQL firewall —
    // "Allow Azure services" only covers Azure-hosted callers.
    "OnboardingDb": "Server=tcp:...;Database=...;User ID=...;Password=...;Encrypt=True;"
  },
  "Database": {
    "Provider": "SqlServer",   // or "Sqlite" for a local fallback DB
    "EnsureCreated": false      // Sqlite only; NEVER creates schema on SQL Server usage
  },
  "LogicApps": {
    "DataEnrichmentUrl": "https://<logic-app-host>/api/data-enrichment/triggers/When_a_HTTP_request_is_received/invoke?...sig=...",
    "OnboardingLauncherUrl": "https://<logic-app-host>/api/onboarding-launcher/triggers/When_a_HTTP_request_is_received/invoke?...sig=..."
  }
}
```

Equivalent user-secrets: `dotnet user-secrets set "LogicApps:DataEnrichmentUrl" "..." --project ThreePl.Web`.

## Run

```
dotnet build ThreePl.sln
dotnet test  ThreePl.sln
dotnet run --project ThreePl.Web
```

The dev machines here carry newer ASP.NET Core runtimes than 8.x, so the Web
and Tests projects set `RollForward=LatestMajor`.

## Database notes

- **schema.sql stays authoritative** ([../sql/schema.sql](../sql/schema.sql)); EF maps to the existing
  tables and no migrations ever run against the live DB.
- The one app-owned table is `dbo.FieldRequirement` (Admin config — the only
  table this app writes). Its DDL is appended to schema.sql; run that against
  the live DB before pointing the app at it (the app degrades to code-default
  requirement levels if the table is missing).
- `EncryptedPassword` is mapped but excluded from every DTO, and `ActorEmail`
  in the audit trail is masked server-side — verified by tests.
