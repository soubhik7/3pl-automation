#!/usr/bin/env python3
"""
deploy.py — end-to-end deployment of the 3PL Solace/MuleSoft/BTP automation hub against the
real Azure subscription + AI Foundry project, encoding every real-world gotcha hit while
deploying this by hand (see README.md "Gotchas hit during the real deployment").

Idempotent: every step checks-before-creating, so re-running this after the first successful
run (or after only some steps failed) is safe — it will not duplicate branches, containers,
agents, or Logic Apps, and will not touch the unrelated pre-existing resources in this resource
group (the other ~8 Foundry agents, the wf-solace-automation-* workflows, the standalone
Gmail-triggered `solace-mail-trigger` Consumption Logic App, etc).

Usage:
    python3 deploy.py all                  # run every step, in order
    python3 deploy.py branches              # just the 3 GitHub branches
    python3 deploy.py cosmos                # just the 2 Cosmos containers
    python3 deploy.py function-app          # just the mcp-server zip deploy
    python3 deploy.py agents                # just the Foundry agent + workflow registration
    python3 deploy.py logic-apps            # just the 4 Consumption Logic Apps
    python3 deploy.py smoke-test            # just the 3 -generate route smoke tests

Never run by this script: anything that writes to GitHub beyond creating the 3 empty
branches (no commits, no PRs — that only happens via a real Teams-approved -publish call,
which is intentionally not part of any automated deployment), and anything touching Key
Vault (blocked for this subscription's identities by design — see README).
"""
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile

_AZ_BIN = shutil.which("az") or "az"  # Windows: az resolves to az.CMD, which
                                       # subprocess can't find via a bare "az" without shell=True

# --- Real, already-confirmed resource identities for this deployment ----------------------
SUBSCRIPTION_ID = "a556a28c-c14a-4657-b59f-e0c80f0bb54c"
RESOURCE_GROUP = "ODL-IBM-2262033"
FUNCTION_APP_NAME = "ip-3pl-mcp"  # renamed from the original ip-solace-mcp (Azure Web Apps
                                   # can't be renamed in place, so this was a create-new app +
                                   # cutover + delete-old, not an in-place rename)
FUNCTION_APP_BASE_URL = f"https://{FUNCTION_APP_NAME}.azurewebsites.net"
COSMOS_ACCOUNT = "integration-pulse-nosql"
COSMOS_DATABASE = "integration-pulse"
COSMOS_PARTITION_KEY = "/tenantId"  # matches the existing solace_requests container
COSMOS_THROUGHPUT = 400            # this account is provisioned (not serverless) —
                                    # matches solace_requests' existing throughput
LOGIC_APP_LOCATION = "canadacentral"  # MUST match the `teams` connection's region — a Logic
                                       # App in a different region cannot resolve it
TEAMS_CONNECTION_ID = (
    f"/subscriptions/{SUBSCRIPTION_ID}/resourceGroups/{RESOURCE_GROUP}"
    "/providers/Microsoft.Web/connections/teams"
)
GITHUB_OWNER = "soubhik7"
GITHUB_REPO = "3pl-automation"
PERSISTENT_BRANCHES = ["solace/onboarding", "mulesoft/onboarding", "btp/onboarding"]
FOUNDRY_PROJECT_ENDPOINT = (
    "https://integration-pulse-found-resource.services.ai.azure.com/api/projects/integration-pulse-proj"
)

_HERE = pathlib.Path(__file__).parent
_REPO_ROOT = _HERE.parent
_MCP_SERVER_DIR = _REPO_ROOT / "mcp-server"
_LOGIC_APP_CONSUMPTION_DIR = _REPO_ROOT / "logic-app" / "consumption"
_AGENT_DIR = _REPO_ROOT / "agent"


def _az(*args: str, timeout: int = 120) -> str:
    """Run an az CLI command, return stdout. Raises on non-zero exit."""
    result = subprocess.run(
        [_AZ_BIN, *args], capture_output=True, text=True, timeout=timeout,
    )
    if result.returncode != 0:
        raise RuntimeError(f"az {' '.join(args)} failed:\n{result.stderr}")
    return result.stdout.strip()


def _github_token() -> str:
    """PAT from env if set, else read from the live Function App's own app setting (the
    same one it uses at runtime) — never hardcoded, never printed."""
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        return token
    out = _az(
        "functionapp", "config", "appsettings", "list",
        "-n", FUNCTION_APP_NAME, "-g", RESOURCE_GROUP,
        "--query", "[?name=='GITHUB_TOKEN'].value", "-o", "tsv",
    )
    if not out:
        raise RuntimeError(
            "No GITHUB_TOKEN found (not in env, not as a GITHUB_TOKEN app setting on "
            f"{FUNCTION_APP_NAME}). Set GITHUB_TOKEN in your shell or as that app setting first."
        )
    return out


def _github_request(method: str, path: str, token: str, body: dict | None = None) -> tuple[int, dict]:
    req = urllib.request.Request(
        f"https://api.github.com{path}",
        data=json.dumps(body).encode() if body is not None else None,
        method=method,
        headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github+json"},
    )
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, json.loads(resp.read() or b"{}")
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b"{}")


def step_branches() -> None:
    """Create the 3 persistent feature branches off main, if they don't already exist."""
    print("-- branches --")
    token = _github_token()
    status, main_ref = _github_request("GET", f"/repos/{GITHUB_OWNER}/{GITHUB_REPO}/git/refs/heads/main", token)
    if status != 200:
        raise RuntimeError(f"could not read main ref: {status} {main_ref}")
    main_sha = main_ref["object"]["sha"]

    for branch in PERSISTENT_BRANCHES:
        status, _ = _github_request(
            "GET", f"/repos/{GITHUB_OWNER}/{GITHUB_REPO}/git/refs/heads/{branch}", token,
        )
        if status == 200:
            print(f"  {branch} already exists, skipping")
            continue
        status, body = _github_request(
            "POST", f"/repos/{GITHUB_OWNER}/{GITHUB_REPO}/git/refs", token,
            {"ref": f"refs/heads/{branch}", "sha": main_sha},
        )
        if status != 201:
            raise RuntimeError(f"failed to create {branch}: {status} {body}")
        print(f"  created {branch}")


def step_cosmos() -> None:
    """Create the mulesoft_requests / btp_requests containers if missing (solace_requests
    already exists from the original Solace-only deployment)."""
    print("-- cosmos --")
    existing = _az(
        "cosmosdb", "sql", "container", "list",
        "--account-name", COSMOS_ACCOUNT, "-g", RESOURCE_GROUP, "-d", COSMOS_DATABASE,
        "--query", "[].id", "-o", "tsv",
    )
    existing_names = {line.rsplit("/", 1)[-1] for line in existing.splitlines()}

    for container in ("solace_requests", "mulesoft_requests", "btp_requests"):
        if container in existing_names:
            print(f"  {container} already exists, skipping")
            continue
        _az(
            "cosmosdb", "sql", "container", "create",
            "--account-name", COSMOS_ACCOUNT, "-g", RESOURCE_GROUP, "-d", COSMOS_DATABASE,
            "-n", container, "--partition-key-path", COSMOS_PARTITION_KEY,
            "--throughput", str(COSMOS_THROUGHPUT),
        )
        print(f"  created {container}")


def step_function_app() -> None:
    """Zip-deploy mcp-server/ to the live Function App. Always sets
    SCM_DO_BUILD_DURING_DEPLOYMENT first — without it, Linux Consumption Python apps skip the
    remote pip install and every import fails (every route 404s, including unrelated existing
    ones — this took down the working Solace pipeline for a few minutes during the real
    deployment). If the very first deploy after a dependency change 404s on every route, that's
    almost always an Oryx build that needs a second attempt, not a real code bug — see README."""
    print("-- function app --")
    _az(
        "functionapp", "config", "appsettings", "set",
        "-n", FUNCTION_APP_NAME, "-g", RESOURCE_GROUP,
        "--settings", "SCM_DO_BUILD_DURING_DEPLOYMENT=true",
    )

    zip_path = _HERE / "_build" / "mcp-server-deploy.zip"
    zip_path.parent.mkdir(exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for f in _MCP_SERVER_DIR.rglob("*"):
            if f.is_file() and "__pycache__" not in f.parts and f.suffix != ".pyc":
                zf.write(f, f.relative_to(_MCP_SERVER_DIR))
    print(f"  packaged {zip_path}")

    _az(
        "functionapp", "deployment", "source", "config-zip",
        "-n", FUNCTION_APP_NAME, "-g", RESOURCE_GROUP,
        "--src", str(zip_path), "--timeout", "900", "--build-remote", "true",
        timeout=900,
    )
    print("  deployed — verifying functions actually loaded (Oryx build can fail silently)...")
    time.sleep(20)
    _verify_function_app_healthy()


def _verify_function_app_healthy() -> None:
    """A failed remote build leaves the host running but with 0 functions loaded — every
    route 404s, old and new alike. Retry the deploy once automatically if this happens."""
    req = urllib.request.Request(
        f"{FUNCTION_APP_BASE_URL}/api/solace-mcp", method="POST",
        data=b'{"jsonrpc":"2.0","id":1,"method":"tools/list"}',
        headers={"Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            if resp.status == 200:
                print("  health check OK — functions are loaded")
                return
    except Exception:
        pass
    print("  health check FAILED (likely a transient Oryx build failure) — retrying deploy once")
    zip_path = _HERE / "_build" / "mcp-server-deploy.zip"
    _az(
        "functionapp", "deployment", "source", "config-zip",
        "-n", FUNCTION_APP_NAME, "-g", RESOURCE_GROUP,
        "--src", str(zip_path), "--timeout", "900", "--build-remote", "true",
        timeout=900,
    )
    time.sleep(20)
    with urllib.request.urlopen(req, timeout=30) as resp:
        if resp.status != 200:
            raise RuntimeError("function app still unhealthy after one retry — check Application "
                                "Insights traces/exceptions for the real import error")
    print("  health check OK on retry")


def step_agents() -> None:
    """Register/upgrade the 3 platform agents + their 6 generate/publish workflows."""
    print("-- agents + workflows --")
    env = os.environ.copy()
    env.setdefault("SOLACE_MCP_ENDPOINT", f"{FUNCTION_APP_BASE_URL}/api/solace-mcp")
    env.setdefault("MULESOFT_MCP_ENDPOINT", f"{FUNCTION_APP_BASE_URL}/api/mulesoft-mcp")
    env.setdefault("BTP_MCP_ENDPOINT", f"{FUNCTION_APP_BASE_URL}/api/btp-mcp")
    env.setdefault("FOUNDRY_PROJECT_ENDPOINT", FOUNDRY_PROJECT_ENDPOINT)
    result = subprocess.run(
        [sys.executable, str(_AGENT_DIR / "register.py"), "all", "all"],
        env=env, cwd=str(_REPO_ROOT),
    )
    if result.returncode != 0:
        raise RuntimeError("agent/register.py failed — see output above")


def _list_callback_url(workflow_name: str) -> str:
    out = _az(
        "rest", "--method", "POST",
        "--url", f"https://management.azure.com/subscriptions/{SUBSCRIPTION_ID}/resourceGroups/"
                 f"{RESOURCE_GROUP}/providers/Microsoft.Logic/workflows/{workflow_name}/"
                 "triggers/manual/listCallbackUrl?api-version=2019-05-01",
        "--query", "value", "-o", "tsv",
    )
    if not out.startswith("https://"):
        raise RuntimeError(
            f"listCallbackUrl for {workflow_name} did not return a real URL — the resource "
            "probably doesn't exist yet (deploy it before calling this for it)."
        )
    return out


def step_logic_apps() -> None:
    """Deploy the 4 Consumption Logic Apps: the 3 mail-trigger workflows, then the
    orchestrator wired to their real callback URLs. Location is hardcoded to
    LOGIC_APP_LOCATION (not resourceGroup().location) because the `teams` API connection only
    resolves to a Logic App in the SAME region — a resource-group-default-location deploy
    landed these in centralus once and every Teams action failed with ApiConnectionNotFound."""
    print("-- logic apps --")
    mail_triggers = [
        "solace-mail-trigger-workflow",
        "mulesoft-mail-trigger-workflow",
        "btp-mail-trigger-workflow",
    ]
    for name in mail_triggers:
        existing = subprocess.run(
            [_AZ_BIN, "resource", "show", "--ids",
             f"/subscriptions/{SUBSCRIPTION_ID}/resourceGroups/{RESOURCE_GROUP}"
             f"/providers/Microsoft.Logic/workflows/{name}"],
            capture_output=True, text=True,
        )
        if existing.returncode == 0:
            print(f"  {name} already exists, skipping")
            continue
        _az(
            "deployment", "group", "create", "-g", RESOURCE_GROUP,
            "--template-file", str(_LOGIC_APP_CONSUMPTION_DIR / f"{name}.json"),
            "--name", f"deploy-{name}-{int(time.time())}",
            "--parameters", f"teamsConnectionId={TEAMS_CONNECTION_ID}",
            timeout=300,
        )
        print(f"  deployed {name}")

    orchestrator_name = "3pl-onboarding-orchestrator-workflow"
    existing = subprocess.run(
        [_AZ_BIN, "resource", "show", "--ids",
         f"/subscriptions/{SUBSCRIPTION_ID}/resourceGroups/{RESOURCE_GROUP}"
         f"/providers/Microsoft.Logic/workflows/{orchestrator_name}"],
        capture_output=True, text=True,
    )
    callback_urls = {name: _list_callback_url(name) for name in mail_triggers}
    if existing.returncode == 0:
        print(f"  {orchestrator_name} already exists — redeploying to refresh callback URLs "
              "(harmless, same resource)")
    # Callback URLs contain `&` (SAS query string) — passed as a bare `key=value` CLI arg this
    # gets split into multiple commands by cmd.exe when az.CMD re-invokes through it on Windows.
    # A parameters file sidesteps cmd.exe's argument parsing entirely.
    params_path = _HERE / "_build" / "orchestrator-params.json"
    params_path.parent.mkdir(exist_ok=True)
    params_path.write_text(json.dumps({
        "solaceWorkflowCallbackUrl": {"value": callback_urls["solace-mail-trigger-workflow"]},
        "mulesoftWorkflowCallbackUrl": {"value": callback_urls["mulesoft-mail-trigger-workflow"]},
        "btpWorkflowCallbackUrl": {"value": callback_urls["btp-mail-trigger-workflow"]},
    }))
    _az(
        "deployment", "group", "create", "-g", RESOURCE_GROUP,
        "--template-file", str(_LOGIC_APP_CONSUMPTION_DIR / f"{orchestrator_name}.json"),
        "--name", f"deploy-orchestrator-{int(time.time())}",
        "--parameters", f"@{params_path}",
        timeout=300,
    )
    print(f"  deployed {orchestrator_name}")
    print("  NOTE: the `teams` connection still needs interactive authentication, and each "
          "workflow's Adaptive Card action still has placeholder Team/Channel IDs — see "
          "README.md 'Remaining manual steps'. Phase 1 (generate) works today; the approval "
          "card step will fail until those are done.")


def step_smoke_test() -> None:
    """Hit the 3 -generate routes only — never -publish (that would create a real commit/PR
    on a public GitHub repo, only ever appropriate after a real Teams approval)."""
    print("-- smoke test (generate only, no GitHub writes) --")
    cases = [
        ("solace-generate", {"from": "ops@smoketest.com", "subject": "smoke test",
                              "body": "Smoke-test Solace domain for SmokeTestCo, DE, dev, one event."}),
        ("mulesoft-generate", {"from": "ops@smoketest.com", "subject": "smoke test",
                                "body": "Smoke-test MuleSoft onboarding for SmokeTestCo DE."}),
        ("btp-generate", {"from": "ops@smoketest.com", "subject": "smoke test",
                           "body": "Smoke-test BTP subaccount for SmokeTestCo DE, region eu10."}),
    ]
    for route, body in cases:
        req = urllib.request.Request(
            f"{FUNCTION_APP_BASE_URL}/api/{route}", method="POST",
            data=json.dumps(body).encode(),
            headers={"Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                result = json.loads(resp.read())
                ok = "branchName" in result or "filePaths" in result
                print(f"  {route}: HTTP {resp.status} {'OK' if ok else 'unexpected shape'}")
        except urllib.error.HTTPError as e:
            print(f"  {route}: HTTP {e.code} FAILED — {e.read()[:300]}")


STEPS = {
    "branches": step_branches,
    "cosmos": step_cosmos,
    "function-app": step_function_app,
    "agents": step_agents,
    "logic-apps": step_logic_apps,
    "smoke-test": step_smoke_test,
}


def main() -> None:
    target = sys.argv[1] if len(sys.argv) > 1 else "all"
    if target == "all":
        for name, fn in STEPS.items():
            fn()
    elif target in STEPS:
        STEPS[target]()
    else:
        print(f"Unknown step '{target}'. Choices: all, {', '.join(STEPS)}")
        sys.exit(1)


if __name__ == "__main__":
    main()
