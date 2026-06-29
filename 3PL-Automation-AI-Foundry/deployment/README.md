# Deployment — what's live, how to re-run it, and what's still manual

This folder is the record of the real deployment of this repo against the actual Azure
subscription + AI Foundry project (not a fresh/clean environment — an existing sandbox with its
own pre-existing resources, agents, and one already-working Solace pipeline). `deploy.py`
encodes every step taken, idempotently, so this can be re-run later (after further code changes,
or to reproduce this in another resource group) without repeating the discovery work below.

## Real resource identities (discovered, not assumed)

| What | Real value |
|---|---|
| Subscription | `Sandbox AI DS - 1003669` (`a556a28c-c14a-4657-b59f-e0c80f0bb54c`) |
| Resource group | `ODL-IBM-2262033` |
| Function App | **`ip-3pl-mcp`** (`ip-3pl-mcp.azurewebsites.net`) — renamed from the original `ip-solace-mcp` once the app outgrew Solace-only scope. Azure Web Apps can't be renamed in place, so this was done as create-new + cutover + delete-old (see "Function App rename" below), not an in-place rename. |
| Function App plan | `EastUSLinuxDynamicPlan` (Consumption, Python 3.11, Linux) |
| AI Foundry project | `integration-pulse-found-resource` / project `integration-pulse-proj`, endpoint `https://integration-pulse-found-resource.services.ai.azure.com/api/projects/integration-pulse-proj` |
| Foundry model | `gpt-4.1` (already deployed on the Foundry resource) |
| Cosmos account / db | `integration-pulse-nosql` / `integration-pulse` (provisioned throughput, **not** serverless — 400 RU/s per container, matching the pre-existing `solace_requests` container) |
| GitHub repo | `soubhik7/3pl-automation` (public, default branch `main`) |
| Logic Apps region | `canadacentral` — **must** match the `teams` API connection's region (see Gotcha #2 below) |
| `teams` connection | `/subscriptions/.../resourceGroups/ODL-IBM-2262033/providers/Microsoft.Web/connections/teams` — exists, **not yet authenticated** |

## What's actually deployed now

- `ip-3pl-mcp` is running the current `mcp-server/` code (all 6 HTTP routes + 3 MCP routes +
  the `github_get_file`/`github_commit_file`/`github_open_pull_request` tool set).
- 3 persistent branches exist on GitHub: `solace/onboarding`, `mulesoft/onboarding`,
  `btp/onboarding` (empty, off `main`).
- Cosmos containers `solace_requests` (pre-existing), `mulesoft_requests`, `btp_requests` all
  exist in `integration-pulse` db.
- All 3 platform agents are registered in the Foundry project: `integration-pulse-solace-publisher`
  (upgraded to v2 — new-vs-update logic via `github_get_file`, persistent branch),
  `integration-pulse-mulesoft-publisher` (new), `integration-pulse-btp-publisher` (new). All 6
  generate/publish workflows registered alongside them.
- 4 new Consumption Logic Apps in `canadacentral`: `solace-mail-trigger-workflow`,
  `mulesoft-mail-trigger-workflow`, `btp-mail-trigger-workflow`, and
  `3pl-onboarding-orchestrator-workflow` (wired to the 3 mail-trigger workflows' real trigger
  callback URLs).
- All 3 `-generate` routes smoke-tested end-to-end through the real Foundry agents (Phase 1 only —
  see "Intentionally not automated" below).

## Function App rename (`ip-solace-mcp` → `ip-3pl-mcp`)

The shared MCP Function App outgrew its Solace-only name once it started hosting the MuleSoft
and BTP routes too, so it was renamed. Azure Web Apps can't be renamed in place, so this was a
create-new + cutover + delete-old, not an in-place rename:

1. Created a new Function App `ip-3pl-mcp` (same plan `EastUSLinuxDynamicPlan`, same storage
   account `integrationpulsestore`, same Linux/Python 3.11 stack) plus its own dedicated
   `ip-3pl-mcp` Application Insights component, alongside the still-running `ip-solace-mcp`.
2. Copied every app setting across (`GITHUB_TOKEN`, `GITHUB_OWNER`/`GITHUB_REPO`/`GITHUB_BASE_BRANCH`,
   `COSMOS_*`, `FOUNDRY_PROJECT_ENDPOINT`) and pointed `APPLICATIONINSIGHTS_CONNECTION_STRING` at
   the new component.
3. Zip-deployed `mcp-server/` to the new app and health-checked it before touching anything live.
4. Re-pointed every real consumer at the new hostname: the 3 Consumption mail-trigger workflows
   (`solace-mail-trigger-workflow`, `mulesoft-mail-trigger-workflow`, `btp-mail-trigger-workflow`),
   **and** the standalone `solace-mail-trigger` Gmail-triggered Logic App (see below — its 2 HTTP
   actions hit the Function App directly), and re-registered the 3 Foundry agents so
   `SOLACE_MCP_ENDPOINT`/`MULESOFT_MCP_ENDPOINT`/`BTP_MCP_ENDPOINT` resolve to `ip-3pl-mcp`.
5. Only deleted the old `ip-solace-mcp` app (+ its Application Insights component) after the new
   one was verified healthy and every known consumer was re-pointed.

Platform-specific names were deliberately left alone — `agent/solace/`, the `solace_requests`
Cosmos container, the `solace/onboarding` branch, the `solace-mcp` MCP route, etc. all still mean
"the Solace platform," which is a different thing from "the shared hub," so they don't get
renamed to `3pl`.

## What's deliberately untouched

- **The standalone `solace-mail-trigger` Consumption Logic App** (no `-workflow` suffix) — this
  is a *different*, already-live resource that polls a Gmail inbox every minute (`When a new
  email arrives`) and feeds the real, working Solace pipeline today. It is not part of this
  repo's 4 manual-HTTP-trigger workflows and was not modified. **Do not delete the `gmail` API
  connection** — it's load-bearing for this resource.
- **The `solace-mail-trigger-workflow` *inside* the Standard `3pl-automation` Logic App** — a
  second, apparently-redundant copy of the Solace flow living in a Standard-tier Logic App.
  Left alone; not investigated further; a candidate for cleanup later but out of scope here.
- **The other ~8 Foundry agents and 5+ workflows already in this project** (`integration-pulse-provisioner`
  and friends) — a completely separate "Builder → Provisioner" pattern-deployment system.
  `register.py` only ever touches the 3 names it's hardcoded to use.
- **Key Vault** — both `integration-pulse-kv` and `integratkeyvaultc82f9d17` block secret
  read/write for every identity tested here, including subscription Owner (re-confirmed live
  during the Function App rename above — still `Forbidden` on `secrets/readMetadata` for the
  current sandbox identity). This looks like a deliberate sandbox guardrail, not a
  misconfiguration. Net effect: the GitHub App + Key Vault auth path in
  `mcp-server/lib/github_client.py` is fully coded and ready, but cannot be finished from this
  environment — the existing `GITHUB_TOKEN` app setting (PAT) was carried over to `ip-3pl-mcp`
  as-is rather than blocking the rename on it. See "Remaining manual steps".

## Intentionally not automated

`deploy.py` never calls a `-publish` route and never will. Publishing commits a real file and
opens a real PR against `soubhik7/3pl-automation`'s `main` branch — that should only ever happen
after an actual human clicks Approve on a real Teams card, which is exactly the gate this whole
project exists to enforce. The smoke test only exercises `-generate` (Cosmos write only, no
GitHub write).

## Remaining manual steps (need a human, in a browser)

1. **Authenticate the `teams` API connection.** Portal → `ODL-IBM-2262033` → `teams` resource →
   Edit API connection → sign in. Until this is done, the 4 new Logic Apps will run Phase 1
   (generate) fine but fail at "Post adaptive card and wait for a response."
2. **Replace the `groupId`/`channelId` placeholders** in the 3 mail-trigger workflows' Adaptive
   Card action with a real Team/Channel (Designer → pick from the connector's picker UI, then
   redeploy via `deploy.py logic-apps` or the Designer's Save).
3. **GitHub App migration (optional, deferred by choice).** `github_client.py` already supports
   GitHub App auth as a fallback — it only activates once `GITHUB_TOKEN` is unset *and*
   `GITHUB_APP_ID`/`GITHUB_APP_INSTALLATION_ID`/`GITHUB_APP_PRIVATE_KEY_VAULT_URL` are set. Since
   Key Vault is blocked here (see above), this deployment intentionally kept the existing PAT
   (`GITHUB_TOKEN` app setting, already live) rather than block on it. To migrate later: create
   the GitHub App in the GitHub UI, grant it `contents:write`+`pull_requests:write` on this repo,
   put its private key in a Key Vault some identity *can* actually write to, set the 3 env vars,
   then unset `GITHUB_TOKEN`. No code changes needed — `_get_auth_token()` in `github_client.py`
   already prefers the PAT and falls back to the App.

## Re-running `deploy.py`

```bash
cd 3PL-Automation-AI-Foundry
python3 deployment/deploy.py all              # every step, idempotent
python3 deployment/deploy.py function-app     # just redeploy mcp-server/ after a code change
python3 deployment/deploy.py agents           # just re-register agents after a prompt change
python3 deployment/deploy.py smoke-test       # just confirm the 3 -generate routes still work
```

Requires: `az` CLI logged into the right subscription, `requests`+`pyyaml` importable (for
`agent/register.py`), and either `GITHUB_TOKEN` in the environment or the existing
`GITHUB_TOKEN` app setting on `ip-3pl-mcp` (the script reads that as a fallback — it never
prints the value).

Every step checks-before-creating: branches/containers/Logic Apps already present are skipped,
the orchestrator is the only thing redeployed unconditionally (cheap — it just refreshes the 3
callback URLs, which can legitimately change if a mail-trigger workflow is ever redeployed).

## Gotchas hit during the real deployment (why the script looks the way it does)

1. **A failed remote pip install takes down every route, not just the new ones.** The first
   `mcp-server/` deploy installed everything except `azure-ai-projects` (Oryx build hiccup, not
   a real dependency conflict — confirmed via PyPI that the version constraints were all
   satisfiable). Because `function_app.py` imports it at module scope, the whole Functions host
   indexed **0 functions**, so even the previously-working `solace-generate`/`solace-mcp` routes
   404'd for a few minutes. A second, identical deploy attempt succeeded. `step_function_app()`
   automates this: it deploys, health-checks via a real `solace-mcp` call, and retries once
   automatically if the health check fails, instead of trusting the deploy command's own exit
   code (which was 0 — "succeeded" — both times).
2. **A Logic App can't resolve an API connection in a different region.** The resource group's
   default location is `centralus`; the `teams` connection lives in `canadacentral`. The first
   deploy attempt used `[resourceGroup().location]` and failed with `ApiConnectionNotFound`
   despite the connection existing. Fixed by hardcoding `canadacentral` in every Consumption
   template — see `LOGIC_APP_LOCATION` in `deploy.py`.
3. **`az rest`'s exit code doesn't mean the response body was a success.** Capturing
   `listCallbackUrl`'s output via `2>&1` once silently captured a `ResourceNotFound` error
   message instead of a real URL (because no Consumption `solace-mail-trigger-workflow` existed
   yet at that point) and nearly got wired into the orchestrator as a "callback URL." `deploy.py`
   parses the JSON and checks for a real `https://`-prefixed `value` before using it anywhere.
4. **`register.py`'s template path was wrong for this repo's actual folder name.** It computed
   `templates/` as two directories up plus a literal `3pl-automation` segment — an assumption
   that doesn't match this folder's real name (`3PL-Automation-AI-Foundry`). Fixed to
   `_HERE.parent / "templates"` (a straightforward sibling of `agent/`).
5. **Bash associative arrays don't behave the same in zsh.** An early version of the manual
   callback-URL validation used `declare -A` and silently evaluated as false even when every
   individual entry was true. `deploy.py` avoids this entirely by doing real work in Python, not
   shell array bookkeeping.
