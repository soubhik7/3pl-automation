"""
github_get_file.py — MCP tool: read a file's current content from a branch, if present.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features. Platform-agnostic —
         shared by all 3 publisher agents' Phase 1 ("generate") step, alongside
         github_commit_file.py and github_open_pull_request.py (Phase 2's tools).
Layer:   mcp-server / tools

Purpose:  Thin wrapper exposing lib.github_client.get_file as an MCP tool. Read-only —
          calling this has no side effects, which is why it's the one tool allowed during
          Phase 1 (generation): an agent calls it to check whether a partner/slug's
          config already exists on the platform's branch (an update) or doesn't (a new
          onboarding), and if it does, to merge the requested change into the real
          current content instead of guessing at it.
Used by:  integration-pulse-{solace,mulesoft,btp}-publisher agents.
Depends:  lib/github_client.py (never raises — always returns a normal result dict).
"""
from lib.github_client import get_file


def github_get_file(branchName: str, path: str) -> dict:
    return get_file(branchName, path)
