"""
save_mulesoft_request.py — MCP tool: persist a pending MuleSoft-request record.

Part of: 3pl-automation MuleSoft-publisher feature
Layer:   mcp-server / tools

Purpose:  Upserts a request-tracking document into the dedicated "mulesoft_requests"
          Cosmos NoSQL container (partition key /tenantId). Used right after Phase 1
          generates the app/dev/tst/prod YAML, before the Teams approval card is sent.
Used by:  /api/mulesoft-generate HTTP route (not the agent itself).
Depends:  lib/nosql_client.py.
"""
from lib.nosql_client import get_container


def save_mulesoft_request(record: dict) -> dict:
    container = get_container("mulesoft")
    container.upsert_item(record)
    return {"status": "OK", "id": record["id"]}
