"""
github_create_branch.py — MCP tool: create a new branch off the configured base branch.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features. Platform-agnostic —
         shared by all 3 publisher agents' Phase 2 ("publish") step.
Layer:   mcp-server / tools

Purpose:  Thin wrapper exposing lib.github_client.create_branch as an MCP tool.
Used by:  integration-pulse-{solace,mulesoft,btp}-publisher agents.
Depends:  lib/github_client.py (never raises — always returns a normal result dict).
"""
from lib.github_client import create_branch


def github_create_branch(branchName: str) -> dict:
    return create_branch(branchName)
