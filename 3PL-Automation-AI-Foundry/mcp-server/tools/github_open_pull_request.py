"""
github_open_pull_request.py — MCP tool: open a PR from a branch back to a base branch.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features. Platform-agnostic — shared
         by all 3 publisher agents' Phase 2 ("publish") step, alongside
         github_commit_file.py. There is no github_create_branch.py — each platform
         commits to its own persistent feature branch, created once, manually.
Layer:   mcp-server / tools

Purpose:  Thin wrapper exposing lib.github_client.create_pull_request as an MCP tool.
Used by:  integration-pulse-{solace,mulesoft,btp}-publisher agents.
Depends:  lib/github_client.py (never raises — always returns a normal result dict).
"""
from lib.github_client import create_pull_request


def github_open_pull_request(headBranch: str, baseBranch: str, title: str, body: str = "") -> dict:
    return create_pull_request(headBranch, baseBranch, title, body)
