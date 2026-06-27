# 3PL Onboarding — End-to-End Flow Overview

This diagram reflects the multi-platform architecture: one master orchestrator fans out to three
independent, platform-specific generate → approve → publish pipelines (Solace, MuleSoft, BTP),
each backed by its own Azure AI Foundry agent and sharing one Azure Functions MCP server.
`HLD.drawio`/`LLD.drawio` in this same folder cover the identical architecture as draw.io
diagrams (component view and sequence-diagram view respectively) — MuleSoft and BTP, marked as
future scope (🚧) in the legacy `3PL-Automation/diagrams/3pl-automation-architecture.drawio` HLD,
are now fully implemented (✅) alongside Solace across all three diagrams.

```mermaid
flowchart TD
    A["Inbound onboarding request<br/>from, subject, body, platforms[]"] --> B["Logic App:<br/>3pl-onboarding-orchestrator-workflow"]

    B -->|platforms has 'solace'| C["Logic App:<br/>solace-mail-trigger-workflow"]
    B -->|platforms has 'mulesoft'| D["Logic App:<br/>mulesoft-mail-trigger-workflow"]
    B -->|platforms has 'btp'| E["Logic App:<br/>btp-mail-trigger-workflow"]
    B --> F["Respond 202 DISPATCHED<br/>(fire-and-acknowledge — children may block<br/>for hours on Teams approval)"]

    subgraph SOL[Solace pipeline]
        C --> C1["POST /api/solace-generate"]
        C1 --> C2["Agent: integration-pulse-solace-publisher<br/>phase=generate"]
        C2 --> C3["solaceJson + branchName='solace/onboarding' + filePath<br/>solace-automation/config/3pl/&lt;slug&gt;.json"]
        C3 --> C4["Cosmos: solace_requests (GENERATED)"]
        C3 --> C5["Teams card: Approve / Reject"]
        C5 -->|Approve| C6["POST /api/solace-publish"]
        C5 -->|Reject| CX["End — no GitHub change"]
        C6 --> C7["Agent phase=publish:<br/>commit_file -> ensure PR open<br/>(no branch-create step — solace/onboarding pre-exists)"]
        C7 --> C8["Cosmos: PUBLISHED + branch/commitUrl/prUrl"]
    end

    subgraph MULE[MuleSoft pipeline]
        D --> D1["POST /api/mulesoft-generate"]
        D1 --> D2["Agent: integration-pulse-mulesoft-publisher<br/>phase=generate"]
        D2 --> D3["app/dev/tst/prod YAML + branchName='mulesoft/onboarding'<br/>mulesoft-automation/config/&lt;slug&gt;/*.yaml"]
        D3 --> D4["Cosmos: mulesoft_requests (GENERATED)"]
        D3 --> D5["Teams card: Approve / Reject"]
        D5 -->|Approve| D6["POST /api/mulesoft-publish"]
        D5 -->|Reject| DX["End — no GitHub change"]
        D6 --> D7["Agent phase=publish:<br/>commit_file x4 -> ensure PR open<br/>(no branch-create step — mulesoft/onboarding pre-exists)"]
        D7 --> D8["Cosmos: PUBLISHED"]
    end

    subgraph BTP[BTP pipeline]
        E --> E1["POST /api/btp-generate"]
        E1 --> E2["Agent: integration-pulse-btp-publisher<br/>phase=generate"]
        E2 --> E3["btp_config.yaml + optional manifest.yml + branchName='btp/onboarding'<br/>btp-automation/config/&lt;slug&gt;/btp_config.yaml"]
        E3 --> E4["Cosmos: btp_requests (GENERATED)"]
        E3 --> E5["Teams card: Approve / Reject"]
        E5 -->|Approve| E6["POST /api/btp-publish"]
        E5 -->|Reject| EX["End — no GitHub change"]
        E6 --> E7["Agent phase=publish:<br/>commit_file x1-2 -> ensure PR open<br/>(no branch-create step — btp/onboarding pre-exists)"]
        E7 --> E8["Cosmos: PUBLISHED"]
    end

    C7 --> G[("GitHub soubhik7/3pl-automation<br/>3 persistent branches, 1 per platform<br/>each with an open PR against main")]
    D7 --> G
    E7 --> G
    G --> H["Existing downstream automation per platform<br/>(e.g. solace-automation GH Actions deploy-dev.yml on merge)<br/>— out of scope, unchanged"]
```

## Shared infrastructure (not platform-specific)

One Azure Functions app (`mcp-server/`, deployed as `ip-3pl-mcp.azurewebsites.net`) hosts all 3
platforms' MCP tool servers and HTTP routes:

```mermaid
flowchart LR
    MCP["function_app.py (shared)"]
    MCP --> R1["/solace-mcp"]
    MCP --> R2["/mulesoft-mcp"]
    MCP --> R3["/btp-mcp"]
    MCP --> R4["/solace-generate, /solace-publish"]
    MCP --> R5["/mulesoft-generate, /mulesoft-publish"]
    MCP --> R6["/btp-generate, /btp-publish"]
    R1 & R2 & R3 --> T1["github_commit_file"]
    R1 & R2 & R3 --> T2["github_open_pull_request"]
    T1 & T2 --> GH["lib/github_client.py — raw urllib, never raises"]
    GH --> APP["GitHub App installation token<br/>(no PAT) — private key in Key Vault,<br/>fetched via this Function App's managed identity"]
    R4 & R5 & R6 --> CW["lib/foundry_client.py — invoke_workflow (streaming, retry x3)"]
    R4 & R5 & R6 --> COS["lib/nosql_client.py — per-platform Cosmos container"]
```

## Key properties

- **Per-platform agents stay separate** — `integration-pulse-{solace,mulesoft,btp}-publisher` each
  have their own `agent.yaml` + `system-prompt.md`, because their output contracts genuinely
  differ (JSON for Solace, multi-file YAML for MuleSoft, YAML + optional manifest for BTP).
- **GitHub tools are shared** — `github_commit_file`, `github_open_pull_request` are the same 2
  implementations behind all 3 platforms' MCP routes. There is no branch-creation tool: each
  platform commits to one persistent feature branch (`solace/onboarding`, `mulesoft/onboarding`,
  `btp/onboarding`), created once manually (see root `README.md` "One-time setup"), never by an
  agent — every approved request is just another commit on that branch (one file per
  partner/slug, so concurrent requests never collide), and `github_open_pull_request` ensures a
  PR is open from it (idempotent — reuses the existing PR after the first request).
- **No PAT/long-lived token anywhere** — `lib/github_client.py` authenticates to GitHub as a
  GitHub App: its private key lives in Key Vault, fetched via this Function App's managed
  identity, exchanged for a short-lived (1 hour) installation token that's cached in memory and
  auto-refreshed. No human-managed token sits in an App Setting.
- **Human approval is mandatory** — no platform ever writes to GitHub without a Teams
  Approve click; Reject ends the pipeline with no side effects.
- **Cosmos is audit-only** — the approval flow's state lives in the Logic App run, not Cosmos;
  each platform's `{platform}_requests` container exists for traceability, not as a dependency
  for the approval flow to function.
- **The AI-Foundry layer's job ends at "commit added, PR open against main"** — actual
  provisioning into Solace Cloud / Anypoint Platform / BTP cockpit remains each platform's own
  existing automation (`solace-automation`'s GitHub Actions, `mulesoft-automation`/
  `btp-automation`'s own Logic Apps), triggered independently once a PR merges.
