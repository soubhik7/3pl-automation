"""
get_solace_request.py — MCP tool: read back a pending Solace-request record by id.

Part of: 3pl-automation Solace-publisher feature
Layer:   mcp-server / tools

Purpose:  Reads a single document from the "solace_requests" Cosmos NoSQL container.
Used by:  update_solace_request_status.py (which the /api/solace-publish route calls
          after the Logic App's Teams approval resolves, to load the existing record
          before patching its status).
Depends:  lib/nosql_client.py.
"""
from lib.nosql_client import get_container


def get_solace_request(id: str, tenantId: str = "demo") -> dict | None:
    container = get_container("solace")
    try:
        return container.read_item(item=id, partition_key=tenantId)
    except Exception as e:
        if "404" in str(e) or "NotFound" in str(e):
            return None
        raise
