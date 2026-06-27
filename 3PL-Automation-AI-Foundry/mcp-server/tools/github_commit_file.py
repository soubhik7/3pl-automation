"""
github_commit_file.py — MCP tool: create or update a file on a branch.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features. Platform-agnostic —
         shared by all 3 publisher agents' Phase 2 ("publish") step.
Layer:   mcp-server / tools

Purpose:  Thin wrapper exposing lib.github_client.commit_file as an MCP tool.
Used by:  integration-pulse-{solace,mulesoft,btp}-publisher agents.
Depends:  lib/github_client.py (never raises — always returns a normal result dict).
"""
from lib.github_client import commit_file


def github_commit_file(branchName: str, path: str, content: str, message: str) -> dict:
    return commit_file(branchName, path, content, message)
