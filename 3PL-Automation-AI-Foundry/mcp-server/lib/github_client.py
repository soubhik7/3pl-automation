"""
github_client.py — raw GitHub REST API client (no SDK), used by the github_* tools.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features (fully isolated from the
         main Integration Pulse agents/MCP servers). Platform-agnostic — shared by all 3
         publisher agents.
Layer:   mcp-server lib

Purpose:  Minimal wrapper over GitHub's REST API for the three operations these features
          need: create a branch off a base branch, create/update a file on a branch, and
          open a pull request from that branch back to a base branch. Uses stdlib urllib
          + a Bearer token, matching this codebase's existing no-SDK style for simple REST
          integrations (see index_knowledge.py).
Depends:  GITHUB_TOKEN, GITHUB_OWNER, GITHUB_REPO, GITHUB_BASE_BRANCH env vars.
Importance: Every function here must NEVER raise — callers (the github_* tools) return
            its result directly as the MCP tool result, and a raised exception would be
            converted into a JSON-RPC error, which kills the whole agent turn (see
            docs/troubleshooting.md §24 in the main repo for the full diagnosis of why
            this matters). All failure paths return a normal dict instead.
"""
import base64
import json
import os
import urllib.error
import urllib.request

_API = "https://api.github.com"


def _request(method: str, path: str, body: dict | None = None) -> tuple[int, dict]:
    token = os.environ.get("GITHUB_TOKEN", "")
    url = f"{_API}{path}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(
        url, data=data, method=method,
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "Content-Type": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, json.loads(resp.read() or b"{}")
    except urllib.error.HTTPError as e:
        try:
            err_body = json.loads(e.read())
        except Exception:
            err_body = {"message": str(e)}
        return e.code, err_body


def get_branch_sha(branch: str) -> dict:
    owner = os.environ.get("GITHUB_OWNER", "")
    repo = os.environ.get("GITHUB_REPO", "")
    status, body = _request("GET", f"/repos/{owner}/{repo}/git/ref/heads/{branch}")
    if status != 200:
        return {"status": "FAILED", "error": body.get("message", f"HTTP {status}")}
    return {"status": "OK", "sha": body["object"]["sha"]}


def create_branch(new_branch: str, base_branch: str | None = None) -> dict:
    owner = os.environ.get("GITHUB_OWNER", "")
    repo = os.environ.get("GITHUB_REPO", "")
    base = base_branch or os.environ.get("GITHUB_BASE_BRANCH", "main")

    base_ref = get_branch_sha(base)
    if base_ref["status"] != "OK":
        return {"status": "FAILED", "error": f"could not read base branch '{base}': {base_ref['error']}"}

    status, body = _request(
        "POST", f"/repos/{owner}/{repo}/git/refs",
        {"ref": f"refs/heads/{new_branch}", "sha": base_ref["sha"]},
    )
    if status not in (200, 201):
        # 422 with "Reference already exists" is fine — branch is already there, reuse it.
        if status == 422 and "already exists" in body.get("message", ""):
            return {"status": "OK", "branch": new_branch, "alreadyExisted": True}
        return {"status": "FAILED", "error": body.get("message", f"HTTP {status}")}
    return {"status": "OK", "branch": new_branch, "alreadyExisted": False}


def commit_file(branch: str, path: str, content: str, message: str) -> dict:
    owner = os.environ.get("GITHUB_OWNER", "")
    repo = os.environ.get("GITHUB_REPO", "")
    encoded = base64.b64encode(content.encode()).decode()

    # Look up existing file SHA on this branch (required by the Contents API for updates;
    # absent for a brand-new file, which is fine — we just omit "sha" in that case).
    status, existing = _request("GET", f"/repos/{owner}/{repo}/contents/{path}?ref={branch}")
    payload = {"message": message, "content": encoded, "branch": branch}
    if status == 200 and isinstance(existing, dict) and "sha" in existing:
        payload["sha"] = existing["sha"]

    status, body = _request("PUT", f"/repos/{owner}/{repo}/contents/{path}", payload)
    if status not in (200, 201):
        return {"status": "FAILED", "error": body.get("message", f"HTTP {status}")}
    return {
        "status": "OK",
        "commitUrl": body.get("commit", {}).get("html_url", ""),
        "contentUrl": body.get("content", {}).get("html_url", ""),
    }


def create_pull_request(head_branch: str, base_branch: str, title: str, body: str = "") -> dict:
    owner = os.environ.get("GITHUB_OWNER", "")
    repo = os.environ.get("GITHUB_REPO", "")
    base = base_branch or os.environ.get("GITHUB_BASE_BRANCH", "main")

    status, resp = _request(
        "POST", f"/repos/{owner}/{repo}/pulls",
        {"title": title, "body": body, "head": head_branch, "base": base},
    )
    if status not in (200, 201):
        # 422 "A pull request already exists" is idempotent-equivalent to create_branch's
        # "Reference already exists" handling — reuse the existing PR rather than erroring.
        if status == 422 and "already exists" in resp.get("message", "").lower():
            return {"status": "OK", "alreadyExisted": True, "prUrl": "", "prNumber": None}
        return {"status": "FAILED", "error": resp.get("message", f"HTTP {status}")}
    return {
        "status": "OK",
        "alreadyExisted": False,
        "prUrl": resp.get("html_url", ""),
        "prNumber": resp.get("number"),
    }
