"""
update_mulesoft_request_status.py — patch a MuleSoft-request record's status (+ extra fields).

Part of: 3pl-automation MuleSoft-publisher feature
Layer:   mcp-server / tools

Purpose:  Reads the existing record, merges in the new status and any extra fields
          (e.g. githubCommitUrl/prUrl after a successful publish), and upserts it back.
Used by:  /api/mulesoft-publish HTTP route, after Approve/Reject is acted on.
Depends:  lib/nosql_client.py, get_mulesoft_request.py.
"""
from lib.nosql_client import get_container
from tools.mulesoft.get_mulesoft_request import get_mulesoft_request


def update_mulesoft_request_status(id: str, tenantId: str, status: str, extra: dict | None = None) -> dict:
    record = get_mulesoft_request(id, tenantId)
    if record is None:
        return {"status": "FAILED", "error": f"no mulesoft_request found for id={id}"}
    record["status"] = status
    if extra:
        record.update(extra)
    container = get_container("mulesoft")
    container.upsert_item(record)
    return {"status": "OK", "id": id}
