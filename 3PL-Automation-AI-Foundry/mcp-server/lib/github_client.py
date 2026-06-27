"""
github_client.py — raw GitHub REST API client (no SDK), used by the github_* tools.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features (fully isolated from the
         main Integration Pulse agents/MCP servers). Platform-agnostic — shared by all 3
         publisher agents.
Layer:   mcp-server lib

Purpose:  Minimal wrapper over GitHub's REST API for the two operations these features
          need: create/update a file on a platform's persistent feature branch, and open
          (or reuse) a pull request from that branch back to main. There is no
          create-branch operation — each platform's feature branch (solace/onboarding,
          mulesoft/onboarding, btp/onboarding) is created ONCE as a manual setup step
          (see ../../README.md "One-time setup"), not by the automated flow. Every
          onboarding request just commits to its platform's existing branch (one file per
          partner/slug, so concurrent requests never collide) and ensures a PR is open.
Auth:     No PAT/long-lived token is configured anywhere. This Function App authenticates
          to GitHub as a GitHub App: its private key lives in Azure Key Vault (never in an
          App Setting), fetched at call time via this Function App's system-assigned
          Managed Identity, then exchanged for a short-lived (1 hour) installation access
          token. The installation token is cached in memory and refreshed automatically —
          callers never see or manage a token directly.
Depends:  GITHUB_OWNER, GITHUB_REPO, GITHUB_BASE_BRANCH env vars; GITHUB_APP_ID,
          GITHUB_APP_INSTALLATION_ID, GITHUB_APP_PRIVATE_KEY_VAULT_URL,
          GITHUB_APP_PRIVATE_KEY_SECRET_NAME env vars (GitHub App auth — see README's env
          var table); PyJWT, cryptography, azure-keyvault-secrets, azure-identity.
Importance: Every function here must NEVER raise — callers (the github_* tools) return
            its result directly as the MCP tool result, and a raised exception would be
            converted into a JSON-RPC error, which kills the whole agent turn (see
            docs/troubleshooting.md §24 in the main repo for the full diagnosis of why
            this matters). All failure paths return a normal dict instead — including
            failures to acquire an installation token.
"""
import base64
import json
import os
import time
import urllib.error
import urllib.request

import jwt
from azure.identity import DefaultAzureCredential
from azure.keyvault.secrets import SecretClient

_API = "https://api.github.com"

_APP_ID = os.environ.get("GITHUB_APP_ID", "")
_INSTALLATION_ID = os.environ.get("GITHUB_APP_INSTALLATION_ID", "")
_KEY_VAULT_URL = os.environ.get("GITHUB_APP_PRIVATE_KEY_VAULT_URL", "")
_PRIVATE_KEY_SECRET_NAME = os.environ.get("GITHUB_APP_PRIVATE_KEY_SECRET_NAME", "github-app-private-key")

_secret_client = None
_cached_token = None
_cached_token_expiry = 0.0


def _get_secret_client() -> SecretClient:
    global _secret_client
    if _secret_client is None:
        _secret_client = SecretClient(vault_url=_KEY_VAULT_URL, credential=DefaultAzureCredential())
    return _secret_client


def _get_installation_token() -> str:
    """Returns a cached GitHub App installation access token, minting a new one if the
    cached one is missing or about to expire. Raises on failure — callers (_request)
    catch this and convert it to a normal FAILED result, same as any other GitHub error."""
    global _cached_token, _cached_token_expiry
    now = time.time()
    if _cached_token and now < _cached_token_expiry - 60:
        return _cached_token

    private_key = _get_secret_client().get_secret(_PRIVATE_KEY_SECRET_NAME).value
    issued_at = int(now)
    app_jwt = jwt.encode(
        {"iat": issued_at - 60, "exp": issued_at + 540, "iss": _APP_ID},
        private_key, algorithm="RS256",
    )

    req = urllib.request.Request(
        f"{_API}/app/installations/{_INSTALLATION_ID}/access_tokens",
        data=b"", method="POST",
        headers={"Authorization": f"Bearer {app_jwt}", "Accept": "application/vnd.github+json"},
    )
    with urllib.request.urlopen(req) as resp:
        token_body = json.loads(resp.read())

    _cached_token = token_body["token"]
    _cached_token_expiry = now + 3600  # installation tokens are valid for 1 hour
    return _cached_token


def _request(method: str, path: str, body: dict | None = None) -> tuple[int, dict]:
    try:
        token = _get_installation_token()
    except Exception as e:
        return 0, {"message": f"could not acquire GitHub App installation token: {e}"}

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
        # 422 "A pull request already exists" means this platform's persistent branch
        # already has an open PR — reuse it rather than erroring. This is the common case
        # after the first request on a given branch: every later commit just adds to the
        # same long-running PR.
        if status == 422 and "already exists" in resp.get("message", "").lower():
            return {"status": "OK", "alreadyExisted": True, "prUrl": "", "prNumber": None}
        return {"status": "FAILED", "error": resp.get("message", f"HTTP {status}")}
    return {
        "status": "OK",
        "alreadyExisted": False,
        "prUrl": resp.get("html_url", ""),
        "prNumber": resp.get("number"),
    }
