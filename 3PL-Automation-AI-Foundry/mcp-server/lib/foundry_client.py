"""
foundry_client.py — invoke a Foundry Workflow by name via a fresh, one-shot conversation.

Part of: 3pl-automation Solace-publisher feature (fully isolated from the main
         Integration Pulse agents/MCP servers, agent-host, and ui-gateway)
Layer:   mcp-server lib

Purpose:  Calls one of this feature's two single-step Foundry Workflows
          (solace-generate-workflow / solace-publish-workflow) by creating a brand new
          Foundry Conversation and sending one message via agent_reference — exactly the
          invocation shape from the original Foundry workflow sample, and the same
          "fresh conversation per call" pattern proven reliable in this session's
          investigation (see docs/troubleshooting.md §20 in the main repo: the workflow
          engine only reliably executes the first ~2 real InvokeAzureAgent steps per
          conversation — each of this feature's workflows has exactly ONE step, so a
          fresh conversation here is always comfortably inside that boundary).
Depends:  FOUNDRY_PROJECT_ENDPOINT env var; azure-ai-projects, azure-identity, openai.
Importance: The only way this feature's HTTP routes reach the solace-publisher agent.

IMPORTANT — stream=False is NOT safe for a tool-calling response here: a live test
(2026-06-26) confirmed `responses.create(..., stream=False)` can return before the
workflow has actually finished its tool calls — the underlying github_create_branch/
github_commit_file calls completed for real (verified via the GitHub commit history),
but the synchronous response object read back had no final "message" item yet, so text
extraction came back empty even though the operation succeeded. Streaming and waiting
for the response.completed event (the same pattern orchestration_workflow.py's _invoke
uses elsewhere in this repo) reliably blocks until every tool call and the final message
are actually present.
"""
import os
import time

from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential
from openai import APIError

_ENDPOINT = os.environ.get("FOUNDRY_PROJECT_ENDPOINT", "")

_client = None
_oai = None


def _get_oai():
    global _client, _oai
    if _oai is None:
        _client = AIProjectClient(endpoint=_ENDPOINT, credential=DefaultAzureCredential())
        _oai = _client.get_openai_client()
    return _oai


def invoke_workflow(workflow_name: str, message: str, max_retries: int = 3) -> str:
    """Create a fresh conversation, stream one message to the named workflow, and
    return the concatenated text of every "done" text segment — only "done" events
    carry a segment's full text; accumulating delta events too would double it."""
    oai = _get_oai()
    text_chunks: list[str] = []

    for attempt in range(1, max_retries + 1):
        try:
            conversation = oai.conversations.create()
            stream = oai.responses.create(
                conversation=conversation.id,
                extra_body={"agent_reference": {"name": workflow_name, "type": "agent_reference"}},
                input=message,
                stream=True,
            )
            for event in stream:
                if getattr(event, "type", "") == "response.output_text.done":
                    text_chunks.append(event.text)
            result = "".join(text_chunks).strip()
            if result:
                return result
            # Empty result with no exception — same empty-extraction symptom seen live;
            # worth one retry before giving up.
            raise ValueError("workflow stream completed with no text output")
        except (APIError, ValueError) as e:
            if attempt == max_retries:
                raise
            text_chunks.clear()
            time.sleep(5 * attempt)

    return ""
