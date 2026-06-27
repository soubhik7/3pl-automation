"""
yaml_extract.py — sanity-check a YAML string before it's committed to GitHub.

Part of: 3pl-automation MuleSoft/BTP-publisher features
Layer:   mcp-server lib

Purpose:  The MuleSoft/BTP agents' Phase 1 output is still a JSON envelope (parsed by
          lib/json_extract.py as today) — only the *values inside* that envelope
          (mulesoftYaml["app.yaml"], btpConfigYaml, manifestYaml, ...) are raw YAML text.
          validate_yaml() is a lightweight well-formedness check on those individual
          string values, used by function_app.py's mulesoft/btp generate routes before
          saving the audit record — a malformed value should surface as a warning in the
          summary, not silently fail the whole request.
Depends:  PyYAML.
"""
import yaml


def validate_yaml(yaml_text: str) -> bool:
    """Returns True if yaml_text parses as valid YAML, False otherwise. Never raises."""
    try:
        yaml.safe_load(yaml_text)
        return True
    except yaml.YAMLError:
        return False
