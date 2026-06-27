"""
save_solace_request.py — MCP tool: persist a pending Solace-request record.

Part of: 3pl-automation Solace-publisher feature
Layer:   mcp-server / tools

Purpose:  Upserts a request-tracking document into the dedicated "solace_requests"
          Cosmos NoSQL container (partition key /tenantId). Used right after Phase 1
          generates a Solace JSON, before the Teams approval card is sent.
Used by:  /api/solace-intake HTTP route (not the agent itself).
Depends:  lib/nosql_client.py.
"""
from lib.nosql_client import get_container


def save_solace_request(record: dict) -> dict:
    container = get_container("solace")
    container.upsert_item(record)
    return {"status": "OK", "id": record["id"]}
