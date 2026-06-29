# 3PL onboarding Logic App workflows — Logic App (Standard)

> **Cost note:** Logic Apps Standard bills a flat ~$150–170/month for its hosting plan whether or
> not anything runs. None of the 4 workflows below need a Standard-only feature, so
> [`consumption/`](consumption/) has the same 4 workflows reshaped as Logic Apps **Consumption**
> resources (billed per action, no idle cost) — read that folder's README for the trade-offs (HTTP
> fan-out instead of native child-workflow calls, immediate-ack instead of a final response) and
> deployment/cutover steps. The Standard docs below stay accurate for the one workflow already
> live; treat Consumption as the default for anything not yet deployed.

**Lives in:** the pre-existing `3pl-automation` Logic App (Standard) resource in `ODL-IBM-2262033`
(region `canadacentral`). This app now hosts 4 workflows:

- `3pl-onboarding-orchestrator-workflow` — **the main workflow.** Accepts `{from, subject, body,
  platforms[]}` and fans out to 1, 2, or all 3 of the per-platform sub-workflows below by invoking
  them as child workflows in this same app (native "Workflow" action, not an HTTP call to their
  trigger URL — see "Master orchestrator" below for why). Responds `202 DISPATCHED` immediately
  after starting the requested children; it does not wait for any of them to finish, since each
  one can block for hours on its own Teams approval card.
- `solace-mail-trigger-workflow` — Solace sub-workflow (JSON config; new domains and updates to
  existing ones), backed by the `integration-pulse-solace-publisher` agent.
- `mulesoft-mail-trigger-workflow` — MuleSoft sub-workflow (up to 4 YAML files: app/dev/tst/prod;
  new onboardings and updates to existing ones), backed by the
  `integration-pulse-mulesoft-publisher` agent.
- `btp-mail-trigger-workflow` — BTP sub-workflow (btp_config.yaml and/or manifest.yml; new
  onboardings and updates to existing ones), backed by the `integration-pulse-btp-publisher` agent.

All 3 sub-workflows are structurally identical — same trigger shape, same 4-action pattern, same
shared Function App and GitHub tools — they only differ in which agent/routes/file count they use.
The orchestrator above is the only piece that's structurally different (router, not a
generate/approve/publish pipeline itself).

All 3 per-platform workflows share the same shape: `Generate_*` (calls the matching
`/api/{platform}-generate` route) → `Post_adaptive_card_and_wait_for_a_response` (Teams
Approve/Reject) → `Decision_check` (Approve → `Publish_to_GitHub` calling `/api/{platform}-publish`;
Reject → no-op) → `Response_Final_Status` (so the orchestrator — or a direct manual test call — gets
a structured `{platform, decision, publishResult}` result). They can each also be called directly
with their own trigger URL for standalone testing (see "Manual test" below) — the orchestrator is an
optional fan-out layer on top, not the only way to invoke them.

## Master orchestrator

`3pl-onboarding-orchestrator-workflow` is a fire-and-acknowledge router: its 3 `Dispatch_*` `If`
actions each run in parallel directly off the trigger (no `runAfter` dependency between them) and
invoke that platform's mail-trigger workflow via the Standard Logic Apps native child-workflow
action (`"type": "Workflow"`, `host.workflow.id`) rather than a plain `Http` POST to the child's
trigger URL — the native child-workflow action is expected to mark the parent action `Succeeded`
once the child run is *started*, not once it *finishes*, which is what makes the immediate
`Response_Dispatched` (202) possible despite each child blocking on Teams approval afterwards.
**This one behavior — exactly when Standard Logic Apps marks a native child-workflow invocation
action as complete — should be confirmed against the live Designer/runtime once deployed**; if it
turns out to block until the child fully finishes, the orchestrator will need reshaping (e.g. calling
each child's trigger URL with `respond immediately` semantics instead). Everything else in this
workflow (the `If` conditions, the env var-free body passthrough, the response shape) doesn't
depend on that detail.

## Current state

- ✅ **`solace-mail-trigger-workflow.json` deployed and live**, in the correct Standard format
  (`{"definition": {...}, "kind": "Stateful"}`) — trigger is a plain **HTTP Request trigger**
  (`manual`, schema `{from, subject, body}`). Its 4 actions (`Generate_Solace_JSON` →
  `Post_adaptive_card_and_wait_for_a_response` → `Decision_check` → `Publish_to_GitHub`, plus the
  new terminal `Response_Final_Status`) match this folder's copy except for the
  `Response_Final_Status` addition, which still needs deploying (see step 6 below).
- 🆕 **`mulesoft-mail-trigger-workflow.json`, `btp-mail-trigger-workflow.json`,
  `3pl-onboarding-orchestrator-workflow.json` are new** — written in this folder but not yet deployed
  to the live app at all (no `mulesoft-mail-trigger-workflow`/`btp-mail-trigger-workflow`/
  `3pl-onboarding-orchestrator-workflow` workflow exists in `ODL-IBM-2262033` yet).
- ✅ `Microsoft.Web/connections/gmail` and `.../teams` exist in `ODL-IBM-2262033`. `gmail` is unused
  by the 4 workflows *in this folder* — **but do not delete it.** A separate, already-live,
  standalone Consumption Logic App named `solace-mail-trigger` (its own resource, not one of these
  4) polls Gmail every minute via this exact connection and feeds the real production Solace flow.
  Deleting `gmail` breaks that. `teams` is used by each platform's approval-card action here and
  needs authenticating (not yet done as of this writing).
- ⏸️ **Everything below is yours to finish manually** — `connections.json` and `parameters.json` in
  this folder are scaffolds, not yet deployed; the same `teams` connection is shared by all 3
  platforms' approval cards plus the orchestrator (which needs no connection of its own — it only
  does HTTP-free native child-workflow calls).

## Remaining manual steps

1. **Authenticate the `teams` connection.** Portal → Resource group `ODL-IBM-2262033` → resource
   `teams` (type "API Connection") → **Edit API connection** → sign in. Leave `gmail` alone — see
   the warning above, it's load-bearing for the live `solace-mail-trigger` Consumption Logic App.

2. **Get the connection's key and runtime URL** (only works once step 1 is done):
   ```bash
   az rest --method POST --url "https://management.azure.com/subscriptions/a556a28c-c14a-4657-b59f-e0c80f0bb54c/resourceGroups/ODL-IBM-2262033/providers/Microsoft.Web/connections/teams/listConnectionKeys?api-version=2016-06-01" \
     --body '{"validityTimeSpan": "7"}'
   ```
   This returns `{"connectionKey": "...", "connectionRuntimeUrl": "..."}` (or similar — the exact
   response shape wasn't verified live since the connection isn't authenticated yet).

3. **Store the key as an app setting** (referenced by `connections.json`'s `@appsetting(...)`, never
   inlined directly):
   ```bash
   az functionapp config appsettings set --name 3pl-automation -g ODL-IBM-2262033 \
     --settings teams-connectionKey="<connectionKey from step 2>"
   ```

4. **Fill in `connections.json`** in this folder — replace the `teams` entry's
   `connectionRuntimeUrl` placeholder with the real value from step 2 (the `connection.id`/`api.id`
   are already correct). The `gmail` entry in this file is unused by these 4 workflows and can stay
   as a scaffold or be removed from this file — that's unrelated to the actual `gmail` *connection
   resource* in Azure, which must not be deleted (see warning above).

5. ✅ Done — all 3 mail-trigger workflows' `Post_adaptive_card_and_wait_for_a_response` actions now
   post to the same Teams **group chat** (`recipient.recipient`:
   `19:397123a1657a4e0b85d6b48fc68bd89e@thread.v2`), using the real Designer-exported connector
   shape (`host.connection.referenceName`, `body.notificationUrl` + nested `body.body`, `path`
   ending in `/location/.../$subscriptions`) — see the Confidence note below for what changed. If a
   different chat/team/channel should receive approvals, update that one `recipient` value (and the
   `location` segment of `path` if switching away from "Group chat") in all 3 files.

6. **Deploy `connections.json`, `parameters.json`, and the 4 workflow files** via the same Kudu VFS
   pattern (`PUT` with header `If-Match: *`):
   ```bash
   TOKEN=$(az account get-access-token --resource "https://management.azure.com" --query accessToken -o tsv)
   curl -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -H "If-Match: *" \
     --data-binary @connections.json \
     "https://3pl-automation-a2gzhsecbjadb6ew.scm.canadacentral-01.azurewebsites.net/api/vfs/site/wwwroot/connections.json"
   # same for parameters.json (app root), then for each workflow file PUT to
   # .../api/vfs/site/wwwroot/<workflow-name>/workflow.json — re-PUT solace-mail-trigger-workflow.json
   # too, since this folder's copy now includes the Response_Final_Status addition; the 3 new
   # workflows (mulesoft-mail-trigger-workflow, btp-mail-trigger-workflow,
   # 3pl-onboarding-orchestrator-workflow) get their FIRST deploy here.
   ```
   Then restart the app (`az functionapp restart --name 3pl-automation -g ODL-IBM-2262033`) so the
   runtime picks up the new connection and workflows.

7. **Provision 2 new Cosmos containers** in the existing Cosmos NoSQL account (`mulesoft_requests`,
   `btp_requests`, partition key `/tenantId`, matching the existing `solace_requests` container) —
   the new `/api/mulesoft-*`/`/api/btp-*` routes in `mcp-server/function_app.py` will fail without
   them. See the root `README.md`'s env var table for the container-name override env vars.

8. **Set the 2 new MCP endpoint app settings** on the Function App hosting `mcp-server/`
   (`ip-3pl-mcp`, separate from this Logic App resource) — `MULESOFT_MCP_ENDPOINT` and
   `BTP_MCP_ENDPOINT`, both pointing at that same Function App's `/api/mulesoft-mcp`/`/api/btp-mcp`
   routes (parallel to the existing `SOLACE_MCP_ENDPOINT` → `/api/solace-mcp`).

9. **Complete the root `README.md`'s "One-time setup"** on the `ip-3pl-mcp` Function App before any
   platform's publish phase will work: push the 3 persistent feature branches
   (`solace/onboarding`, `mulesoft/onboarding`, `btp/onboarding`) and set up GitHub auth for
   `mcp-server/lib/github_client.py` — live today via a `GITHUB_TOKEN` PAT App Setting; see that
   section for the GitHub App + Key Vault path it falls back to once Key Vault access is granted.

10. **Get each workflow's callable URL** once the app is back up:
   ```bash
   az rest --method POST --url "https://management.azure.com/subscriptions/a556a28c-c14a-4657-b59f-e0c80f0bb54c/resourceGroups/ODL-IBM-2262033/providers/Microsoft.Web/sites/3pl-automation/hostruntime/runtime/webhooks/workflow/api/management/workflows/<workflow-name>/triggers/manual/listCallbackUrl?api-version=2022-03-01"
   ```
   For a single-platform standalone test, `POST` to a mail-trigger workflow's own URL with
   `{"from": "...", "subject": "...", "body": "..."}`. For a real multi-platform onboarding, `POST`
   to the orchestrator's URL with `{"from": "...", "subject": "...", "body": "...", "platforms":
   ["solace", "mulesoft"]}` (or any subset/all 3).

## What each per-platform workflow does

1. **manual** (HTTP trigger) — accepts `{from, subject, body}` via POST (called directly, or by the
   orchestrator's native child-workflow invocation).
2. **Generate_{Solace_JSON,Mulesoft_YAML,BTP_Config}** — `POST /api/{platform}-generate` with the
   trigger body. The agent behind this route reads the target file(s) first to tell a brand-new
   config apart from an update to one that already exists, and merges into the real current
   content for an update rather than overwriting it blind. Returns `{id, <config>, branchName,
   <filePath(s)>, summaryForApproval}` — `<filePath(s)>` may be a subset (e.g. just one of
   MuleSoft's 4 files) when only part of an existing config is changing.
3. **Post_adaptive_card_and_wait_for_a_response** — shows the request in a redesigned, structured
   Teams card (header strip with a platform-colored icon, a `FactSet` with requester/subject/file
   metadata, the summary, and the generated config in its own shaded monospace block), pauses the
   run until Approve or Reject is clicked. This is the only gate between any request — new config
   or update — and GitHub; nothing reaches step 4 without it.

   The card also carries one editable field — **`branchName`**, an `Input.Text` pre-filled with the
   agent's suggested branch (normally the platform's persistent `solace/onboarding` /
   `mulesoft/onboarding` / `btp/onboarding`) but editable by whoever is approving. Adaptive Card
   `Action.Submit` merges every `Input.*` value into the same `data` object as the button's own
   `decision` field, so the value the approver leaves in that box at the moment they click
   Approve/Reject — not the value the Generate step originally suggested — is what flows to step 4.
   **The target branch must already exist in the repo** — nothing in this flow (or the GitHub tools
   behind `/api/{platform}-publish`) creates branches, so typing in a branch that doesn't exist yet
   will fail at the commit step.
4. **Decision_check** — if `decision == "approve"`, calls `POST /api/{platform}-publish` with the
   config/filePath(s) from step 2 plus `branchName` taken from the card response in step 3
   (falling back to step 2's suggested branch only if the field somehow comes back empty) — this is
   what actually commits the file(s) to that branch and ensures a PR is open from it to `main`. On
   reject, the run just ends without touching GitHub.
5. **Response_Final_Status** — always runs (`Succeeded`/`Failed`/`Skipped` from `Decision_check`),
   returns `{platform, decision, publishResult}` — read by a direct test caller, or ignored by the
   orchestrator (which has already responded to its own caller by this point).

## Confidence note

The Teams action's `path`/`body` shape was originally reconstructed from the general connector
pattern, not live-validated. It's since been replaced with the real Designer-exported shape for
"Post adaptive card and wait for a response" targeting a **Group chat** recipient:
`host.connection.referenceName` (not the Consumption-style `$parameters('$connections')`
indirection), a `body.notificationUrl`/`body.body` wrapper (`body.body.messageBody` holds the
Adaptive Card, `body.body.updateMessage` replaces the old `shouldUpdateCard` flag, `body.body.recipient.recipient`
holds the chat ID), and a `path` ending `/location/@{encodeURIComponent('Group chat')}/$subscriptions`.
This is now consistent across all 3 mail-trigger workflows.
