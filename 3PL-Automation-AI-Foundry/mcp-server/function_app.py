"""
function_app.py — Azure Function entry point for the shared MCP server.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features (fully isolated from the
         main Integration Pulse agents/MCP servers/Toolbox/ui-gateway/agent-host)
Layer:   mcp-server (Execution Plane) + lightweight orchestration

Purpose:  Hosts MCP tool servers + HTTP routes for all 3 onboarding platforms (Solace,
          MuleSoft, BTP) behind one shared Function App.
          1. Three MCP JSON-RPC tool servers (solace-mcp, mulesoft-mcp, btp-mcp), each
             exposing the same 2 tools (github_commit_file, github_open_pull_request) —
             served on separate routes because each platform's agent.yaml connects to its
             own server_label, even though the tool implementations and this Function App
             are shared across all 3. There is no branch-creation tool — each platform
             commits to its own persistent feature branch (solace/onboarding,
             mulesoft/onboarding, btp/onboarding), created once as a manual setup step,
             never by the agent.
          2. Six plain HTTP routes that the Logic App calls directly:
             /api/{platform}-generate (Phase 1 — returns the generated config
             synchronously so the Logic App can show it in a Teams "post adaptive card
             and wait for a response" action) and /api/{platform}-publish (Phase 2 —
             called only after the Logic App's own Teams wait-for-response step resolves
             to Approve), for platform in (solace, mulesoft, btp).
             The Teams connection itself, the approval card, and the wait/branch logic
             all live in the Logic App, not here — there is no webhook URL, approval
             token, or decision endpoint on this side.
Used by:  The Logic App (solace/mulesoft/btp -generate/-publish routes) and the 3
          publisher agents via MCP (github_commit_file/github_open_pull_request).
Depends:  lib/github_client.py, lib/nosql_client.py, lib/foundry_client.py,
          lib/json_extract.py, lib/yaml_extract.py, tools/*.
Importance: This is the only bridge between the email trigger, the agents, and GitHub
            for these features. Cosmos is used for an audit trail only — the approval
            flow itself does not depend on it (the Logic App run holds that state).
"""
import json
import logging
import uuid
from datetime import datetime, timezone

import azure.functions as func

from lib.foundry_client import invoke_workflow
from lib.json_extract import extract_json
from lib.yaml_extract import validate_yaml
from tools.github_commit_file import github_commit_file
from tools.github_open_pull_request import github_open_pull_request
from tools.solace.save_solace_request import save_solace_request
from tools.solace.update_solace_request_status import update_solace_request_status
from tools.mulesoft.save_mulesoft_request import save_mulesoft_request
from tools.mulesoft.update_mulesoft_request_status import update_mulesoft_request_status
from tools.btp.save_btp_request import save_btp_request
from tools.btp.update_btp_request_status import update_btp_request_status

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)

TOOLS = [
    {"name": "github_commit_file", "inputSchema": {"type": "object", "properties": {
        "branchName": {"type": "string", "description": "This platform's persistent feature branch, e.g. solace/onboarding"},
        "path": {"type": "string", "description": "e.g. solace-automation/config/3pl/acme-orders-dev.json"},
        "content": {"type": "string", "description": "Full file content (pretty-printed JSON or raw YAML text)"},
        "message": {"type": "string", "description": "Commit message"},
    }, "required": ["branchName", "path", "content", "message"], "additionalProperties": False}},

    {"name": "github_open_pull_request", "inputSchema": {"type": "object", "properties": {
        "headBranch": {"type": "string"},
        "baseBranch": {"type": "string", "description": "Defaults to main if omitted"},
        "title": {"type": "string"},
        "body": {"type": "string", "description": "PR description (Markdown), optional"},
    }, "required": ["headBranch", "title"], "additionalProperties": False}},
]


def _call_tool(name: str, args: dict):
    if name == "github_commit_file":
        return github_commit_file(args["branchName"], args["path"], args["content"], args["message"])
    if name == "github_open_pull_request":
        return github_open_pull_request(
            args["headBranch"], args.get("baseBranch", "main"), args["title"], args.get("body", "")
        )
    raise ValueError(f"Unknown tool: {name}")


def _sse(rpc_id, result=None, error=None) -> func.HttpResponse:
    body = {"jsonrpc": "2.0", "id": rpc_id}
    if error:
        body["error"] = error
    else:
        body["result"] = result
    data = json.dumps(body, default=str)
    return func.HttpResponse(
        body=f"event: message\ndata: {data}\n\n",
        status_code=200,
        headers={"Content-Type": "text/event-stream; charset=utf-8"},
    )


def _mcp_handler(req: func.HttpRequest, server_name: str) -> func.HttpResponse:
    """Shared MCP JSON-RPC handler behind all 3 platform routes (solace-mcp/mulesoft-mcp/
    btp-mcp) — identical logic, differing only in server_name (used in the initialize
    response) since each platform's agent.yaml connects to its own server_label even
    though the tool implementations are shared. Never raises — every error is converted
    to a JSON-RPC error object."""
    try:
        body = req.get_json()
    except ValueError:
        return _sse(None, error={"code": -32700, "message": "Parse error"})

    rpc_id = body.get("id")
    method = body.get("method", "")

    try:
        if method == "initialize":
            return _sse(rpc_id, {
                "protocolVersion": "2024-11-05",
                "capabilities": {"tools": {}},
                "serverInfo": {"name": server_name, "version": "1.0.0"},
            })
        if method in ("notifications/initialized", "ping"):
            return func.HttpResponse(status_code=202)
        if method == "tools/list":
            return _sse(rpc_id, {"tools": TOOLS})
        if method == "tools/call":
            params = body.get("params", {})
            tool_name = params.get("name")
            tool_args = params.get("arguments", {})
            result = _call_tool(tool_name, tool_args)
            return _sse(rpc_id, {"content": [{"type": "text", "text": json.dumps(result, default=str)}]})
        return _sse(rpc_id, error={"code": -32601, "message": f"Method not found: {method}"})
    except Exception as exc:
        logging.exception("mcp tool error")
        return _sse(rpc_id, error={"code": -32603, "message": str(exc)})


@app.route(route="solace-mcp/{*path}", methods=["GET", "POST", "DELETE"])
def solace_mcp(req: func.HttpRequest) -> func.HttpResponse:
    return _mcp_handler(req, "solace-mcp")


@app.route(route="mulesoft-mcp/{*path}", methods=["GET", "POST", "DELETE"])
def mulesoft_mcp(req: func.HttpRequest) -> func.HttpResponse:
    return _mcp_handler(req, "mulesoft-mcp")


@app.route(route="btp-mcp/{*path}", methods=["GET", "POST", "DELETE"])
def btp_mcp(req: func.HttpRequest) -> func.HttpResponse:
    return _mcp_handler(req, "btp-mcp")


# ---------------------------------------------------------------------------
# Solace
# ---------------------------------------------------------------------------

@app.route(route="solace-generate", methods=["POST"])
def solace_generate(req: func.HttpRequest) -> func.HttpResponse:
    """Phase 1. Called by the Logic App right after the mail trigger. Body:
    {from, subject, body}. Returns {id, solaceJson, branchName, filePath,
    summaryForApproval} synchronously — the Logic App uses this directly to populate
    its Teams "post adaptive card and wait for a response" action."""
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON body", status_code=400)

    from_email = body.get("from", "")
    subject = body.get("subject", "")
    email_body = body.get("body", "")

    phase1_input = json.dumps({
        "phase": "generate",
        "emailFrom": from_email,
        "emailSubject": subject,
        "emailBody": email_body,
    })

    try:
        reply_text = invoke_workflow("solace-generate-workflow", phase1_input)
        generated = extract_json(reply_text)
    except Exception as e:
        logging.exception("solace-generate: Phase 1 generation failed")
        return func.HttpResponse(json.dumps({"status": "FAILED", "error": str(e)}),
                                  status_code=500, mimetype="application/json")

    request_id = str(uuid.uuid4())
    record = {
        "id": request_id,
        "tenantId": "demo",
        "status": "GENERATED",  # audit trail only — the Logic App run holds approval state
        "fromEmail": from_email,
        "subject": subject,
        "requestText": email_body,
        "solaceJson": generated.get("solaceJson"),
        "branchName": generated.get("branchName"),
        "filePath": generated.get("filePath"),
        "summaryForApproval": generated.get("summaryForApproval", ""),
        "createdAt": datetime.now(timezone.utc).isoformat(),
    }
    save_solace_request(record)

    return func.HttpResponse(
        json.dumps({
            "id": request_id,
            "solaceJson": generated.get("solaceJson"),
            "branchName": generated.get("branchName"),
            "filePath": generated.get("filePath"),
            "summaryForApproval": generated.get("summaryForApproval", ""),
        }),
        status_code=200, mimetype="application/json",
    )


@app.route(route="solace-publish", methods=["POST"])
def solace_publish(req: func.HttpRequest) -> func.HttpResponse:
    """Phase 2. Called by the Logic App only after its Teams wait-for-response action
    resolves to Approve. Body: {id, solaceJson, branchName, filePath} — the exact
    values returned from /api/solace-generate, round-tripped by the Logic App."""
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON body", status_code=400)

    request_id = body.get("id", "")
    publish_input = json.dumps({
        "phase": "publish",
        "solaceJson": body.get("solaceJson"),
        "branchName": body.get("branchName"),
        "filePath": body.get("filePath"),
    })

    try:
        reply_text = invoke_workflow("solace-publish-workflow", publish_input)
        result = extract_json(reply_text)
    except Exception as e:
        logging.exception("solace-publish: Phase 2 publish failed")
        if request_id:
            update_solace_request_status(request_id, "demo", "FAILED", {"error": str(e)})
        return func.HttpResponse(json.dumps({"status": "FAILED", "error": str(e)}),
                                  status_code=500, mimetype="application/json")

    if result.get("status") == "PUBLISHED" and request_id:
        update_solace_request_status(request_id, "demo", "PUBLISHED", {
            "githubBranch": result.get("branch"),
            "githubCommitUrl": result.get("commitUrl"),
            "githubPrUrl": result.get("prUrl"),
        })
    elif request_id:
        update_solace_request_status(request_id, "demo", "FAILED", {"error": result.get("error", "unknown")})

    status_code = 200 if result.get("status") == "PUBLISHED" else 500
    return func.HttpResponse(json.dumps(result), status_code=status_code, mimetype="application/json")


# ---------------------------------------------------------------------------
# MuleSoft
# ---------------------------------------------------------------------------

@app.route(route="mulesoft-generate", methods=["POST"])
def mulesoft_generate(req: func.HttpRequest) -> func.HttpResponse:
    """Phase 1. Called by the Logic App right after the mail trigger. Body:
    {from, subject, body}. Returns {id, mulesoftYaml, branchName, filePaths,
    summaryForApproval} synchronously — the Logic App uses this directly to populate
    its Teams "post adaptive card and wait for a response" action (one block per
    app/dev/tst/prod YAML file)."""
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON body", status_code=400)

    from_email = body.get("from", "")
    subject = body.get("subject", "")
    email_body = body.get("body", "")

    phase1_input = json.dumps({
        "phase": "generate",
        "emailFrom": from_email,
        "emailSubject": subject,
        "emailBody": email_body,
    })

    try:
        reply_text = invoke_workflow("mulesoft-generate-workflow", phase1_input)
        generated = extract_json(reply_text)
    except Exception as e:
        logging.exception("mulesoft-generate: Phase 1 generation failed")
        return func.HttpResponse(json.dumps({"status": "FAILED", "error": str(e)}),
                                  status_code=500, mimetype="application/json")

    mulesoft_yaml = generated.get("mulesoftYaml", {})
    for key, text in mulesoft_yaml.items():
        if not validate_yaml(text):
            logging.warning("mulesoft-generate: %s did not parse as valid YAML", key)

    request_id = str(uuid.uuid4())
    record = {
        "id": request_id,
        "tenantId": "demo",
        "status": "GENERATED",  # audit trail only — the Logic App run holds approval state
        "fromEmail": from_email,
        "subject": subject,
        "requestText": email_body,
        "mulesoftYaml": mulesoft_yaml,
        "branchName": generated.get("branchName"),
        "filePaths": generated.get("filePaths"),
        "summaryForApproval": generated.get("summaryForApproval", ""),
        "createdAt": datetime.now(timezone.utc).isoformat(),
    }
    save_mulesoft_request(record)

    return func.HttpResponse(
        json.dumps({
            "id": request_id,
            "mulesoftYaml": mulesoft_yaml,
            "branchName": generated.get("branchName"),
            "filePaths": generated.get("filePaths"),
            "summaryForApproval": generated.get("summaryForApproval", ""),
        }),
        status_code=200, mimetype="application/json",
    )


@app.route(route="mulesoft-publish", methods=["POST"])
def mulesoft_publish(req: func.HttpRequest) -> func.HttpResponse:
    """Phase 2. Called by the Logic App only after its Teams wait-for-response action
    resolves to Approve. Body: {id, mulesoftYaml, branchName, filePaths} — the exact
    values returned from /api/mulesoft-generate, round-tripped by the Logic App."""
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON body", status_code=400)

    request_id = body.get("id", "")
    publish_input = json.dumps({
        "phase": "publish",
        "mulesoftYaml": body.get("mulesoftYaml"),
        "branchName": body.get("branchName"),
        "filePaths": body.get("filePaths"),
    })

    try:
        reply_text = invoke_workflow("mulesoft-publish-workflow", publish_input)
        result = extract_json(reply_text)
    except Exception as e:
        logging.exception("mulesoft-publish: Phase 2 publish failed")
        if request_id:
            update_mulesoft_request_status(request_id, "demo", "FAILED", {"error": str(e)})
        return func.HttpResponse(json.dumps({"status": "FAILED", "error": str(e)}),
                                  status_code=500, mimetype="application/json")

    if result.get("status") == "PUBLISHED" and request_id:
        update_mulesoft_request_status(request_id, "demo", "PUBLISHED", {
            "githubBranch": result.get("branch"),
            "githubCommitUrls": result.get("commitUrls"),
            "githubPrUrl": result.get("prUrl"),
        })
    elif request_id:
        update_mulesoft_request_status(request_id, "demo", "FAILED", {"error": result.get("error", "unknown")})

    status_code = 200 if result.get("status") == "PUBLISHED" else 500
    return func.HttpResponse(json.dumps(result), status_code=status_code, mimetype="application/json")


# ---------------------------------------------------------------------------
# BTP
# ---------------------------------------------------------------------------

@app.route(route="btp-generate", methods=["POST"])
def btp_generate(req: func.HttpRequest) -> func.HttpResponse:
    """Phase 1. Called by the Logic App right after the mail trigger. Body:
    {from, subject, body}. Returns {id, btpConfigYaml, manifestYaml, branchName,
    filePaths, summaryForApproval} synchronously — the Logic App uses this directly to
    populate its Teams "post adaptive card and wait for a response" action. manifestYaml
    is absent unless a Cloud Foundry app deployment was requested."""
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON body", status_code=400)

    from_email = body.get("from", "")
    subject = body.get("subject", "")
    email_body = body.get("body", "")

    phase1_input = json.dumps({
        "phase": "generate",
        "emailFrom": from_email,
        "emailSubject": subject,
        "emailBody": email_body,
    })

    try:
        reply_text = invoke_workflow("btp-generate-workflow", phase1_input)
        generated = extract_json(reply_text)
    except Exception as e:
        logging.exception("btp-generate: Phase 1 generation failed")
        return func.HttpResponse(json.dumps({"status": "FAILED", "error": str(e)}),
                                  status_code=500, mimetype="application/json")

    if not validate_yaml(generated.get("btpConfigYaml", "")):
        logging.warning("btp-generate: btpConfigYaml did not parse as valid YAML")
    if "manifestYaml" in generated and not validate_yaml(generated["manifestYaml"]):
        logging.warning("btp-generate: manifestYaml did not parse as valid YAML")

    request_id = str(uuid.uuid4())
    record = {
        "id": request_id,
        "tenantId": "demo",
        "status": "GENERATED",  # audit trail only — the Logic App run holds approval state
        "fromEmail": from_email,
        "subject": subject,
        "requestText": email_body,
        "btpConfigYaml": generated.get("btpConfigYaml"),
        "manifestYaml": generated.get("manifestYaml"),
        "branchName": generated.get("branchName"),
        "filePaths": generated.get("filePaths"),
        "summaryForApproval": generated.get("summaryForApproval", ""),
        "createdAt": datetime.now(timezone.utc).isoformat(),
    }
    save_btp_request(record)

    response_body = {
        "id": request_id,
        "btpConfigYaml": generated.get("btpConfigYaml"),
        "branchName": generated.get("branchName"),
        "filePaths": generated.get("filePaths"),
        "summaryForApproval": generated.get("summaryForApproval", ""),
    }
    if "manifestYaml" in generated:
        response_body["manifestYaml"] = generated["manifestYaml"]

    return func.HttpResponse(json.dumps(response_body), status_code=200, mimetype="application/json")


@app.route(route="btp-publish", methods=["POST"])
def btp_publish(req: func.HttpRequest) -> func.HttpResponse:
    """Phase 2. Called by the Logic App only after its Teams wait-for-response action
    resolves to Approve. Body: {id, btpConfigYaml, manifestYaml, branchName, filePaths}
    — the exact values returned from /api/btp-generate, round-tripped by the Logic App
    (manifestYaml may be absent)."""
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON body", status_code=400)

    request_id = body.get("id", "")
    publish_input = json.dumps({
        "phase": "publish",
        "btpConfigYaml": body.get("btpConfigYaml"),
        "manifestYaml": body.get("manifestYaml"),
        "branchName": body.get("branchName"),
        "filePaths": body.get("filePaths"),
    })

    try:
        reply_text = invoke_workflow("btp-publish-workflow", publish_input)
        result = extract_json(reply_text)
    except Exception as e:
        logging.exception("btp-publish: Phase 2 publish failed")
        if request_id:
            update_btp_request_status(request_id, "demo", "FAILED", {"error": str(e)})
        return func.HttpResponse(json.dumps({"status": "FAILED", "error": str(e)}),
                                  status_code=500, mimetype="application/json")

    if result.get("status") == "PUBLISHED" and request_id:
        update_btp_request_status(request_id, "demo", "PUBLISHED", {
            "githubBranch": result.get("branch"),
            "githubCommitUrls": result.get("commitUrls"),
            "githubPrUrl": result.get("prUrl"),
        })
    elif request_id:
        update_btp_request_status(request_id, "demo", "FAILED", {"error": result.get("error", "unknown")})

    status_code = 200 if result.get("status") == "PUBLISHED" else 500
    return func.HttpResponse(json.dumps(result), status_code=status_code, mimetype="application/json")
