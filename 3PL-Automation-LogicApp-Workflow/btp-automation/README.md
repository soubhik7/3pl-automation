# SAP BTP Full Automation

Python automation framework covering every layer of SAP Business Technology Platform — accessed four ways: **interactive TUI**, **scripted CLI**, **REST API + web dashboard**, and **AI agent tools (MCP)**.

---

## Table of Contents

- [What It Can Do](#what-it-can-do)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Credential Setup](#credential-setup)
- [Authentication](#authentication)
- [1 · Interactive TUI](#1--interactive-tui)
- [2 · Scripted CLI](#2--scripted-cli)
- [3 · Web App (FastAPI + Dashboard)](#3--web-app-fastapi--dashboard)
- [4 · AI Agent Tools (MCP Server)](#4--ai-agent-tools-mcp-server)
- [Testing All Four Modes](#testing-all-four-modes)
- [Library Usage (Python API)](#library-usage-python-api)
- [API Coverage](#api-coverage)
- [Troubleshooting](#troubleshooting)

---

## What It Can Do

| Layer | TUI | CLI | Web API | MCP Agent |
|-------|:---:|:---:|:-------:|:---------:|
| Account hierarchy (subaccounts, directories) | ✓ | ✓ | ✓ | ✓ |
| Entitlements (assign service plan quotas) | ✓ | ✓ | ✓ | ✓ |
| Environments (CF, Kyma provisioning) | ✓ | ✓ | ✓ | ✓ |
| Security (roles, role collections, users) | ✓ | ✓ | ✓ | ✓ |
| Service instances & bindings (full lifecycle) | ✓ | ✓ | ✓ | ✓ |
| Cloud Foundry (spaces, apps, services, keys) | ✓ | ✓ | ✓ | ✓ |
| **Scaffold & deploy new CF app from template** | ✓ | – | ✓ | ✓ |
| Destinations (HTTP, BasicAuth, OAuth2, OnPremise) | ✓ | ✓ | ✓ | ✓ |
| SAP Integration Suite (iFlows, packages, logs) | ✓ | ✓ | ✓ | ✓ |
| Composite: create instance + bind in one call | – | – | – | ✓ |
| Full account snapshot | ✓ | – | ✓ | ✓ |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Entry Points                                │
│                                                                     │
│  btp_cli.py      btp_api.py          btp_mcp_server.py              │
│  (TUI + CLI)     (FastAPI REST)      (46 MCP tools for AI)          │
│       │               │                      │                      │
│       └───────────────┴──────────────────────┘                      │
│                       │                                             │
│                 btp/ Python Service Layer                           │
│  ┌──────────┐ ┌────────────┐ ┌──────────┐ ┌──────────────────────┐  │
│  │accounts  │ │entitlements│ │services  │ │destinations          │  │
│  │provision │ │authorizat. │ │btp_cli   │ │integration_suite     │  │
│  └────┬─────┘ └─────┬──────┘ └────┬─────┘ └──────────┬───────────┘  │
└───────┼─────────────┼─────────────┼──────────────────┼──────────────┘
        │             │             │                  │
   BTP CLI      OAuth2 REST    BTP CLI +           REST APIs
  subprocess     (XSUAA)     CF CLI subprocess   (Destination Svc
  `btp` binary              `cf` binary           IS OData API)

                         ┌──────────────┐
                         │  btp_web/    │
                         │  index.html  │ ← served by btp_api.py
                         └──────────────┘
```

**Auth paths:**

| Path | Used by | Mechanism |
|------|---------|-----------|
| BTP CLI subprocess | Accounts, entitlements, provisioning, security, service instances/bindings | Interactive SSO — `btp login --sso`, token cached 24 h |
| CF CLI subprocess | Spaces, apps, CF services, service keys | `cf login --sso`, session cached |
| Destination Service REST | Destinations CRUD | OAuth2 `client_credentials` from service binding |
| Integration Suite OData | Packages, iFlows, message logs | OAuth2 `client_credentials` from Process Integration Runtime key |

---

## Project Structure

```
btp-automation/
├── .env                          ← YOUR SECRETS (gitignored)
├── .env.example                  ← Variable name template (no real values)
├── .gitignore
├── .mcp.json                     ← Claude Code MCP server registration
├── pyproject.toml                ← Package metadata, deps, entry points
├── requirements.txt              ← Flat dep list
│
├── btp_cli.py                    ← Interactive TUI + Click CLI (9 command groups)
├── btp_api.py                    ← FastAPI REST backend (40+ endpoints)
├── btp_mcp_server.py             ← FastMCP server (46 AI agent tools)
│
├── btp_web/
│   └── index.html                ← Web dashboard (SAP Fiori style, no build step)
│
├── apps/                         ← Scaffolded CF apps land here (gitignored)
│   └── <app-name>/
│       ├── app.py / app.js / index.html
│       ├── requirements.txt / package.json
│       └── manifest.yml
│
├── config/
│   └── btp_config.yaml           ← Central config: endpoints, GUIDs, defaults
│
├── btp/                          ← Core library
│   ├── __init__.py
│   ├── config.py                 ← BTPConfig: yaml + .env → typed properties
│   ├── auth.py                   ← TokenManager: OAuth2 + in-memory cache
│   ├── client.py                 ← BTPClient: HTTP session with auth
│   ├── btp_cli.py                ← BTP CLI subprocess wrapper
│   ├── cf.py                     ← CF CLI subprocess wrapper
│   ├── accounts.py               ← AccountsService
│   ├── entitlements.py           ← EntitlementsService
│   ├── provisioning.py           ← ProvisioningService
│   ├── authorization.py          ← AuthorizationService
│   ├── services.py               ← ServicesService (instances + bindings)
│   ├── destinations.py           ← DestinationService (REST)
│   ├── integration_suite.py      ← IntegrationSuiteService (OData)
│   ├── exceptions.py             ← BTPError, BTPAuthError, BTPNotFoundError
│   └── output.py                 ← Rich: tables, JSON, YAML
│
└── scripts/
    ├── list_all.py               ← Full BTP snapshot
    ├── manage_entitlements.py    ← Bulk entitlement ops
    ├── manage_roles.py           ← Role / role collection management
    └── setup_new_subaccount.py   ← End-to-end subaccount provisioning
```

---

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| Python | 3.10+ | `brew install python` |
| SAP BTP CLI | latest | `brew install btp` or [tools.hana.ondemand.com](https://tools.hana.ondemand.com/#cloud-btpcli) |
| CF CLI | v8+ | `brew install cloudfoundry/tap/cf-cli@8` |

---

## Installation

```bash
git clone <repo-url>
cd btp-automation

# Install everything (CLI entry point + web API + MCP server)
pip install -e .

# Verify entry point is available
btp-auto --help
```

`pip install -e .` installs these console entry points:

| Command | Runs |
|---------|------|
| `btp-auto` | Interactive TUI or CLI |
| (web) `uvicorn btp_api:app` | FastAPI REST + dashboard |
| (mcp) `python3 btp_mcp_server.py` | MCP server for AI agents |

---

## Credential Setup

All secrets live in `.env` — nothing is hardcoded.

```bash
cp .env.example .env
```

Edit `.env` and fill in:

```bash
# ── CIS Central ── for account / entitlement / provisioning / service instance APIs
BTP_CIS_CLIENT_ID=sb-ut-<uuid>-clone!b<n>|cis-central!b<n>
BTP_CIS_CLIENT_SECRET=<uuid>$<base64>

# ── XSUAA ── for authorization / roles operations
BTP_XSUAA_CLIENT_ID=sb-<app>!t<n>
BTP_XSUAA_CLIENT_SECRET=<uuid>$<base64>

# ── CF login (reference only, not a stored secret) ──
BTP_CF_USERNAME=your-email@example.com

# ── Integration Suite (optional — only needed for isuit commands / MCP IS tools) ──
IS_BASE_URL=https://<tenant>.integrationsuite.cfapps.us10.hana.ondemand.com
IS_TOKEN_URL=https://<tenant>.authentication.us10.hana.ondemand.com/oauth/token
IS_CLIENT_ID=sb-<app>!t<n>
IS_CLIENT_SECRET=<uuid>$<base64>
```

**Where to find CIS credentials:**
```bash
cf create-service-key btp-mcp-cis cis-key
cf service-key btp-mcp-cis cis-key
# clientid   → BTP_CIS_CLIENT_ID
# clientsecret → BTP_CIS_CLIENT_SECRET
```

**Where to find XSUAA credentials:**
```bash
cf create-service-key btp-mcp-xsuaa xsuaa-key
cf service-key btp-mcp-xsuaa xsuaa-key
# clientid   → BTP_XSUAA_CLIENT_ID
# clientsecret → BTP_XSUAA_CLIENT_SECRET
```

**Also verify** `config/btp_config.yaml`:
```yaml
global_account:
  guid: "<your-global-account-guid>"
  subdomain: "<your-ga-subdomain>"
subaccount:
  guid: "<your-subaccount-guid>"
  subdomain: "<your-subaccount-subdomain>"
  region: us10
```

---

## Authentication

```bash
# 1. BTP CLI — one-time SSO (cached ~24 h)
btp login --url https://cpcli.cf.eu10.hana.ondemand.com --sso
# Opens browser → complete SSO → session saved

# 2. CF CLI — one-time SSO (cached by cf)
cf login -a https://api.cf.us10-001.hana.ondemand.com --sso
cf target -o <org-name> -s dev

# Verify both are working
btp --format json get accounts/global-account | head -5
cf target
```

---

## 1 · Interactive TUI

The full-featured terminal UI — no arguments needed.

### Run

```bash
python3 btp_cli.py      # direct
btp-auto                # via installed entry point
btp-auto interactive    # explicit subcommand
```

### Main menu

```
╭──────────────────────────────────────────────────────────╮
│  SAP BTP Automation                                      │
│  Global: a6ff9d4dtrial | Subaccount: a6ff9d4dtrial       │
╰──────────────────────────────────────────────────────────╯

  Main Menu
    1  Accounts & Hierarchy
    2  Entitlements
    3  Provisioning / Environments
    4  Security: Roles & Users
    5  Services: Instances & Bindings
    6  Cloud Foundry
    7  Destinations
    8  SAP Integration Suite
    9  Account Snapshot
    0  Exit
```

Each section opens a submenu. All inputs default from `config/btp_config.yaml`. Destructive operations always confirm with `y/n`.

### Section highlights

**Cloud Foundry → 7: Scaffold & Create New App**
```
Choose Runtime:
  1  Python (Flask + gunicorn)
  2  Node.js (Express)
  3  Static HTML5 (Staticfile buildpack)

App name     → my-api
Memory       → 256M
Instances    → 1
Description  → My API on BTP
Push now?    → y

→ Generates all files, runs cf push, shows live build log
```

**Services: Instances & Bindings — full lifecycle**
```
5  Services: Instances & Bindings
  1  List Service Offerings
  2  List Service Plans
  3  List Service Instances
  4  Create Service Instance    ← prompts: name, offering, plan, params
  5  Delete Service Instance    ← confirms before deleting
  6  ── Bindings ──
  7  List Service Bindings
  8  Create Service Binding     ← prompts: binding name, instance name
  9  Get Service Binding (credentials)   ← returns full JSON credentials
 10  Delete Service Binding
```

### Test interactively with piped input

```bash
# Test accounts section (global account → list subaccounts → back → exit)
printf "1\n1\n2\n0\n0\n" | python3 btp_cli.py interactive

# Test full services lifecycle (list instances → list bindings → back → exit)
printf "5\n3\n7\n0\n0\n" | python3 btp_cli.py interactive

# Test account snapshot
printf "9\n0\n" | python3 btp_cli.py interactive
```

---

## 2 · Scripted CLI

```bash
btp-auto [--format table|json|yaml] <group> <command> [options]
python3 btp_cli.py [--format table|json|yaml] <group> <command> [options]
```

### accounts

```bash
btp-auto accounts global-account
btp-auto accounts list-subaccounts
btp-auto accounts get-subaccount <guid>
btp-auto accounts create-subaccount --name "Dev" --subdomain my-dev --region us10
btp-auto accounts delete-subaccount <guid>
btp-auto accounts list-directories
btp-auto accounts create-directory --name Engineering
```

### entitlements

```bash
btp-auto entitlements list                                              # global
btp-auto entitlements list --subaccount <guid>                         # subaccount quotas
btp-auto entitlements assign --subaccount <guid> --service destination --plan lite
btp-auto entitlements unassign --subaccount <guid> --service destination --plan lite
btp-auto entitlements data-centers
```

### provisioning

```bash
btp-auto provisioning list-available --subaccount <guid>
btp-auto provisioning list --subaccount <guid>
btp-auto provisioning create-cf --subaccount <guid> --org-name my-org
btp-auto provisioning delete --subaccount <guid> --instance-id <id>
```

### auth

```bash
btp-auto auth list-apps
btp-auto auth list-roles
btp-auto auth list-role-collections
btp-auto auth create-role-collection --name MyRC --description "Custom access"
btp-auto auth add-role-to-collection --collection MyRC --role-name Developer \
  --template Developer --app-id sapappstudiotrial!t123
btp-auto auth list-users
btp-auto auth assign-user --collection MyRC --user user@company.com
btp-auto auth unassign-user --collection MyRC --user user@company.com
```

### services — full lifecycle

```bash
# Discover
btp-auto services list-offerings
btp-auto services list-plans
btp-auto services list-plans --offering xsuaa
btp-auto services list-instances

# Instance lifecycle
btp-auto services create-instance \
  --name my-xsuaa \
  --offering xsuaa \
  --plan application \
  --params '{"xsappname":"my-app","tenant-mode":"dedicated"}'
btp-auto services delete-instance my-xsuaa       # bindings must be removed first

# Binding lifecycle (generates credentials)
btp-auto services list-bindings
btp-auto services create-binding --binding my-key --instance my-xsuaa
btp-auto services get-binding my-key             # full credentials JSON
btp-auto services get-binding my-key --format json | jq .credentials
btp-auto services delete-binding my-key
```

### cf — Cloud Foundry

```bash
# Target
btp-auto cf target
btp-auto cf set-target --org my-org --space dev

# Spaces
btp-auto cf list-spaces
btp-auto cf create-space staging
btp-auto cf delete-space staging

# Apps
btp-auto cf list-apps
btp-auto cf push my-app --path ./dist
btp-auto cf push my-app --path ./dist --no-start
btp-auto cf start my-app
btp-auto cf stop my-app
btp-auto cf restage my-app
btp-auto cf delete-app my-app
btp-auto cf logs my-app

# Environment variables
btp-auto cf env my-app
btp-auto cf set-env my-app DB_URL "jdbc:postgresql://host:5432/db"

# CF services
btp-auto cf list-services
btp-auto cf create-service destination lite my-dest
btp-auto cf delete-service my-dest
btp-auto cf bind-service my-app my-dest
btp-auto cf unbind-service my-app my-dest

# Service keys (credentials without an app)
btp-auto cf create-service-key my-dest dest-key
btp-auto cf get-service-key my-dest dest-key
```

### destinations

```bash
btp-auto destinations list
btp-auto destinations get MY_BACKEND

# NoAuthentication HTTP
btp-auto destinations create-http \
  --name MY_BACKEND \
  --url https://api.example.com

# BasicAuthentication
btp-auto destinations create-http \
  --name MY_BACKEND \
  --url https://api.example.com \
  --auth BasicAuthentication \
  --user myuser --password mysecret

# On-premise via Cloud Connector
btp-auto destinations create-http \
  --name MY_ONPREM \
  --url http://internal-host:8080 \
  --proxy OnPremise

# OAuth2 client credentials
btp-auto destinations create-oauth \
  --name MY_API \
  --url https://api.service.com \
  --client-id sb-myapp!t123 \
  --client-secret "abc\$xyz" \
  --token-url https://tenant.authentication.us10.hana.ondemand.com/oauth/token

btp-auto destinations delete MY_BACKEND
```

### isuit — SAP Integration Suite

```bash
btp-auto isuit list-packages
btp-auto isuit list-iflows <PackageId>
btp-auto isuit deploy <iFlowId>
btp-auto isuit deploy <iFlowId> --version active
btp-auto isuit undeploy <iFlowId>
btp-auto isuit list-runtime
btp-auto isuit logs --top 50
btp-auto isuit logs --status FAILED
btp-auto isuit failed-messages --top 20
```

### Output formats

```bash
btp-auto --format json   services list-instances
btp-auto --format yaml   auth list-role-collections
btp-auto --format table  entitlements list       # default
```

---

## 3 · Web App (FastAPI + Dashboard)

### Run

```bash
uvicorn btp_api:app --reload --port 8000
```

Then open **[http://localhost:8000](http://localhost:8000)** for the web dashboard.

For production:
```bash
uvicorn btp_api:app --host 0.0.0.0 --port 8000 --workers 2
```

### Dashboard tabs

| Tab | Shows | Actions |
|-----|-------|---------|
| **Services** | All service instances + bindings with status | Read |
| **Cloud Foundry** | CF target, apps, CF service instances | Read |
| **Security** | Role collections with read-only badge | Read |
| **Destinations** | All subaccount destinations | Create HTTP, Delete |
| **+ Create** | Scaffold & push new CF app; Create service instance | Write |

KPI tiles at the top show live counts: Service Instances · Bindings · CF Apps · Entitlements.

### REST API reference

Full Swagger UI at **[http://localhost:8000/docs](http://localhost:8000/docs)**.

**Accounts**
```
GET  /api/accounts/global                    Global account details
GET  /api/accounts/subaccounts               List subaccounts
GET  /api/accounts/subaccounts/{guid}        Get one subaccount
POST /api/accounts/subaccounts               Create subaccount
```

**Entitlements**
```
GET  /api/entitlements                       Global list (or ?subaccount_guid=...)
POST /api/entitlements                       Assign entitlement
```

**Provisioning**
```
GET  /api/environments/available             Available env types
GET  /api/environments/instances             Provisioned CF orgs / Kyma clusters
```

**Security**
```
GET  /api/security/role-collections          List role collections
GET  /api/security/users                     List users
POST /api/security/role-collections/assign   Assign user to role collection
```

**Services**
```
GET    /api/services/offerings               All service offerings
GET    /api/services/plans                   Plans (filter: ?offering=xsuaa)
GET    /api/services/instances               All instances
POST   /api/services/instances               Create instance
DELETE /api/services/instances/{name}        Delete instance
GET    /api/services/bindings                All bindings
POST   /api/services/bindings                Create binding
GET    /api/services/bindings/{name}/credentials  Get credentials
DELETE /api/services/bindings/{name}         Delete binding
```

**Cloud Foundry**
```
GET    /api/cf/target                        CF target info
GET    /api/cf/spaces                        List spaces
GET    /api/cf/apps                          List apps
POST   /api/cf/apps/scaffold                 Scaffold + push new app
DELETE /api/cf/apps/{name}                   Delete app
GET    /api/cf/services                      CF service instances
```

**Destinations**
```
GET    /api/destinations                     List all
GET    /api/destinations/{name}              Get one
POST   /api/destinations/http                Create HTTP destination
POST   /api/destinations/oauth               Create OAuth2 destination
DELETE /api/destinations/{name}              Delete
```

**Integration Suite**
```
GET  /api/isuit/packages                              List packages
GET  /api/isuit/packages/{package_id}/iflows          List iFlows
POST /api/isuit/iflows/{iflow_id}/deploy              Deploy iFlow
GET  /api/isuit/runtime                               Runtime artifacts
GET  /api/isuit/logs                                  Logs (filter: ?status=FAILED)
```

**Dashboard**
```
GET  /api/snapshot                           Full account snapshot (used by dashboard)
```

### Test the API

```bash
# Health check — global account
curl http://localhost:8000/api/accounts/global | python3 -m json.tool

# List service instances
curl http://localhost:8000/api/services/instances | python3 -m json.tool

# Create a service instance
curl -X POST http://localhost:8000/api/services/instances \
  -H "Content-Type: application/json" \
  -d '{"name":"test-ff","offering_name":"feature-flags","plan_name":"lite"}'

# Create a binding for it
curl -X POST http://localhost:8000/api/services/bindings \
  -H "Content-Type: application/json" \
  -d '{"binding_name":"test-ff-key","instance_name":"test-ff"}'

# Get credentials
curl http://localhost:8000/api/services/bindings/test-ff-key/credentials \
  | python3 -m json.tool | grep -A5 '"credentials"'

# Delete binding then instance
curl -X DELETE http://localhost:8000/api/services/bindings/test-ff-key
curl -X DELETE http://localhost:8000/api/services/instances/test-ff

# Scaffold and deploy a Python app
curl -X POST http://localhost:8000/api/cf/apps/scaffold \
  -H "Content-Type: application/json" \
  -d '{"app_name":"test-api","runtime":"python","memory":"256M","push":true}' \
  | python3 -m json.tool

# List destinations
curl http://localhost:8000/api/destinations | python3 -m json.tool

# Full snapshot
curl http://localhost:8000/api/snapshot | python3 -m json.tool
```

---

## 4 · AI Agent Tools (MCP Server)

The MCP server exposes **46 tools** across all BTP domains. A Claude agent can plan and execute complete end-to-end BTP workflows from a single natural-language prompt.

### How it works

```
You (natural language prompt)
        │
        ▼
  Claude Agent
  - reads account_snapshot() to understand current state
  - plans required steps
  - calls tools in sequence
  - handles errors and retries
        │
        ▼
  btp_mcp_server.py (46 tools)
        │
        ▼
  btp/ Python service layer → BTP CLI / CF CLI / REST APIs
```

### Run (stdio transport)

```bash
python3 btp_mcp_server.py
```

The server communicates over stdin/stdout — MCP clients start it automatically.

### Configure for Claude Code

The `.mcp.json` in the project root registers the server automatically:

```json
{
  "mcpServers": {
    "btp-automation": {
      "type": "stdio",
      "command": "python3",
      "args": ["btp_mcp_server.py"],
      "cwd": "/absolute/path/to/btp-automation"
    }
  }
}
```

After editing, run `/mcp` in Claude Code to reload, or restart the session.

### Configure for Claude Desktop

Edit `~/.claude/claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "btp-automation": {
      "command": "python3",
      "args": ["/absolute/path/to/btp-automation/btp_mcp_server.py"],
      "cwd": "/absolute/path/to/btp-automation"
    }
  }
}
```

Restart Claude Desktop to load the server.

### All 46 tools

**Account tools**
| Tool | What it does |
|------|-------------|
| `get_global_account` | Global account details |
| `list_subaccounts` | All subaccounts with GUIDs/regions |
| `get_subaccount` | One subaccount by GUID |
| `create_subaccount` | Create a new subaccount |

**Entitlement tools**
| Tool | What it does |
|------|-------------|
| `list_global_entitlements` | All service entitlements |
| `list_subaccount_entitlements` | Quotas for the configured subaccount |
| `assign_entitlement` | Assign service plan to subaccount |

**Provisioning tools**
| Tool | What it does |
|------|-------------|
| `list_available_environments` | Available env types (CF, Kyma) |
| `list_environment_instances` | Provisioned CF orgs / Kyma clusters |

**Security tools**
| Tool | What it does |
|------|-------------|
| `list_role_collections` | All role collections |
| `list_users` | All users in subaccount |
| `create_role_collection` | Create role collection |
| `assign_user_to_role_collection` | Assign user to collection |

**Service tools**
| Tool | What it does |
|------|-------------|
| `list_service_offerings` | Full service catalog |
| `list_service_plans` | Plans, filterable by offering |
| `list_service_instances` | All instances with status |
| `create_service_instance` | Create instance (any offering/plan) |
| `delete_service_instance` | Delete instance |
| `list_service_bindings` | All bindings |
| `create_service_binding` | Bind and generate credentials |
| `get_service_binding_credentials` | Retrieve credentials |
| `delete_service_binding` | Revoke binding |
| `create_instance_and_bind` | **Composite**: create + bind in one call |

**Cloud Foundry tools**
| Tool | What it does |
|------|-------------|
| `cf_target` | Current CF org/space/user |
| `cf_list_spaces` | All spaces |
| `cf_list_apps` | Apps with status/URLs |
| `cf_scaffold_and_push_app` | **Generate code + manifest + cf push** |
| `cf_push_app` | Push existing directory |
| `cf_delete_app` | Delete app + routes |
| `cf_app_logs` | Recent log output |
| `cf_set_env` | Set environment variable |
| `cf_list_services` | CF marketplace instances |
| `cf_create_service_key` | Create key + return credentials |
| `cf_bind_service` | Bind service to app |

**Destination tools**
| Tool | What it does |
|------|-------------|
| `list_destinations` | All subaccount destinations |
| `get_destination` | One destination by name |
| `create_http_destination` | HTTP/Basic/OnPremise destination |
| `create_oauth_destination` | OAuth2 client-credentials destination |
| `delete_destination` | Delete by name |

**Integration Suite tools**
| Tool | What it does |
|------|-------------|
| `is_list_packages` | All integration packages |
| `is_list_iflows` | iFlows in a package |
| `is_deploy_iflow` | Deploy to runtime |
| `is_list_runtime_artifacts` | Deployed artifacts |
| `is_get_failed_messages` | Failed message logs |
| `is_get_message_logs` | All logs with status filter |

**Composite tools**
| Tool | What it does |
|------|-------------|
| `account_snapshot` | Full state: account + instances + bindings + CF apps |
| `create_instance_and_bind` | Create service + binding + return credentials in one call |

### Example agent prompts

Once the MCP server is registered, send prompts like these in Claude:

**Full account overview:**
```
Use account_snapshot to show me everything running in my BTP account right now.
```

**Complete service setup:**
```
Create a feature-flags service instance called "ff-prod" with the lite plan,
immediately create a binding for it called "ff-prod-key", and show me the credentials.
```

**App from scratch, end-to-end:**
```
Scaffold a new Python Flask app called "order-api" and deploy it to Cloud Foundry.
Then set the environment variable ORDER_DB_URL to "postgresql://host:5432/orders".
Show me the app logs when it's running.
```

**Full integration scenario:**
```
I need to connect my Cloud Foundry app "order-api" to a backend REST service.
1. Create an OAuth2 destination called "BACKEND_API" pointing to https://backend.company.com
   using client_id "my-client" and client_secret "my-secret"
2. Check that it's listed in the destination service
3. Bind the destination service instance "btp-mcp-destination" to "order-api"
4. Restage the app so it picks up the binding
```

**Monitoring:**
```
Check if there are any failed messages in Integration Suite in the last 20 logs.
If yes, show me the details of each failed message.
```

### Test the MCP server manually

```bash
# Verify all 46 tools are registered
python3 -c "
import btp_mcp_server as s
tools = s.mcp._tool_manager._tools
print(f'Tools registered: {len(tools)}')
for name in sorted(tools): print(f'  {name}')
"

# Test a specific tool (requires active BTP CLI session)
python3 -c "
from btp_mcp_server import list_service_instances
import json
result = json.loads(list_service_instances())
print(f'Found {len(result)} service instances')
for i in result:
    print(f'  {i[\"name\"]:30s}  ready={i[\"ready\"]}')
"

# Test scaffold tool (no BTP session needed for file generation)
python3 -c "
from btp_mcp_server import cf_scaffold_and_push_app
import json
r = json.loads(cf_scaffold_and_push_app('test-app', 'python', memory='128M', instances=1))
print('status:', r['status'])
print('files: ', r.get('files_generated'))
import shutil; shutil.rmtree('apps/test-app', ignore_errors=True)
"

# Test composite tool: create instance + bind
python3 -c "
from btp_mcp_server import create_instance_and_bind, delete_service_binding, delete_service_instance
import json
r = json.loads(create_instance_and_bind('test-ff', 'feature-flags', 'lite', 'test-ff-key'))
print('status:', r.get('status'))
print('has credentials:', bool(r.get('credentials')))
# Cleanup
delete_service_binding('test-ff-key')
delete_service_instance('test-ff')
print('cleaned up')
"
```

---

## Testing All Four Modes

Full end-to-end test — runs all 4 modes against live BTP:

```bash
# ── Prerequisites ────────────────────────────────────────────────────────────
btp login --url https://cpcli.cf.eu10.hana.ondemand.com --sso
cf login -a https://api.cf.us10-001.hana.ondemand.com --sso

# ── 1. TUI — pipe simulated inputs ───────────────────────────────────────────
echo "Testing TUI..."

# Accounts: global account + list subaccounts
printf "1\n1\n2\n0\n0\n" | python3 btp_cli.py interactive

# Services: list instances + list bindings
printf "5\n3\n7\n0\n0\n" | python3 btp_cli.py interactive

# CF: show target + list spaces
printf "6\n1\n2\n0\n0\n" | python3 btp_cli.py interactive

# Snapshot
printf "9\n0\n" | python3 btp_cli.py interactive

# ── 2. CLI ───────────────────────────────────────────────────────────────────
echo "Testing CLI..."

btp-auto accounts list-subaccounts
btp-auto services list-instances
btp-auto --format json services list-bindings
btp-auto cf target

# Create → bind → get credentials → delete
btp-auto services create-instance --name test-cli --offering feature-flags --plan lite
btp-auto services create-binding --binding test-cli-key --instance test-cli
btp-auto services get-binding test-cli-key
btp-auto services delete-binding test-cli-key
btp-auto services delete-instance test-cli

# ── 3. Web API ───────────────────────────────────────────────────────────────
echo "Testing Web API..."
uvicorn btp_api:app --port 8765 &
sleep 3

curl -s http://localhost:8765/api/services/instances | python3 -c \
  "import json,sys; d=json.load(sys.stdin); print(f'instances: {len(d)}')"

curl -s -X POST http://localhost:8765/api/services/instances \
  -H "Content-Type: application/json" \
  -d '{"name":"test-api","offering_name":"feature-flags","plan_name":"lite"}' \
  | python3 -m json.tool

curl -s -X DELETE http://localhost:8765/api/services/instances/test-api

curl -s http://localhost:8765/api/snapshot | python3 -c \
  "import json,sys; d=json.load(sys.stdin); print('snapshot keys:', list(d.keys()))"

kill %1

# ── 4. MCP tools ─────────────────────────────────────────────────────────────
echo "Testing MCP tools..."

python3 -c "
from btp_mcp_server import (
    list_service_instances, account_snapshot,
    create_instance_and_bind, delete_service_binding, delete_service_instance
)
import json

# list instances
r = json.loads(list_service_instances())
print(f'Instances: {len(r)}')

# composite: create + bind in one call
r = json.loads(create_instance_and_bind(
    'test-mcp', 'feature-flags', 'lite', 'test-mcp-key'))
print('create_instance_and_bind status:', r['status'])
print('has credentials:', bool(r.get('credentials')))

# cleanup
delete_service_binding('test-mcp-key')
delete_service_instance('test-mcp')
print('MCP tools OK')
"
echo "All tests done."
```

---

## Library Usage (Python API)

Use the service classes directly in your own scripts:

```python
from btp import (
    AccountsService, EntitlementsService, ProvisioningService,
    AuthorizationService, ServicesService, DestinationService,
    IntegrationSuiteService,
)
from btp import cf
from btp.config import BTPConfig

cfg = BTPConfig()

# ── Account hierarchy ─────────────────────────────────────────────────────────
accounts = AccountsService()
ga = accounts.get_global_account()
subs = accounts.list_subaccounts()
new = accounts.create_subaccount("Dev", "my-dev-sa", "us10")

# ── Service lifecycle ─────────────────────────────────────────────────────────
svc = ServicesService()
svc.create_instance("my-xsuaa", "xsuaa", "application",
                    {"xsappname": "my-app", "tenant-mode": "dedicated"})
svc.create_binding("my-key", "my-xsuaa")
creds = svc.get_binding("my-key")   # full dict with clientid, clientsecret, url
svc.delete_binding("my-key")
svc.delete_instance("my-xsuaa")

# ── Cloud Foundry ─────────────────────────────────────────────────────────────
cf.set_target(space="dev")
cf.push_app("my-app", path="./dist")
cf.bind_service("my-app", "my-xsuaa")
cf.start_app("my-app")
print(cf.recent_logs("my-app"))

# ── Destinations ──────────────────────────────────────────────────────────────
dest = DestinationService(cfg.subaccount_guid)  # auto-detects binding

dest.create(DestinationService.http_destination(
    name="MY_BACKEND",
    url="https://api.example.com",
    auth="NoAuthentication",
))
dest.create(DestinationService.oauth_destination(
    name="MY_OAUTH_API",
    url="https://api.example.com",
    client_id="sb-myapp!t123",
    client_secret="secret",
    token_url="https://tenant.auth.example.com/oauth/token",
))
for d in dest.list():
    print(d["Name"], d["Type"], d["URL"])

# ── Integration Suite ─────────────────────────────────────────────────────────
is_svc = IntegrationSuiteService()   # reads IS_* from .env
packages = is_svc.list_packages()
iflows = is_svc.list_iflows(packages[0]["Id"])
is_svc.deploy_iflow(iflows[0]["Id"])
failed = is_svc.get_failed_messages(top=10)

# ── Security ──────────────────────────────────────────────────────────────────
auth = AuthorizationService()
auth.create_role_collection("My-RC", "Custom access")
auth.assign_user_to_collection("My-RC", "user@company.com")
```

---

## API Coverage

| Domain | Operations | Backend |
|--------|-----------|---------|
| **Global Account** | Get | BTP CLI |
| **Subaccounts** | List, Get, Create, Update, Delete | BTP CLI |
| **Directories** | List, Get, Create, Update, Delete | BTP CLI |
| **Entitlements** | List (global + subaccount), Assign, Unassign | BTP CLI |
| **Data Centers** | List available regions | BTP CLI |
| **CF Environments** | List available, List instances, Create, Delete | BTP CLI |
| **XSUAA Apps** | List | BTP CLI |
| **Roles** | List, Create, Delete | BTP CLI |
| **Role Collections** | List, Get, Create, Update, Delete | BTP CLI |
| **Role → Collection** | Add, Remove | BTP CLI |
| **User → Collection** | Assign, Remove, List users | BTP CLI |
| **Service Offerings** | List | BTP CLI |
| **Service Plans** | List, filter by offering | BTP CLI |
| **Service Instances** | List, Create, Delete | BTP CLI |
| **Service Bindings** | List, Create, Get (credentials), Delete | BTP CLI |
| **CF Target** | Get, Set | CF CLI |
| **CF Spaces** | List, Create, Delete | CF CLI |
| **CF Apps** | List, Scaffold+Push, Push, Start, Stop, Restage, Delete, Logs | CF CLI |
| **CF Env vars** | Get, Set | CF CLI |
| **CF Services** | List, Create, Delete | CF CLI |
| **CF Service Bindings** | Bind app, Unbind app | CF CLI |
| **CF Service Keys** | Create, Get (credentials), Delete | CF CLI |
| **Destinations** | List, Get, Create HTTP/Basic/OAuth2/OnPremise, Update, Delete | Destination Service REST |
| **Integration Packages** | List, Get | Integration Suite OData |
| **iFlows** | List, Get, Deploy, Undeploy | Integration Suite OData |
| **Runtime Artifacts** | List, Get | Integration Suite OData |
| **Message Logs** | List all/failed/by-status, Get attachments | Integration Suite OData |
| **Value Mappings** | List | Integration Suite OData |

---

## Troubleshooting

### "BTP CLI not found"
```bash
brew install btp
# or: https://tools.hana.ondemand.com/#cloud-btpcli
```

### "Not logged in to BTP CLI" / "Unknown session"
```bash
btp login --url https://cpcli.cf.eu10.hana.ondemand.com --sso
```

### "CF CLI not found"
```bash
brew install cloudfoundry/tap/cf-cli@8
```

### "Not logged in to CF"
```bash
cf login -a https://api.cf.us10-001.hana.ondemand.com --sso
cf target -o <org> -s dev
```

### "No destination service binding found"
The destination service auto-detect looks for a BTP-level service binding whose `name` or `context.instance_name` contains `dest`. Create one first:
```bash
# First create a BTP-level destination instance (not CF)
btp create services/instance \
  --subaccount <guid> \
  --name dest-btp \
  --offering-name destination \
  --plan-name lite

# Then create a binding
btp-auto services create-binding --binding dest-key --instance dest-btp
```

> **Note:** CF-created service instances (via `cf create-service`) cannot have BTP-level bindings. Use `cf create-service-key` for those.

### "Integration Suite credentials not configured"
Add to `.env`:
```bash
IS_BASE_URL=https://<tenant>.integrationsuite.cfapps.us10.hana.ondemand.com
IS_TOKEN_URL=https://<tenant>.authentication.us10.hana.ondemand.com/oauth/token
IS_CLIENT_ID=sb-<app>!t<n>
IS_CLIENT_SECRET=<uuid>$<base64>
```

### MCP server not appearing in Claude Code
1. Check `.mcp.json` uses absolute paths in `cwd`
2. Run `/mcp` in Claude Code to reload
3. Verify the server starts manually: `python3 btp_mcp_server.py`

### FastAPI 400 on `/api/accounts/global`
The REST API uses OAuth2 tokens that expire. Refresh:
```bash
# The BTP REST client auto-refreshes — if you get 401, re-run with fresh .env tokens
# Re-create the CIS service key to get fresh credentials:
cf delete-service-key btp-mcp-cis cis-key
cf create-service-key btp-mcp-cis cis-key
cf service-key btp-mcp-cis cis-key
# Update BTP_CIS_CLIENT_ID and BTP_CIS_CLIENT_SECRET in .env
```

### Service instance deletion fails: "has active bindings"
```bash
btp-auto services list-bindings --format json
btp-auto services delete-binding <binding-name>
btp-auto services delete-instance <instance-name>
```

### `rich.errors.MarkupError` on CF push output
Already fixed — `out.info()` and `out.error()` escape Rich markup automatically. If you see this on an older version, upgrade:
```bash
pip install -e .
```
