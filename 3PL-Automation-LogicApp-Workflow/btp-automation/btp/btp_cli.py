"""
BTP CLI subprocess wrapper.
Calls the `btp` command-line tool and parses JSON output.
Requires one-time interactive login:
    btp login --url https://cpcli.cf.eu10.hana.ondemand.com --sso
"""
import json
import subprocess
from typing import Any, Dict, List, Optional

from .exceptions import BTPError, BTPNotFoundError, BTPAuthError


_BTP_BIN = "btp"


def _run(args: List[str], input_text: str = None) -> Any:
    """Run a btp CLI command and return parsed JSON output."""
    cmd = [_BTP_BIN, "--format", "json"] + args
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            input=input_text,
        )
    except FileNotFoundError:
        raise BTPError(
            "BTP CLI not found. Install with: brew install btp\n"
            "Then login: btp login --url https://cpcli.cf.eu10.hana.ondemand.com --sso"
        )

    if result.returncode != 0:
        err = result.stderr.strip() or result.stdout.strip()
        if "not logged in" in err.lower() or "authentication" in err.lower():
            raise BTPAuthError(
                "Not logged in to BTP CLI.\n"
                "Run: btp login --url https://cpcli.cf.eu10.hana.ondemand.com --sso"
            )
        if "not found" in err.lower() or "does not exist" in err.lower():
            raise BTPNotFoundError(err)
        raise BTPError(f"btp CLI error: {err}")

    output = result.stdout.strip()
    if not output:
        return None
    try:
        return json.loads(output)
    except json.JSONDecodeError:
        return {"message": output}


def check_login() -> bool:
    """Return True if the BTP CLI has a valid session."""
    try:
        _run(["get", "accounts/global-account"])
        return True
    except (BTPAuthError, BTPError):
        return False


# ── Global Account ─────────────────────────────────────────────────────────────

def get_global_account() -> Dict:
    return _run(["get", "accounts/global-account"])


# ── Subaccounts ────────────────────────────────────────────────────────────────

def list_subaccounts() -> List[Dict]:
    result = _run(["list", "accounts/subaccount"])
    if isinstance(result, list):
        return result
    return result.get("value", [])


def get_subaccount(guid: str) -> Dict:
    return _run(["get", "accounts/subaccount", guid])


def create_subaccount(
    display_name: str,
    subdomain: str,
    region: str,
    description: str = "",
    beta_enabled: bool = False,
    parent_guid: str = None,
) -> Dict:
    args = [
        "create", "accounts/subaccount",
        "--display-name", display_name,
        "--subdomain", subdomain,
        "--region", region,
        "--used-for-production", "NOT_USED_FOR_PRODUCTION",
    ]
    if description:
        args += ["--description", description]
    if beta_enabled:
        args += ["--beta-enabled", "true"]
    if parent_guid:
        args += ["--parent-directory", parent_guid]
    return _run(args)


def update_subaccount(guid: str, display_name: str = None, description: str = None) -> Dict:
    args = ["update", "accounts/subaccount", guid]
    if display_name:
        args += ["--display-name", display_name]
    if description:
        args += ["--description", description]
    return _run(args)


def delete_subaccount(guid: str) -> None:
    _run(["delete", "accounts/subaccount", guid, "--confirm"])


# ── Directories ────────────────────────────────────────────────────────────────
# BTP CLI has no `list accounts/directory` command.
# Directories are retrieved from the global account hierarchy.

def list_directories() -> List[Dict]:
    result = _run(["get", "accounts/global-account", "--show-hierarchy"])
    # hierarchy includes 'children' which may contain directories
    children = result.get("children", [])
    return [c for c in children if c.get("entityType") == "DIRECTORY" or c.get("parentType") == "DIRECTORY"]


def get_directory(guid: str) -> Dict:
    return _run(["get", "accounts/directory", guid])


def create_directory(display_name: str, description: str = "", parent_guid: str = None) -> Dict:
    args = ["create", "accounts/directory", "--display-name", display_name]
    if description:
        args += ["--description", description]
    if parent_guid:
        args += ["--parent-directory", parent_guid]
    return _run(args)


def update_directory(guid: str, display_name: str = None, description: str = None) -> Dict:
    args = ["update", "accounts/directory", guid]
    if display_name:
        args += ["--display-name", display_name]
    if description:
        args += ["--description", description]
    return _run(args)


def delete_directory(guid: str) -> None:
    _run(["delete", "accounts/directory", guid])


# ── Entitlements ────────────────────────────────────────────────────────────────

def list_entitlements(subaccount_guid: str = None) -> List[Dict]:
    args = ["list", "accounts/entitlement"]
    if subaccount_guid:
        args += ["--subaccount", subaccount_guid]
        result = _run(args)
        # Subaccount-scoped response uses 'quotas' key
        return result if isinstance(result, list) else result.get("quotas", [])
    result = _run(args)
    # Global response uses 'entitledServices' key
    return result if isinstance(result, list) else result.get("entitledServices", [])


def assign_entitlement(
    subaccount_guid: str,
    service_name: str,
    plan_name: str,
    amount: int = None,
) -> None:
    args = [
        "assign", "accounts/entitlement",
        "--to-subaccount", subaccount_guid,
        "--for-service", service_name,
        "--plan", plan_name,
    ]
    if amount is not None:
        args += ["--amount", str(amount)]
    else:
        args += ["--enable"]
    _run(args)


def unassign_entitlement(subaccount_guid: str, service_name: str, plan_name: str) -> None:
    _run([
        "unassign", "accounts/entitlement",
        "--from-subaccount", subaccount_guid,
        "--for-service", service_name,
        "--plan", plan_name,
    ])


# ── Environment Instances ─────────────────────────────────────────────────────

def list_environment_instances(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "accounts/environment-instance", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("environmentInstances", result.get("value", []))


def get_environment_instance(subaccount_guid: str, instance_id: str) -> Dict:
    return _run(["get", "accounts/environment-instance", instance_id,
                 "--subaccount", subaccount_guid])


def create_environment_instance(
    subaccount_guid: str,
    name: str,
    environment_type: str = "cloudfoundry",
    plan: str = "standard",
    landscape_label: str = None,
) -> Dict:
    args = [
        "create", "accounts/environment-instance",
        "--subaccount", subaccount_guid,
        "--display-name", name,
        "--environment", environment_type,
        "--service", environment_type,
        "--plan", plan,
    ]
    if landscape_label:
        args += ["--landscape-label", landscape_label]
    return _run(args)


def delete_environment_instance(subaccount_guid: str, instance_id: str) -> None:
    _run([
        "delete", "accounts/environment-instance", instance_id,
        "--subaccount", subaccount_guid,
    ])


# ── Available Services / Data Centers ─────────────────────────────────────────

def list_available_environments(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "accounts/available-environment", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("availableEnvironments", [])


def list_available_regions() -> List[Dict]:
    result = _run(["list", "accounts/available-region"])
    return result if isinstance(result, list) else result.get("datacenters", [])


# ── Security: Role Collections ─────────────────────────────────────────────────

def list_role_collections(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "security/role-collection", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("value", [])


def get_role_collection(name: str, subaccount_guid: str) -> Dict:
    return _run(["get", "security/role-collection", name, "--subaccount", subaccount_guid])


def create_role_collection(name: str, description: str = "", subaccount_guid: str = None) -> Dict:
    args = ["create", "security/role-collection", name]
    if description:
        args += ["--description", description]
    if subaccount_guid:
        args += ["--subaccount", subaccount_guid]
    return _run(args)


def update_role_collection(name: str, description: str, subaccount_guid: str) -> Dict:
    return _run([
        "update", "security/role-collection", name,
        "--description", description,
        "--subaccount", subaccount_guid,
    ])


def delete_role_collection(name: str, subaccount_guid: str) -> None:
    _run(["delete", "security/role-collection", name, "--subaccount", subaccount_guid])


def add_role_to_collection(
    role_name: str,
    role_template_name: str,
    app_id: str,
    collection_name: str,
    subaccount_guid: str,
) -> None:
    _run([
        "add", "security/role", role_name,
        "--to-role-collection", collection_name,
        "--of-app", app_id,
        "--of-role-template", role_template_name,
        "--subaccount", subaccount_guid,
    ])


def remove_role_from_collection(
    role_name: str,
    role_template_name: str,
    app_id: str,
    collection_name: str,
    subaccount_guid: str,
) -> None:
    _run([
        "remove", "security/role", role_name,
        "--from-role-collection", collection_name,
        "--of-app", app_id,
        "--of-role-template", role_template_name,
        "--subaccount", subaccount_guid,
    ])


def assign_user_to_collection(
    collection_name: str,
    user_email: str,
    subaccount_guid: str,
    origin: str = "sap.default",
) -> None:
    _run([
        "assign", "security/role-collection", collection_name,
        "--to-user", user_email,
        "--of-idp", origin,
        "--subaccount", subaccount_guid,
    ])


def unassign_user_from_collection(
    collection_name: str,
    user_email: str,
    subaccount_guid: str,
    origin: str = "sap.default",
) -> None:
    _run([
        "unassign", "security/role-collection", collection_name,
        "--from-user", user_email,
        "--of-idp", origin,
        "--subaccount", subaccount_guid,
    ])


# ── Security: Roles ───────────────────────────────────────────────────────────

def list_roles(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "security/role", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("value", [])


def create_role(
    name: str,
    role_template_name: str,
    app_id: str,
    description: str = "",
    subaccount_guid: str = None,
) -> Dict:
    args = [
        "create", "security/role", name,
        "--of-app", app_id,
        "--of-role-template", role_template_name,
    ]
    if description:
        args += ["--description", description]
    if subaccount_guid:
        args += ["--subaccount", subaccount_guid]
    return _run(args)


def delete_role(
    name: str,
    role_template_name: str,
    app_id: str,
    subaccount_guid: str,
) -> None:
    _run([
        "delete", "security/role", name,
        "--of-app", app_id,
        "--of-role-template", role_template_name,
        "--subaccount", subaccount_guid,
    ])


# ── Security: Apps and Users ──────────────────────────────────────────────────

def list_apps(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "security/app", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("value", [])


def list_users(subaccount_guid: str) -> List[str]:
    result = _run(["list", "security/user", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("value", [])


# ── Services (CF service marketplace) ────────────────────────────────────────

def list_service_offerings(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "services/offering", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("value", [])


def list_service_instances(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "services/instance", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("value", [])


def list_service_plans(subaccount_guid: str, offering_name: str = None) -> List[Dict]:
    result = _run(["list", "services/plan", "--subaccount", subaccount_guid])
    items = result if isinstance(result, list) else result.get("items", result.get("value", []))
    if offering_name:
        items = [p for p in items if p.get("service_offering_name", "").lower() == offering_name.lower()]
    return items


def create_service_instance(
    subaccount_guid: str,
    name: str,
    offering_name: str,
    plan_name: str,
    parameters: str = None,
) -> Dict:
    args = [
        "create", "services/instance",
        "--subaccount", subaccount_guid,
        "--name", name,
        "--offering-name", offering_name,
        "--plan-name", plan_name,
    ]
    if parameters:
        args += ["--parameters", parameters]
    return _run(args)


def delete_service_instance(subaccount_guid: str, name: str) -> None:
    _run([
        "delete", "services/instance",
        "--subaccount", subaccount_guid,
        "--name", name,
        "--confirm",
    ])


# ── Services: Binding lifecycle ───────────────────────────────────────────────

def list_service_bindings(subaccount_guid: str) -> List[Dict]:
    result = _run(["list", "services/binding", "--subaccount", subaccount_guid])
    return result if isinstance(result, list) else result.get("items", result.get("value", []))


def create_service_binding(
    subaccount_guid: str,
    binding_name: str,
    instance_name: str,
    parameters: str = None,
) -> Dict:
    args = [
        "create", "services/binding",
        "--subaccount", subaccount_guid,
        "--binding", binding_name,
        "--instance-name", instance_name,
    ]
    if parameters:
        args += ["--parameters", parameters]
    return _run(args)


def get_service_binding(subaccount_guid: str, binding_name: str) -> Dict:
    return _run([
        "get", "services/binding",
        "--subaccount", subaccount_guid,
        "--name", binding_name,
    ])


def delete_service_binding(subaccount_guid: str, binding_name: str) -> None:
    _run([
        "delete", "services/binding",
        "--subaccount", subaccount_guid,
        "--name", binding_name,
        "--confirm",
    ])
