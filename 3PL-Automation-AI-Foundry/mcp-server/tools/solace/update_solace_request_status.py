"""
update_solace_request_status.py — patch a Solace-request record's status (+ extra fields).

Part of: 3pl-automation Solace-publisher feature
Layer:   mcp-server / tools

Purpose:  Reads the existing record, merges in the new status and any extra fields
          (e.g. githubCommitUrl after a successful publish), and upserts it back.
Used by:  /api/solace-publish HTTP route, after the Logic App's Teams approval resolves.
Depends:  lib/nosql_client.py, get_solace_request.py.
"""
from lib.nosql_client import get_container
from tools.solace.get_solace_request import get_solace_request


def update_solace_request_status(id: str, tenantId: str, status: str, extra: dict | None = None) -> dict:
    record = get_solace_request(id, tenantId)
    if record is None:
        return {"status": "FAILED", "error": f"no solace_request found for id={id}"}
    record["status"] = status
    if extra:
        record.update(extra)
    container = get_container("solace")
    container.upsert_item(record)
    return {"status": "OK", "id": id}
