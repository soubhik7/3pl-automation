# UI → data-enrichment → SQL → orchestrator: full integration

## Context

The data-enrichment layer (3 workflows + SQL tables + audit log) is built and live-tested. The user now wants the existing business-user UI (`3PL-Automation-LogicApp-Workflow\3PL-Automation\ui\3pl-onboarding.html`) wired into this stack: a business person fills in whatever they know → rows land in the same SQL tables via `data-enrichment` (channel Api) → the notifier/card/mail workflows enrich the gaps → and only when **all three domains** are `EnrichmentStatus='Complete'` can the user trigger `onboarding-orchestrator` **from the UI**. Requirements: production-safe, end-to-end tracking (UI → workflows → SQL), transparency, privacy.

Decisions confirmed with the user:
- **Replace the UI's legacy pipeline entirely** (old blob-storage submit/approve/status flow with dead SAS URLs is removed).
- **All 3 domains required** before launch — no changes to `onboarding-orchestrator` (it always fans out to all three).
- **UI stays a local HTML file for now** → Logic App CORS `*` during testing (tighten later when hosted).

Key facts discovered (drive the design):
- `SolaceClient`/`MuleSoftPartner` already store ALL GitHub publish metadata (RepoOwner/RepoName/FilePath(Prefix)/Branch/BaseBranch/FeatureBranchName/RequesterEmail/RecipientEmail/CommitMessage) — the full orchestrator payload is buildable from SQL alone.
- The orchestrator's `mulesoft.generate`/`solace.generate` sections need `csvContent`. Both C# parsers (`Services/CsvParser.cs`, `Services/MuleSoftCsvParser.cs`) consume a long, one-row-per-item, 26-column CSV that maps **1:1 onto the child tables** — CSV is reconstructable from SQL.
- `mulesoft-config-generation` contains a human-in-the-loop Teams wait (feature-branch approval) → orchestrator runs can take hours. The UI launch must be **respond-202-then-continue**, never synchronous.
- The "one onboarding" grouping key across the 3 unrelated natural keys is a **shared `correlationId`** — UI generates one per onboarding session and sends it on all three domain submissions; every table + the audit log already store it.
- A concrete working orchestrator payload exists at `postman/3PL-Automation-LogicApps.postman_collection.json` ("Onboarding Orchestrator" request) — copy that shape exactly, filling values from SQL.

## Architecture

```
UI (3pl-onboarding.html, local file)
 ├─ POST data-enrichment          (per domain, shared correlationId)  [exists — UNCHANGED]
 ├─ POST enrichment-status        (poll every 10s)                    [NEW workflow]
 └─ POST onboarding-launcher      (gated launch)                      [NEW workflow]
                                     └─ invokes onboarding-orchestrator [exists — UNCHANGED]
```
`data-enrichment`, `data-enrichment-notifier`, `data-enrichment-mail-intake`, `onboarding-orchestrator`, and the 5 config child workflows are **not modified**.

## 1. SQL — `threeplspace/sql/schema.sql` (idempotent ALTERs, one statement per `GO` batch — same-batch column reference is a known trap)

- Extend the `EnrichmentAuditLog.EventType` CHECK constraint: drop + recreate (guard via `sys.check_constraints`) adding `'OrchestrationStarted','OrchestrationSucceeded','OrchestrationFailed'`. Verify whether `Domain` has a CHECK; if not, launcher audit rows use `Domain='Orchestrator'` as-is.
- Add nonclustered indexes `IX_BtpConfig_CorrelationId`, `IX_SolaceClient_CorrelationId`, `IX_MuleSoftPartner_CorrelationId` (status/launcher query by CorrelationId).
- No new grants: `threepl-logicapp-svc` already has SELECT/UPDATE on parents and INSERT on the audit log.

## 2. New C# local functions — `threeplspace/threepllocalfunction/Functions/`

Mirror the `[WorkflowActionTrigger]` pattern of `TriggerBtpDeploymentFunction.cs`. Escaping must be the exact inverse of `Shared/CsvLineSplitter.cs` (read it first); reject/strip embedded newlines in values (parsers split on `\n`).

- **`BuildSolaceCsvFunction.cs`** — input: parent-row JSON + `messageTypes` rows array (from SQL). Output `{ csvContent }`: header + one row per SolaceMessageType row in the exact 26-column order documented in `CsvParser.cs:42-51` (record-level columns on the first row of the group, per-messageType queue fields per row). SQL BIT → `true`/`false` strings; `EncryptedPassword` passed through untrimmed (index 4 is preserved exactly by the parser).
- **`BuildMuleSoftCsvFunction.cs`** — input: parent row + 5 child arrays. Output `{ csvContent }`: header per `MuleSoftCsvParser.cs:38-43`, identity/base columns from the parent row, then one row per child with the right `RowType` (`Environment`/`TransactionType`/`MessageType`/`SourceDestination`/`UomMapping`). Throw a clear error if there are zero child rows (parser requires ≥1 data row).
- Optional shared helper `Shared/CsvFieldEscaper.cs` if quoting logic is nontrivial.

## 3. New workflow — `three3pllogicapp/enrichment-status/workflow.json` (Request trigger, read-only)

Input `{ correlationId }`. House patterns apply throughout: `@concat(...)` + `replace(v,'''','''''')` query building (NO queryParameters), `coalesce(body('X')?[0], body('X'), json('[]'))` guard before any `length()`/`first()`, exponential retryPolicy on every SQL action, Scope-Try/Scope-Catch with audit `Error` (channel System) + Response 500.

1. Validate correlationId non-empty → else 400.
2. Query each parent table by CorrelationId — **explicit column list, `EncryptedPassword` excluded** (privacy: it must never reach the browser).
3. Query `EnrichmentAuditLog` TOP 100 by CorrelationId ordered by CreatedAt, **masking ActorEmail in T-SQL** (`CONCAT(LEFT(ActorEmail,2),'***',SUBSTRING(ActorEmail,CHARINDEX('@',ActorEmail),320))`).
4. Per domain, Compose: `found`, `enrichmentStatus`, `deploymentStatus`, `cardSentAt/cardRespondedAt`, and `missingFields` — mirroring the exact required-field lists from `data-enrichment`'s `Compose_{Domain}_Enrichment_Status` expressions (e.g. Btp: mode, developerId, title, repoOwner, repoName, workflowFileName, branchRef, serviceExists — see `data-enrichment/workflow.json:459`).
5. Response 200: `{ correlationId, readyToLaunch (all 3 found && Complete), btp{}, solace{}, mulesoft{}, auditTrail[] }`.

## 4. New workflow — `three3pllogicapp/onboarding-launcher/workflow.json` (Request trigger, async launch)

Input `{ correlationId, launchedBy?, forceRedeploy? }`. Same house patterns as above.

1. Audit `Received` (channel Api, Domain 'Orchestrator').
2. Fetch all 3 parent rows by CorrelationId + all child rows (SolaceMessageType by ClientId; the 5 MuleSoft child tables by PartnerId).
3. **Server-side gate** (never trust the UI's enabled button): all 3 rows exist; all `EnrichmentStatus='Complete'`; no row `DeploymentStatus='InProgress'` (blocks double-launch); if any row `Deployed`, require `forceRedeploy=true`. Gate failure → audit + **Response 409** with per-domain reasons.
4. UPDATE all 3 rows `DeploymentStatus='InProgress'` + audit `OrchestrationStarted`.
5. **Response 202** `{ correlationId, status: 'OrchestrationStarted' }` — respond-then-continue; the run keeps going after the Response action.
6. InvokeFunction `BuildSolaceCsv` + `BuildMuleSoftCsv` with the fetched rows.
7. Compose the orchestrator payload exactly per the Postman collection shape: `btp{...}` straight from BtpConfig columns; `mulesoft.generate/publish` and `solace.generate/publish` from the parent-row metadata columns + the built `csvContent` (copy `updateExisting`/`existing*Yaml` defaults from the Postman example).
8. Invoke `onboarding-orchestrator` (`"type":"Workflow"`, `host.workflow.id`) — long-running is fine post-202.
9. On Succeeded: UPDATE 3 rows `Deployed` + audit `OrchestrationSucceeded` (EventDetail = trimmed response). On Failed (runAfter Failed / Scope-Catch): UPDATE 3 rows `Failed` + audit `OrchestrationFailed` with orchestrator error body (its 500 includes per-domain `result()` details) so failures are debuggable from the audit trail alone.

## 5. UI rework — `3PL-Automation-LogicApp-Workflow\3PL-Automation\ui\3pl-onboarding.html` (in place, keep the visual style/CSS)

**Remove**: `PIPELINE_ENDPOINTS`, dead `AZ_ENDPOINTS`, `generateConfig()` and the whole legacy Solace-hub form/partner seed data, Architect-approval view, blob-status polling.

**Add three views** (reusing the existing role-tab pattern):
- **Onboarding Intake**: "New onboarding" generates `correlationId = ui-<slug>-<timestamp>`, shown as a badge and stored in localStorage (recent list). Three domain tabs — field sets exactly matching the trigger contracts in `data-enrichment/samples/*-complete.json` (Btp: 14 flat fields; Solace: flat fields + dynamic `messageTypes[]` row table; MuleSoft: flat fields + 5 dynamic child-row tables). Per-domain **Save** → POST `data-enrichment` with `domain` + shared `correlationId`; display returned `enrichmentStatus` (partial fill is fine — AwaitingInput hands off to the notifier/card/mail flows). `encryptedPassword` is `type=password`, never echoed back, never stored in localStorage.
- **Status & Tracking**: pick a correlationId → poll `enrichment-status` every 10s. Per-domain cards: EnrichmentStatus, missing-field chips, CardSentAt/RespondedAt, DeploymentStatus; below, the audit timeline (event, channel, masked actor, time). Stop polling on terminal states.
- **Launch**: button enabled only when `readyToLaunch` (client convenience; server re-gates). POST `onboarding-launcher`; on 202 switch to status polling to watch DeploymentStatus InProgress→Deployed/Failed; on 409 render the per-domain reasons.

**Endpoint config**: one `const API = { dataEnrichment:'<paste SAS URL>', enrichmentStatus:'<...>', onboardingLauncher:'<...>' }` block at the top with placeholder values — real SAS-signed invoke URLs are pasted locally only, **never committed** (this repo already had a GitGuardian secret-leak incident).

## 6. Deployment / config steps (manual, documented at the end of implementation)

1. Run updated `schema.sql` in the Query Editor (new EventTypes + indexes).
2. Build/deploy `threepllocalfunction` (2 new functions) and the 2 new workflows — **copy full Code-view JSON into the portal** (established workaround for the local-file sync/reversion issue).
3. Portal → Logic App Standard → CORS: allow `*` (documented as dev-only; tighten to the hosted origin later).
4. Copy the 3 workflows' SAS invoke URLs from the portal into the UI's `API` block locally.

## Validation (before any deploy)

- `validate_json.py`, `check_scope.py`, `check_runafter_dag.py` (scratchpad) against both new workflow JSONs.
- CSV round-trip check: feed `BuildSolaceCsv`/`BuildMuleSoftCsv` output into `CsvParser`/`MuleSoftCsvParser` and assert the parsed records match the input rows (small test harness; catches escaping mismatches before they hit GitHub-publishing workflows).
- `dotnet build` on the functions project.

## End-to-end verification

1. UI: new onboarding → submit complete BTP + complete MuleSoft + **incomplete** Solace under one correlationId → Status view shows Solace `AwaitingInput` with missing-field chips; Launch disabled; audit shows 3× Received/Upserted (Api).
2. Complete Solace (answer the Teams card, or re-save from UI) → status flips `Complete`, `readyToLaunch=true`, Launch enables.
3. Launch → 202 → DeploymentStatus `InProgress` → orchestrator completes → `Deployed` (or `Failed` with the orchestrator error visible in the audit timeline).
4. Click Launch again mid-run → 409 InProgress (double-launch guard).
5. Unknown correlationId in status view → clean "not found" per domain, `readyToLaunch=false`, no 500.
6. Audit timeline reads end-to-end: Received → Upserted → CardSent → CardResponded → OrchestrationStarted → OrchestrationSucceeded, with channels Api/AdaptiveCard/System and masked emails.

## Files touched

| File | Change |
|---|---|
| `threeplspace/sql/schema.sql` | EventType CHECK extension + 3 CorrelationId indexes |
| `threeplspace/threepllocalfunction/Functions/BuildSolaceCsvFunction.cs` | new |
| `threeplspace/threepllocalfunction/Functions/BuildMuleSoftCsvFunction.cs` | new |
| `threeplspace/three3pllogicapp/enrichment-status/workflow.json` | new |
| `threeplspace/three3pllogicapp/onboarding-launcher/workflow.json` | new |
| `3PL-Automation-LogicApp-Workflow/3PL-Automation/ui/3pl-onboarding.html` | rework in place |

Explicitly unchanged: `data-enrichment`, `data-enrichment-notifier`, `data-enrichment-mail-intake`, `onboarding-orchestrator`, all 5 config child workflows, `connections.json`.
