"""
save_btp_request.py — MCP tool: persist a pending BTP-request record.

Part of: 3pl-automation BTP-publisher feature
Layer:   mcp-server / tools

Purpose:  Upserts a request-tracking document into the dedicated "btp_requests" Cosmos
          NoSQL container (partition key /tenantId). Used right after Phase 1 generates
          btp_config.yaml (+ optional manifest.yml), before the Teams approval card is
          sent.
Used by:  /api/btp-generate HTTP route (not the agent itself).
Depends:  lib/nosql_client.py.
"""
from lib.nosql_client import get_container


def save_btp_request(record: dict) -> dict:
    container = get_container("btp")
    container.upsert_item(record)
    return {"status": "OK", "id": record["id"]}
