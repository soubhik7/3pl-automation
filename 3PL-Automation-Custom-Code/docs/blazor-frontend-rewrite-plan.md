# Rewrite front end to C# Blazor Server (EF Core reads) — Logic Apps stay the backend

## Context

Today the 3PL onboarding portal is a **static HTML file + vanilla JS** ([../threeplspace/ui/3pl-onboarding.html](../threeplspace/ui/3pl-onboarding.html)) that talks to **Azure Logic App Standard** HTTP workflows for everything (`data-enrichment` for saves, `enrichment-status` for reads, `onboarding-launcher` for launch), which in turn own all SQL + business logic.

The goal is to rewrite the **front end as a C# .NET app** (rendering in C#, not JS) while **keeping the Logic App workflows as the backend** for all business logic. Confirmed architecture:

- **Blazor Server** UI (all C#, server-rendered over SignalR).
- **EF Core (ORM) for READS** — render status cards, intake prefill, session list, and the audit timeline directly from Azure SQL.
- **Logic Apps for WRITES** — Save (per domain) and Launch still POST to the existing `data-enrichment` and `onboarding-launcher` HTTP triggers. Validation, enrichment-status computation, upsert, audit logging, orchestration, and the email flows all **stay in the Logic Apps, unchanged**.

```
Blazor Server UI (C#)
 ├─ READS:  EF Core ──────────────→ Azure SQL      (status, prefill, sessions, audit, admin)
 └─ WRITES: HttpClient ──→ Logic App HTTP triggers  (business logic unchanged)
        ├─ data-enrichment      (save each domain)
        └─ onboarding-launcher  (gated launch)
```

Nothing in the Logic App layer changes. The rewrite replaces only the presentation tier. The `enrichment-status` workflow becomes optional (reads now go through EF), but is left deployed as-is.

**Confirmed decisions:** Blazor Server · EF Core for reads / Logic Apps for writes · Phase 1 first (runnable foundation: EF read model + Intake/Status/Launch/Admin UI, Save/Launch wired to the live Logic Apps, built/tested/run).

## Target architecture

New .NET solution at `3PL-Automation-Custom-Code/threeplspace/dotnet/` (leaves the Logic App tree and current HTML untouched):

```
ThreePl.sln  (net8)
├─ ThreePl.Core            class library
│    ├─ Entities/          EF entities for the dbo tables needed to render
│    ├─ Data/              OnboardingDbContext (read-focused) + Fluent config
│    ├─ Reads/             StatusReadService, IntakePrefillService, SessionService,
│    │                     MissingFieldRules (direction-aware, shared w/ Admin markers)
│    ├─ Writes/            LogicAppClient (typed HttpClient: SaveDomainAsync, LaunchAsync)
│    └─ Admin/             FieldRequirementService (server-persisted admin config)
├─ ThreePl.Web             ASP.NET Core Blazor Server
│    ├─ Components/        Intake, Status, Launch, Admin pages + child-row editors
│    ├─ wwwroot/           CSS ported from the current HTML
│    └─ Program.cs         DI, EF, typed HttpClient, options
└─ ThreePl.Tests           xUnit — MissingFieldRules parity, DTO mapping, payload shapes
```

- net8 (matches existing `threepllocalfunction`); `Microsoft.EntityFrameworkCore.SqlServer`.
- **schema.sql stays authoritative.** EF maps to existing tables; no migrations run against the live DB. One new app-owned table, `FieldRequirement`, for Admin config (the app's own settings, not onboarding business data).
- **Endpoints + SAS URLs live in config** (`appsettings`/user-secrets), never committed — this repo had a prior secret-leak incident. Same URLs currently hardcoded in the HTML `const API` block.

## Phase 1 — detailed scope (first delivery)

### 1. EF Core read model — `ThreePl.Core/Entities` + `Data/OnboardingDbContext.cs`
Hand-write entities from the authoritative [../threeplspace/sql/schema.sql](../threeplspace/sql/schema.sql) (no live-DB scaffolding needed):
- `Onboarding` (PK `CorrelationId`; carries the **Common** fields `InterfaceId`…`SubscriptionRules`).
- `BtpConfig`, `SolaceClient`, `MuleSoftPartner` — `EnrichmentStatus`, `DeploymentStatus`, `Direction`, `CardSentAt/RespondedAt`, GitHub metadata; Solace/Mule also `BranchApprovalStatus`, `PendingBranchName`. **`EncryptedPassword` is mapped but excluded from every DTO** (privacy — must never reach the browser).
- Children: `SolaceMessageType` (FK `ClientId`); the 5 MuleSoft child tables (FK `PartnerId`).
- `EnrichmentAuditLog`, `OnboardingApproval`.
- `FieldRequirement` (new: `Domain`, `FieldName`, `Level` ∈ Always/Outbound/Optional) — app-owned admin config.
- DbContext is read-mostly; the only writes it performs are to `FieldRequirement`.

### 2. Read services — `ThreePl.Core/Reads`
- **`StatusReadService.GetStatusAsync(correlationId)`** → the same DTO shape the current UI renders: per-domain `found/enrichmentStatus/deploymentStatus/cardSentAt/cardRespondedAt/direction/branchApprovalStatus/pendingBranchName/missingFields`, plus `architectureApproval`, `readyToLaunch` (all 3 found && `EnrichmentStatus='Complete'`), and `auditTrail[]` (TOP 100 by CorrelationId, **ActorEmail masked** in C#, no password). `EnrichmentStatus` is read straight from the stored column (data-enrichment already computed it on write) — the C# side only recomputes `missingFields` for display.
- **`MissingFieldRules`** — pure, direction-aware helper mirroring the required-field lists in `data-enrichment`'s `Compose_{Domain}_Enrichment_Status` (e.g. Btp at `three3pllogicapp/data-enrichment/workflow.json:745`; Inbound ⇒ no missing fields). Shared by the status chips **and** the Admin required-markers so UI and backend agree.
- **`IntakePrefillService`** — full records + child arrays for prefilling the intake forms when a past session is opened.
- **`SessionService`** — recent correlationIds (from `Onboarding`), replacing the current `localStorage` list; correlationId builder ported from the HTML `buildCorrelationId`.

### 3. Write client — `ThreePl.Core/Writes/LogicAppClient.cs`
Typed `HttpClient` (registered via `IHttpClientFactory`) wrapping the **existing** Logic App triggers, payloads identical to what the current HTML posts:
- `SaveDomainAsync(domain, correlationId, fields, childArrays)` → POST `data-enrichment` (Common/Btp/Solace/MuleSoft; Solace `messageTypes`; the 5 MuleSoft child arrays). Returns the workflow's `{ enrichmentStatus, record, … }`. Surfaces the clean HTTP 500/400 bodies the workflow returns (the hardening applied earlier).
- `LaunchAsync(correlationId, domains, forceRedeploy)` → POST `onboarding-launcher`; handles 202 (accepted) and 409 (gate failure) per-domain reasons.
- Base URLs + SAS query strings from `IOptions<LogicAppOptions>` bound to config.

### 4. Blazor Server UI — `ThreePl.Web/Components`
Port the current 4-view UX + visual design (reuse the CSS verbatim) as Blazor components:
- **Intake**: Common / SAP BTP / Solace / MuleSoft tabs; flat fields + dynamic child-row editor components (add/remove rows — replaces the JS `render*Table`). Save → `LogicAppClient.SaveDomainAsync`; show returned `enrichmentStatus`. `encryptedPassword` is a password field, never prefilled, never persisted client-side.
- **Status & Tracking**: per-domain cards + audit timeline from `StatusReadService`; live refresh via Blazor (a server-side `PeriodicTimer` or SignalR push) instead of the 10s JS `fetch`.
- **Launch**: gating UI (`readyToLaunch` from reads); button → `LogicAppClient.LaunchAsync`; render 202→poll and 409 reasons.
- **Admin**: field-requirement config (Required / Outbound-only / Optional per field) **server-persisted** via `FieldRequirementService` (shared across users — upgrade over per-browser `localStorage`). Direction-aware markers + client validation reuse `MissingFieldRules`.

### 5. Tests — `ThreePl.Tests`
- `MissingFieldRules`: Btp/Solace/MuleSoft Complete vs AwaitingInput, Inbound short-circuit, per-field lists (assert parity with the workflow expressions).
- Status DTO mapping (EF rows → DTO), incl. password exclusion + email masking.
- `LogicAppClient` payload shape matches what the HTML posts (guards the write contract) — via a stub `HttpMessageHandler`.
- EF against **SQLite in-memory** so tests need no live DB.

## Verification (Phase 1)
- `dotnet build ThreePl.sln` clean; `dotnet test` green.
- **Run** (`dotnet run --project ThreePl.Web`) and drive headless (Playwright):
  - **Write path against the LIVE Logic App** (works now, post-firewall-fix): create session → Save BTP Outbound with all fields → data-enrichment returns 200/`Complete`; Save Inbound with only natural keys → `Complete` (short-circuit); missing-field validation blocks before POST.
  - **Read path**: point EF at the same DB and confirm the Status view renders the just-saved row (`enrichmentStatus`, masked audit trail, no `EncryptedPassword`); toggle an Admin requirement → intake marker updates.
- Local reads need DB access: this machine's IP must be in the SQL firewall (the "Allow Azure services" rule only covers Azure-hosted callers). If the firewall blocks local EF, run reads against **LocalDB/SQLite** seeded from a saved row and verify the write path separately against the live Logic App.
- Confirm `EncryptedPassword` never appears in any Status DTO; confirm `ActorEmail` masking.

## Follow-up (later phases)
- Live-status push (SignalR) polish; deploy the Blazor app to App Service; **Managed Identity** for the EF read connection (retire the SQL password from config); tighten Logic App CORS to the hosted origin; optionally retire the now-unused `enrichment-status` workflow.

## Files/dirs created (Phase 1)
| Path | Change |
|---|---|
| `threeplspace/dotnet/ThreePl.sln` | new solution |
| `threeplspace/dotnet/ThreePl.Core/**` | EF entities + DbContext, read services, `MissingFieldRules`, `LogicAppClient`, admin |
| `threeplspace/dotnet/ThreePl.Web/**` | Blazor Server app (Intake/Status/Launch/Admin), ported CSS |
| `threeplspace/dotnet/ThreePl.Tests/**` | xUnit tests |

Untouched: all Logic App workflows, `threepllocalfunction`, the current static HTML (kept working in parallel until cutover), `schema.sql` (only addition is the app-owned `FieldRequirement` table DDL when Admin persistence lands).
