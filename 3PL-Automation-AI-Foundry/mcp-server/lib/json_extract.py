"""
json_extract.py — best-effort JSON extraction from an agent's free-text reply.

Part of: 3pl-automation Solace-publisher feature
Layer:   mcp-server lib

Purpose:  Agents are instructed to return only JSON but routinely wrap it in prose or
          markdown fences. Mirrors ui-gateway's extractAgentJson(): try the whole
          trimmed reply, then a fenced code block, then the first balanced {...} object.
"""
import json
import re


def extract_json(text: str) -> dict:
    trimmed = text.strip()

    try:
        return json.loads(trimmed)
    except json.JSONDecodeError:
        pass

    fence = re.search(r"```(?:json)?\s*([\s\S]*?)```", trimmed, re.IGNORECASE)
    if fence:
        try:
            return json.loads(fence.group(1).strip())
        except json.JSONDecodeError:
            pass

    start = trimmed.find("{")
    if start != -1:
        depth = 0
        in_string = False
        escape_next = False
        for i in range(start, len(trimmed)):
            ch = trimmed[i]
            if escape_next:
                escape_next = False
                continue
            if ch == "\\":
                escape_next = True
                continue
            if ch == '"':
                in_string = not in_string
                continue
            if in_string:
                continue
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    try:
                        return json.loads(trimmed[start:i + 1])
                    except json.JSONDecodeError:
                        break

    raise ValueError(f"Could not extract JSON from reply: {trimmed[:300]}")
