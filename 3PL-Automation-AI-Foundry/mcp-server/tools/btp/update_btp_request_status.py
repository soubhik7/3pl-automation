"""
update_btp_request_status.py — patch a BTP-request record's status (+ extra fields).

Part of: 3pl-automation BTP-publisher feature
Layer:   mcp-server / tools

Purpose:  Reads the existing record, merges in the new status and any extra fields
          (e.g. githubCommitUrl/prUrl after a successful publish), and upserts it back.
Used by:  /api/btp-publish HTTP route, after Approve/Reject is acted on.
Depends:  lib/nosql_client.py, get_btp_request.py.
"""
from lib.nosql_client import get_container
from tools.btp.get_btp_request import get_btp_request


def update_btp_request_status(id: str, tenantId: str, status: str, extra: dict | None = None) -> dict:
    record = get_btp_request(id, tenantId)
    if record is None:
        return {"status": "FAILED", "error": f"no btp_request found for id={id}"}
    record["status"] = status
    if extra:
        record.update(extra)
    container = get_container("btp")
    container.upsert_item(record)
    return {"status": "OK", "id": id}
