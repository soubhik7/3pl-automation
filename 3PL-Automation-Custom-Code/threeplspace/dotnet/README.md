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

## Running the UI

### Prerequisites

- .NET SDK 8+ (this machine has 9.0.3xx and 10.0.2xx — either builds the solution).
- To **run** `ThreePl.Web` you need the ASP.NET Core runtime matching its
  target (net10.0 → ASP.NET Core 10.x, already installed here). Do **not**
  retarget the Web app to net8.0 on this machine: there is no ASP.NET Core
  8.x runtime, and roll-forwarding a net8 Blazor app onto the 10.x framework
  breaks `_framework/blazor.web.js` serving (404 → the page renders but no
  button works). `ThreePl.Core`/`ThreePl.Tests` stay net8.0 (Core matches
  `threepllocalfunction`).

### 1. Configure endpoints + database (one-time)

Create `ThreePl.Web/appsettings.Local.json` (gitignored — secrets never go in
the repo). Two ways to run:

**A. Local demo mode (works on this machine today — no SQL firewall access needed)**

Reads come from a local SQLite file; Save/Launch still POST to the **live**
Logic Apps. Note the split-brain this implies: rows you save land in the live
Azure SQL DB via the Logic App, so they will *not* show up in the local
Status view (which reads SQLite).

```jsonc
{
  "ConnectionStrings": {
    "OnboardingDb": "Data Source=C:\\...\\threeplspace\\dotnet\\ThreePl.Web\\threepl-local.db"
  },
  "Database": { "Provider": "Sqlite", "EnsureCreated": true },
  "LogicApps": {
    "DataEnrichmentUrl": "<data-enrichment SAS invoke URL — same value as the const API block in ../ui/3pl-onboarding.html>",
    "OnboardingLauncherUrl": "<onboarding-launcher SAS invoke URL — same source>"
  }
}
```

The SQLite file is created automatically on first start (`EnsureCreated`).
This checkout already has `appsettings.Local.json` set up this way, with
`threepl-local.db` pre-seeded with a demo onboarding
(`3PLPnP-DEMOSEED-EU-20260709120000`) so all four views show data.

**B. Live mode (full closed loop: saves show up in Status)**

Point reads at the live Azure SQL DB. Requires your client IP in the SQL
server firewall (the "Allow Azure services" rule does not cover local
machines) and a SQL user with SELECT on the dbo tables (plus DML on
`dbo.FieldRequirement` for the Admin page — run its DDL from
[../sql/schema.sql](../sql/schema.sql) once first).

```jsonc
{
  "ConnectionStrings": {
    "OnboardingDb": "Server=tcp:<server>.database.windows.net,1433;Database=<db>;User ID=<user>;Password=<pwd>;Encrypt=True;"
  },
  "Database": { "Provider": "SqlServer", "EnsureCreated": false },
  "LogicApps": { "...": "same as above" }
}
```

User-secrets work too: `dotnet user-secrets set "LogicApps:DataEnrichmentUrl" "..." --project ThreePl.Web`.

### 2. Start it

```powershell
cd 3PL-Automation-Custom-Code\threeplspace\dotnet
dotnet run --project ThreePl.Web
```

Open the URL it prints (e.g. `http://localhost:5000`). To pick a port:
`dotnet run --project ThreePl.Web --urls http://localhost:5199`.

### 3. Using the portal

1. **New Onboarding** (sidebar footer) → enter 3PL Partner + Region → the
   correlation ID is generated (`3PLPnP-<PARTNER>-<REGION>-<timestamp>`) and
   the session opens. Existing sessions are listed in the sidebar straight
   from `dbo.Onboarding` (searchable).
2. **Onboarding Intake** — Common / SAP BTP / Solace / MuleSoft tabs. Red
   stars mark required fields (direction-aware: switching Direction to
   Inbound drops the SME-enrichment requirements). Child tables (Solace
   message types, the five MuleSoft tables) support add/remove rows; blank
   rows are pruned on save. **Save** validates first, then POSTs to
   data-enrichment and toasts the returned `enrichmentStatus`.
3. **Status & Tracking** — per-domain cards (enrichment/deploy status,
   direction, branch approval, missing-field chips) + architecture-approval
   card + audit timeline (ActorEmail masked). Auto-refreshes every 10s.
4. **Launch** — "Launch Orchestrator" unlocks when all three domains exist
   and are Complete; per-domain deploy buttons gate on that domain only.
   202 → accepted (or awaiting architecture approval); 409 reasons surface
   as a toast. Force Redeploy ignores previous-deployment checks.
5. **Admin** — behind a sign-in gate (default **admin / admin123**, changeable
   via the `AdminAuth` config section; a UI lock for the internal portal, not
   hardened auth). Two panels:
   - **Approval Email Defaults** — the SME approval email per domain
     (SAP BTP / Solace / MuleSoft) plus the architecture-approval address,
     persisted server-side in `dbo.AdminSetting`. The domain addresses
     pre-fill each intake form's Recipient Email whenever it's empty (saved
     values always win); the architecture address is shown on the Launch view
     (the launcher workflow's actual approver recipient is configured on the
     Logic App itself).
   - **Field requirement levels** (Required / Outbound only / Optional),
     persisted in `dbo.FieldRequirement` and shared by every user; natural
     keys are locked. Drives the intake stars and save-time validation.

### 4. Tests / troubleshooting

```powershell
dotnet build ThreePl.sln   # clean
dotnet test  ThreePl.sln   # 53 tests
```

- **Page loads but clicks do nothing** → `_framework/blazor.web.js` is 404
  (see the framework note above) or the SignalR websocket is blocked.
- **"address already in use"** → another `ThreePl.Web` instance is running;
  stop it or pass a different `--urls`.
- **Sidebar empty / status warning banner** → the DB in
  `ConnectionStrings:OnboardingDb` is unreachable (firewall) or empty. The
  app still runs; saves go to the Logic App regardless.
- **Save fails with "endpoint is not configured"** → `LogicApps:*Url` empty —
  fill `appsettings.Local.json`/user-secrets.

## Database notes

- **schema.sql stays authoritative** ([../sql/schema.sql](../sql/schema.sql)); EF maps to the existing
  tables and no migrations ever run against the live DB.
- The one app-owned table is `dbo.FieldRequirement` (Admin config — the only
  table this app writes). Its DDL is appended to schema.sql; run that against
  the live DB before pointing the app at it (the app degrades to code-default
  requirement levels if the table is missing).
- `EncryptedPassword` is mapped but excluded from every DTO, and `ActorEmail`
  in the audit trail is masked server-side — verified by tests.
