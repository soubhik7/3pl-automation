"""
register.py — self-contained registration for the 3 platform publisher agents
(solace/mulesoft/btp) + their generate/publish workflow pairs. Does NOT import or modify
ops/client/agents.py — zero risk to the existing 8 agents or 5 workflows in the main app.

Usage:
    python 3pl-automation/agent/register.py agent      [solace|mulesoft|btp|all]
    python 3pl-automation/agent/register.py workflows  [solace|mulesoft|btp|all]
    python 3pl-automation/agent/register.py all        [solace|mulesoft|btp|all]

Platform defaults to "all" if omitted.
"""
import os
import pathlib
import subprocess
import sys

import requests
import yaml

_HERE = pathlib.Path(__file__).parent
_REPO_ROOT = _HERE.parent.parent
_TEMPLATES_DIR = _REPO_ROOT / "3pl-automation" / "templates"

_PROJECT_ENDPOINT = os.environ.get(
    "FOUNDRY_PROJECT_ENDPOINT",
    "https://integration-pulse-found-resource.services.ai.azure.com/api/projects/integration-pulse-proj",
)
_API_VERSION = "2025-05-15-preview"

PLATFORMS = {
    "solace": {
        "agent_name": "integration-pulse-solace-publisher",
        "dir": "solace",
        "template": "solace-template.json",
        "mcp_endpoint_env": "SOLACE_MCP_ENDPOINT",
        "model_env": "SOLACE_PUBLISHER_MODEL",
        "server_label": "solace-mcp",
        "allowed_tools": ["github_get_file", "github_commit_file", "github_open_pull_request"],
        "description": "Two-phase Solace config generator + publisher. Phase 1 generates a "
                        "masked-credential Solace JSON from an email request. Phase 2, only "
                        "after human approval, pushes it to a new GitHub branch and opens a PR.",
    },
    "mulesoft": {
        "agent_name": "integration-pulse-mulesoft-publisher",
        "dir": "mulesoft",
        "template": "mulesoft-template.yaml",
        "mcp_endpoint_env": "MULESOFT_MCP_ENDPOINT",
        "model_env": "MULESOFT_PUBLISHER_MODEL",
        "server_label": "mulesoft-mcp",
        "allowed_tools": ["github_get_file", "github_commit_file", "github_open_pull_request"],
        "description": "Two-phase MuleSoft onboarding YAML generator + publisher. Phase 1 "
                        "generates masked-credential app/dev/tst/prod YAML from an email "
                        "request. Phase 2, only after human approval, pushes them to a new "
                        "GitHub branch and opens a PR.",
    },
    "btp": {
        "agent_name": "integration-pulse-btp-publisher",
        "dir": "btp",
        "template": "btp-template.yaml",
        "manifest_template": "btp-manifest-template.yml",
        "mcp_endpoint_env": "BTP_MCP_ENDPOINT",
        "model_env": "BTP_PUBLISHER_MODEL",
        "server_label": "btp-mcp",
        "allowed_tools": ["github_get_file", "github_commit_file", "github_open_pull_request"],
        "description": "Two-phase BTP onboarding config generator + publisher. Phase 1 "
                        "generates a masked-credential btp_config.yaml (and optional "
                        "manifest.yml) from an email request. Phase 2, only after human "
                        "approval, pushes them to a new GitHub branch and opens a PR.",
    },
}


def _bearer_headers() -> dict:
    token = os.environ.get("FOUNDRY_BEARER")
    if not token:
        token = subprocess.check_output(
            ["az", "account", "get-access-token", "--resource", "https://ai.azure.com",
             "--query", "accessToken", "-o", "tsv"],
            text=True,
        ).strip()
    return {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}


def register_agent(platform: str) -> None:
    cfg = PLATFORMS[platform]
    prompt_text = (_HERE / cfg["dir"] / "system-prompt.md").read_text(encoding="utf-8")

    template_text = (_TEMPLATES_DIR / cfg["template"]).read_text(encoding="utf-8")
    instructions = prompt_text.replace("{{TEMPLATE}}", template_text.strip())

    if "manifest_template" in cfg:
        manifest_text = (_TEMPLATES_DIR / cfg["manifest_template"]).read_text(encoding="utf-8")
        instructions = instructions.replace("{{MANIFEST_TEMPLATE}}", manifest_text.strip())

    model = os.environ.get(cfg["model_env"], "gpt-4.1")
    mcp_endpoint = os.environ[cfg["mcp_endpoint_env"]]  # required — fail fast if not set

    payload = {
        "definition": {
            "kind": "prompt",
            "model": model,
            "description": cfg["description"],
            "instructions": instructions,
            "tools": [{
                "type": "mcp",
                "server_label": cfg["server_label"],
                "server_url": mcp_endpoint,
                "allowed_tools": cfg["allowed_tools"],
                "require_approval": "never",
            }],
        }
    }
    name = cfg["agent_name"]
    r = requests.post(
        f"{_PROJECT_ENDPOINT}/agents/{name}/versions",
        headers=_bearer_headers(), params={"api-version": _API_VERSION}, json=payload, timeout=30,
    )
    if not r.ok:
        print(f"  FAILED registering agent: {r.status_code} {r.text[:600]}")
        sys.exit(1)
    print(f"  Registered agent '{name}' -> version {r.json().get('version', '?')}")


def register_workflow(platform: str, stem: str, name: str) -> None:
    cfg = PLATFORMS[platform]
    raw = (_HERE / cfg["dir"] / "workflows" / f"{stem}.yaml").read_text(encoding="utf-8")
    yaml.safe_load(raw)  # fail fast on syntax errors

    payload = {
        "name": name,
        "description": f"Single-step workflow for the {platform}-publisher agent ({stem}).",
        "version": "1.0",
        "definition": {"kind": "workflow", "id": "", "name": name, "workflow": raw},
    }
    r = requests.post(
        f"{_PROJECT_ENDPOINT}/agents/{name}/versions",
        headers=_bearer_headers(), params={"api-version": _API_VERSION}, json=payload, timeout=30,
    )
    if not r.ok:
        print(f"  FAILED registering workflow '{name}': {r.status_code} {r.text[:600]}")
        sys.exit(1)
    print(f"  Registered workflow '{name}' -> version {r.json().get('version', '?')}")


def main() -> None:
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    platform_arg = sys.argv[2] if len(sys.argv) > 2 else "all"
    platforms = list(PLATFORMS) if platform_arg == "all" else [platform_arg]

    for platform in platforms:
        print(f"-- {platform} --")
        if mode in ("agent", "all"):
            register_agent(platform)
        if mode in ("workflows", "all"):
            register_workflow(platform, f"{platform}-generate-workflow", f"{platform}-generate-workflow")
            register_workflow(platform, f"{platform}-publish-workflow", f"{platform}-publish-workflow")


if __name__ == "__main__":
    main()
