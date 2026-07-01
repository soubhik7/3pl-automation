#!/usr/bin/env python3
"""
BTP Automation MCP Server

Exposes every BTP operation as an AI-callable tool so Claude (or any MCP
client) can plan and execute end-to-end BTP workflows autonomously.

── Quick start ──────────────────────────────────────────────────────────────
    pip install mcp
    python3 btp_mcp_server.py          # stdio transport (Claude Desktop)

── Claude Desktop config  (~/.claude/claude_desktop_config.json) ────────────
    {
      "mcpServers": {
        "btp-automation": {
          "command": "python3",
          "args": ["/absolute/path/to/btp_mcp_server.py"],
          "cwd": "/absolute/path/to/btp-automation"
        }
      }
    }

── Claude Code (.mcp.json in project root) ──────────────────────────────────
    {
      "mcpServers": {
        "btp-automation": {
          "command": "python3",
          "args": ["btp_mcp_server.py"]
        }
      }
    }

── Example agent prompts ────────────────────────────────────────────────────
    "List all service instances and show me which ones have bindings"
    "Create a feature-flags instance called 'ff-prod', bind it, and return credentials"
    "Scaffold a Node.js app called 'my-api', push it to CF, then set APP_ENV=staging"
    "Show me all destinations and create an OAuth2 one pointing to our S/4HANA system"
    "Deploy iFlow 'OrderSync' and check if there are any failed messages"
"""
import json
import os
import sys
from pathlib import Path
from typing import Any

# ── load .env before importing btp modules ────────────────────────────────────
try:
    from dotenv import load_dotenv
    load_dotenv(Path(__file__).parent / ".env")
except ImportError:
    pass

from mcp.server.fastmcp import FastMCP

from btp.config import BTPConfig
from btp.accounts import AccountsService
from btp.entitlements import EntitlementsService
from btp.provisioning import ProvisioningService
from btp.authorization import AuthorizationService
from btp.services import ServicesService
from btp.exceptions import BTPError
import btp.cf as cf_cli

mcp = FastMCP(
    "BTP Automation",
    instructions=(
        "You have full access to an SAP BTP account via these tools. "
        "You can manage subaccounts, entitlements, service instances, service bindings, "
        "Cloud Foundry spaces/apps, Destination Service destinations, and SAP Integration Suite "
        "iFlows. Always list existing resources before creating to avoid duplicates. "
        "For destructive operations (delete) always confirm intent with the user first."
    ),
)

# ── helpers ────────────────────────────────────────────────────────────────────

def _ok(data: Any) -> str:
    return json.dumps(data, indent=2, default=str)

def _err(e: Exception) -> str:
    return json.dumps({"error": str(e)})

def _cfg():
    return BTPConfig()


# ══════════════════════════════════════════════════════════════════════════════
# ACCOUNTS
# ══════════════════════════════════════════════════════════════════════════════

@mcp.tool()
def get_global_account() -> str:
    """Return the global account details (name, GUID, license type, state)."""
    try:
        return _ok(AccountsService().get_global_account())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def list_subaccounts() -> str:
    """List all subaccounts with their GUIDs, regions, subdomains, and states."""
    try:
        return _ok(AccountsService().list_subaccounts())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def get_subaccount(guid: str) -> str:
    """Get full details for a specific subaccount by its GUID."""
    try:
        return _ok(AccountsService().get_subaccount(guid))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def create_subaccount(display_name: str, subdomain: str, region: str = "us10",
                      description: str = "") -> str:
    """
    Create a new subaccount.

    Args:
        display_name: Human-readable name shown in cockpit
        subdomain:    Unique URL-safe subdomain (e.g. 'my-dev-sa')
        region:       BTP region code (default 'us10')
        description:  Optional description
    """
    try:
        return _ok(AccountsService().create_subaccount(
            display_name, subdomain, region, description))
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# ENTITLEMENTS
# ══════════════════════════════════════════════════════════════════════════════

@mcp.tool()
def list_global_entitlements() -> str:
    """List all service entitlements available in the global account."""
    try:
        return _ok(EntitlementsService().list_assignments())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def list_subaccount_entitlements(subaccount_guid: str = "") -> str:
    """
    List entitlements assigned to the configured subaccount (or a specified GUID).

    Returns service name, plan, and quota for each entitlement.
    """
    try:
        cfg = _cfg()
        guid = subaccount_guid or cfg.subaccount_guid
        return _ok(EntitlementsService().list_assignments(subaccount_guid=guid))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def assign_entitlement(service_name: str, plan_name: str,
                       amount: int = 0, subaccount_guid: str = "") -> str:
    """
    Assign (or enable) a service plan entitlement to the subaccount.

    Args:
        service_name:    BTP service technical name (e.g. 'xsuaa', 'destination')
        plan_name:       Plan name (e.g. 'application', 'lite')
        amount:          Quota amount — use 0 to just enable without a quota cap
        subaccount_guid: Override the default subaccount GUID if needed
    """
    try:
        cfg = _cfg()
        guid = subaccount_guid or cfg.subaccount_guid
        amt = amount if amount > 0 else None
        EntitlementsService().assign_entitlement(guid, service_name, plan_name, amt)
        return _ok({"status": "assigned", "service": service_name, "plan": plan_name})
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# PROVISIONING
# ══════════════════════════════════════════════════════════════════════════════

@mcp.tool()
def list_available_environments() -> str:
    """List environment types (cloudfoundry, kyma) available in the subaccount."""
    try:
        cfg = _cfg()
        return _ok(ProvisioningService().list_available_environments(cfg.subaccount_guid))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def list_environment_instances() -> str:
    """List provisioned environment instances (CF orgs, Kyma clusters) in the subaccount."""
    try:
        cfg = _cfg()
        return _ok(ProvisioningService().list_environment_instances(cfg.subaccount_guid))
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# SECURITY
# ══════════════════════════════════════════════════════════════════════════════

@mcp.tool()
def list_role_collections() -> str:
    """List all role collections defined in the subaccount."""
    try:
        return _ok(AuthorizationService().list_role_collections())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def list_users() -> str:
    """List all users in the subaccount."""
    try:
        return _ok(AuthorizationService().list_users())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def assign_user_to_role_collection(user_email: str, collection_name: str,
                                    origin: str = "sap.default") -> str:
    """
    Assign a user to a role collection.

    Args:
        user_email:       User's email address
        collection_name:  Exact role collection name
        origin:           IDP origin key (default 'sap.default')
    """
    try:
        AuthorizationService().assign_user_to_collection(
            collection_name, user_email, origin)
        return _ok({"status": "assigned", "user": user_email, "collection": collection_name})
    except BTPError as e:
        return _err(e)


@mcp.tool()
def create_role_collection(name: str, description: str = "") -> str:
    """Create a new role collection in the subaccount."""
    try:
        return _ok(AuthorizationService().create_role_collection(name, description))
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# SERVICES — Instances & Bindings
# ══════════════════════════════════════════════════════════════════════════════

@mcp.tool()
def list_service_offerings() -> str:
    """List all available service offerings (catalog) in the subaccount."""
    try:
        return _ok(ServicesService().list_offerings())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def list_service_plans(offering_name: str = "") -> str:
    """
    List service plans. Pass offering_name to filter (e.g. 'xsuaa', 'destination').
    Returns plan name, offering, and whether it's free.
    """
    try:
        return _ok(ServicesService().list_plans(offering_name or None))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def list_service_instances() -> str:
    """List all service instances in the subaccount with their plan and status."""
    try:
        return _ok(ServicesService().list_instances())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def create_service_instance(name: str, offering_name: str, plan_name: str,
                             parameters: str = "") -> str:
    """
    Create a new service instance via BTP Service Manager.

    Args:
        name:          Instance name (e.g. 'my-xsuaa')
        offering_name: Service offering (e.g. 'xsuaa', 'destination', 'feature-flags')
        plan_name:     Plan name (e.g. 'application', 'lite')
        parameters:    Optional JSON string of instance parameters
    """
    try:
        params = json.loads(parameters) if parameters.strip() else None
        result = ServicesService().create_instance(name, offering_name, plan_name, params)
        return _ok(result or {"status": "created", "name": name})
    except (BTPError, json.JSONDecodeError) as e:
        return _err(e)


@mcp.tool()
def delete_service_instance(name: str) -> str:
    """
    Delete a service instance. All bindings must be removed first.

    Args:
        name: Instance name to delete
    """
    try:
        ServicesService().delete_instance(name)
        return _ok({"status": "deleted", "name": name})
    except BTPError as e:
        return _err(e)


@mcp.tool()
def list_service_bindings() -> str:
    """List all service bindings in the subaccount."""
    try:
        return _ok(ServicesService().list_bindings())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def create_service_binding(binding_name: str, instance_name: str,
                            parameters: str = "") -> str:
    """
    Create a service binding and generate credentials for the instance.

    Args:
        binding_name:  Name for this binding (e.g. 'my-app-binding')
        instance_name: Existing service instance name to bind
        parameters:    Optional JSON string of binding parameters
    """
    try:
        params = json.loads(parameters) if parameters.strip() else None
        ServicesService().create_binding(binding_name, instance_name, params)
        return _ok({"status": "created", "binding": binding_name, "instance": instance_name})
    except (BTPError, json.JSONDecodeError) as e:
        return _err(e)


@mcp.tool()
def get_service_binding_credentials(binding_name: str) -> str:
    """
    Get the full credentials for a service binding.
    Returns clientid, clientsecret, url, and other service-specific fields.

    Args:
        binding_name: Name of the binding to retrieve credentials for
    """
    try:
        return _ok(ServicesService().get_binding(binding_name))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def delete_service_binding(binding_name: str) -> str:
    """
    Delete a service binding (revokes its credentials).

    Args:
        binding_name: Binding to delete
    """
    try:
        ServicesService().delete_binding(binding_name)
        return _ok({"status": "deleted", "binding": binding_name})
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# CLOUD FOUNDRY
# ══════════════════════════════════════════════════════════════════════════════

@mcp.tool()
def cf_target() -> str:
    """Show the current CF API endpoint, org, space, and logged-in user."""
    try:
        return cf_cli.target()
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_list_spaces() -> str:
    """List all CF spaces in the current org."""
    try:
        spaces = cf_cli.list_spaces()
        return _ok([{"name": s} for s in spaces])
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_list_apps() -> str:
    """List all CF applications in the current space with their status and URLs."""
    try:
        return cf_cli.list_apps()
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_scaffold_and_push_app(app_name: str, runtime: str, memory: str = "256M",
                              instances: int = 1, description: str = "") -> str:
    """
    Scaffold a new CF application from a template and push it to Cloud Foundry.

    Creates all necessary files (app code, manifest.yml, dependencies) then
    runs cf push automatically.

    Args:
        app_name:    Name for the application (also used as CF route prefix)
        runtime:     One of: 'python', 'nodejs', 'static'
        memory:      Memory limit (default '256M')
        instances:   Number of instances (default 1)
        description: Optional app description
    """
    import textwrap
    from pathlib import Path

    app_dir = Path(__file__).parent / "apps" / app_name
    app_dir.mkdir(parents=True, exist_ok=True)

    def write(filename: str, content: str):
        (app_dir / filename).write_text(textwrap.dedent(content).lstrip())

    rt = runtime.lower()

    if rt in ("python", "flask", "python-flask"):
        write("app.py", f"""\
            from flask import Flask, jsonify
            import os

            app = Flask(__name__)

            @app.route('/')
            def index():
                return jsonify({{"app": "{app_name}", "status": "running",
                                  "env": os.environ.get("APP_ENV", "production")}})

            @app.route('/health')
            def health():
                return jsonify({{"status": "ok"}}), 200

            if __name__ == '__main__':
                port = int(os.environ.get('PORT', 8080))
                app.run(host='0.0.0.0', port=port)
        """)
        write("requirements.txt", "flask>=3.0.0\ngunicorn>=21.0.0\n")
        write("Procfile", "web: gunicorn app:app --bind 0.0.0.0:$PORT --workers 2\n")
        buildpack, start_cmd = "python_buildpack", \
            "gunicorn app:app --bind 0.0.0.0:$PORT --workers 2"

    elif rt in ("node", "nodejs", "node.js", "express"):
        write("app.js", f"""\
            'use strict';
            const express = require('express');
            const app = express();
            const port = process.env.PORT || 8080;
            app.get('/', (req, res) => res.json({{
                app: '{app_name}', status: 'running',
                env: process.env.APP_ENV || 'production'
            }}));
            app.get('/health', (req, res) => res.json({{status: 'ok'}}));
            app.listen(port, () => console.log(`{app_name} on ${{port}}`));
        """)
        pkg = {"name": app_name, "version": "1.0.0", "main": "app.js",
               "scripts": {"start": "node app.js"},
               "dependencies": {"express": "^4.18.0"},
               "engines": {"node": ">=18.0.0"}}
        write("package.json", json.dumps(pkg, indent=2) + "\n")
        buildpack, start_cmd = "nodejs_buildpack", "npm start"

    elif rt in ("static", "html", "html5"):
        write("index.html", f"""\
            <!DOCTYPE html><html lang="en">
            <head><meta charset="UTF-8"><title>{app_name}</title>
            <style>body{{font-family:Arial,sans-serif;display:flex;align-items:center;
            justify-content:center;min-height:100vh;margin:0;background:#f5f5f5}}
            .card{{background:white;border-radius:8px;padding:2rem 3rem;
            text-align:center;box-shadow:0 2px 12px rgba(0,0,0,.1)}}
            h1{{color:#0070f2}}</style></head>
            <body><div class="card"><h1>{app_name}</h1>
            <p>{description or "Running on SAP BTP Cloud Foundry"}</p>
            <p><small>Deployed via btp-automation agent</small></p>
            </div></body></html>
        """)
        write("Staticfile", "")
        buildpack, start_cmd = "staticfile_buildpack", None

    else:
        return _err(ValueError(
            f"Unknown runtime '{runtime}'. Use: python, nodejs, or static"))

    manifest_lines = [
        "applications:",
        f"- name: {app_name}",
        f"  memory: {memory}",
        f"  instances: {instances}",
        "  buildpacks:",
        f"  - {buildpack}",
    ]
    if start_cmd:
        manifest_lines.append(f"  command: {start_cmd}")
    manifest_lines += [
        "  env:",
        f"    APP_NAME: {app_name}",
        "    APP_ENV: production",
        "",
    ]
    (app_dir / "manifest.yml").write_text("\n".join(manifest_lines))

    files = [f.name for f in sorted(app_dir.iterdir())]
    try:
        push_output = cf_cli.push_app(app_name, str(app_dir), no_start=False)
        return _ok({
            "status": "deployed",
            "app": app_name,
            "runtime": runtime,
            "files_generated": files,
            "directory": str(app_dir),
            "cf_output": push_output,
        })
    except BTPError as e:
        return _ok({
            "status": "scaffolded_but_push_failed",
            "app": app_name,
            "files_generated": files,
            "directory": str(app_dir),
            "error": str(e),
        })


@mcp.tool()
def cf_push_app(app_name: str, directory_path: str) -> str:
    """
    Push an existing local app directory to CF using its manifest.yml.

    Args:
        app_name:       CF application name
        directory_path: Local path to the directory containing manifest.yml
    """
    try:
        return cf_cli.push_app(app_name, directory_path, no_start=False)
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_delete_app(app_name: str) -> str:
    """Delete a CF application and its routes."""
    try:
        cf_cli.delete_app(app_name)
        return _ok({"status": "deleted", "app": app_name})
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_app_logs(app_name: str) -> str:
    """Get recent log output from a CF application."""
    try:
        return cf_cli.recent_logs(app_name)
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_set_env(app_name: str, key: str, value: str) -> str:
    """
    Set an environment variable on a CF application.
    The app must be restarted or restaged for the change to take effect.
    """
    try:
        cf_cli.set_env(app_name, key, value)
        return _ok({"status": "set", "app": app_name, "key": key})
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_list_services() -> str:
    """List CF marketplace service instances in the current space."""
    try:
        return cf_cli.list_services()
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_create_service_key(service_instance: str, key_name: str) -> str:
    """
    Create a CF service key for a service instance (generates credentials).

    Args:
        service_instance: CF service instance name
        key_name:         Name for the service key
    """
    try:
        cf_cli.create_service_key(service_instance, key_name)
        creds = cf_cli.get_service_key(service_instance, key_name)
        return creds
    except BTPError as e:
        return _err(e)


@mcp.tool()
def cf_bind_service(app_name: str, service_instance: str) -> str:
    """Bind a CF service instance to an application."""
    try:
        cf_cli.bind_service(app_name, service_instance)
        return _ok({"status": "bound", "app": app_name, "service": service_instance})
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# DESTINATIONS
# ══════════════════════════════════════════════════════════════════════════════

def _dest_svc():
    from btp.destinations import DestinationService
    cfg = _cfg()
    return DestinationService(cfg.subaccount_guid)


@mcp.tool()
def list_destinations() -> str:
    """List all subaccount-level destinations (HTTP, RFC, LDAP)."""
    try:
        return _ok(_dest_svc().list())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def get_destination(name: str) -> str:
    """Get full configuration of a specific destination by name."""
    try:
        return _ok(_dest_svc().get(name))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def create_http_destination(name: str, url: str,
                             authentication: str = "NoAuthentication",
                             proxy_type: str = "Internet",
                             username: str = "", password: str = "") -> str:
    """
    Create an HTTP destination in the Destination Service.

    Args:
        name:           Destination name (used in apps to look it up)
        url:            Target URL (e.g. 'https://api.example.com')
        authentication: Auth type — NoAuthentication, BasicAuthentication,
                        OAuth2ClientCredentials, PrincipalPropagation
        proxy_type:     'Internet' or 'OnPremise' (for SAP Cloud Connector)
        username:       Required for BasicAuthentication
        password:       Required for BasicAuthentication
    """
    try:
        from btp.destinations import DestinationService
        cfg = DestinationService.http_destination(
            name, url, authentication, proxy_type,
            username or None, password or None)
        _dest_svc().create(cfg)
        return _ok({"status": "created", "name": name, "url": url,
                    "authentication": authentication})
    except BTPError as e:
        return _err(e)


@mcp.tool()
def create_oauth_destination(name: str, url: str, client_id: str,
                              client_secret: str, token_url: str) -> str:
    """
    Create an OAuth2 client-credentials destination.

    Args:
        name:          Destination name
        url:           Target API URL
        client_id:     OAuth2 client ID
        client_secret: OAuth2 client secret
        token_url:     OAuth2 token endpoint URL
    """
    try:
        from btp.destinations import DestinationService
        cfg = DestinationService.oauth_destination(
            name, url, client_id, client_secret, token_url)
        _dest_svc().create(cfg)
        return _ok({"status": "created", "name": name, "type": "OAuth2ClientCredentials"})
    except BTPError as e:
        return _err(e)


@mcp.tool()
def delete_destination(name: str) -> str:
    """Delete a destination by name."""
    try:
        _dest_svc().delete(name)
        return _ok({"status": "deleted", "name": name})
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# SAP INTEGRATION SUITE
# ══════════════════════════════════════════════════════════════════════════════

def _is_svc():
    from btp.integration_suite import IntegrationSuiteService
    return IntegrationSuiteService()


@mcp.tool()
def is_list_packages() -> str:
    """List all integration packages in SAP Integration Suite."""
    try:
        return _ok(_is_svc().list_packages())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def is_list_iflows(package_id: str) -> str:
    """
    List all iFlows (integration flows) in an integration package.

    Args:
        package_id: Package ID from is_list_packages()
    """
    try:
        return _ok(_is_svc().list_iflows(package_id))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def is_deploy_iflow(iflow_id: str, version: str = "active") -> str:
    """
    Deploy (or redeploy) an iFlow to the Integration Suite runtime.

    Args:
        iflow_id: iFlow artifact ID
        version:  Version to deploy (default 'active')
    """
    try:
        result = _is_svc().deploy_iflow(iflow_id, version)
        return _ok(result or {"status": "deploy_triggered", "iflow": iflow_id})
    except BTPError as e:
        return _err(e)


@mcp.tool()
def is_list_runtime_artifacts() -> str:
    """List all deployed iFlows currently running in the Integration Suite runtime."""
    try:
        return _ok(_is_svc().list_runtime_artifacts())
    except BTPError as e:
        return _err(e)


@mcp.tool()
def is_get_failed_messages(top: int = 20) -> str:
    """
    Get the most recent failed message processing logs.

    Args:
        top: Number of failed messages to return (default 20)
    """
    try:
        return _ok(_is_svc().get_failed_messages(top))
    except BTPError as e:
        return _err(e)


@mcp.tool()
def is_get_message_logs(top: int = 20, status: str = "") -> str:
    """
    Get message processing logs with optional status filter.

    Args:
        top:    Number of logs to return (default 20)
        status: Filter by status — COMPLETED, FAILED, PROCESSING, RETRY, ABANDONED
    """
    try:
        return _ok(_is_svc().get_message_logs(top, status or None))
    except BTPError as e:
        return _err(e)


# ══════════════════════════════════════════════════════════════════════════════
# COMPOSITE TOOLS — multi-step workflows in a single call
# ══════════════════════════════════════════════════════════════════════════════

@mcp.tool()
def account_snapshot() -> str:
    """
    Return a full snapshot of the BTP account state:
    global account, subaccounts, role collections, and all service instances.

    Use this as a starting point to understand what's already provisioned
    before planning any automation workflow.
    """
    cfg = _cfg()
    snapshot = {}

    for label, fn, kwargs in [
        ("global_account",    AccountsService().get_global_account,   {}),
        ("subaccounts",       AccountsService().list_subaccounts,      {}),
        ("role_collections",  AuthorizationService().list_role_collections, {}),
        ("service_instances", ServicesService().list_instances,        {}),
        ("service_bindings",  ServicesService().list_bindings,         {}),
    ]:
        try:
            snapshot[label] = fn(**kwargs)
        except BTPError as e:
            snapshot[label] = {"error": str(e)}

    try:
        snapshot["cf_apps"]    = cf_cli.list_apps()
        snapshot["cf_services"] = cf_cli.list_services()
    except BTPError as e:
        snapshot["cf"] = {"error": str(e)}

    return _ok(snapshot)


@mcp.tool()
def create_instance_and_bind(instance_name: str, offering_name: str,
                              plan_name: str, binding_name: str,
                              parameters: str = "") -> str:
    """
    Create a service instance AND immediately create a binding for it in one step.
    Returns the binding credentials on success.

    Args:
        instance_name: Name for the new service instance
        offering_name: Service offering (e.g. 'xsuaa', 'feature-flags')
        plan_name:     Plan name (e.g. 'application', 'lite')
        binding_name:  Name for the binding (credentials key)
        parameters:    Optional JSON string of instance parameters
    """
    try:
        params = json.loads(parameters) if parameters.strip() else None
        svc = ServicesService()
        svc.create_instance(instance_name, offering_name, plan_name, params)
        svc.create_binding(binding_name, instance_name)
        binding = svc.get_binding(binding_name)
        return _ok({
            "status": "ready",
            "instance": instance_name,
            "binding":  binding_name,
            "credentials": binding.get("credentials", {}),
        })
    except (BTPError, json.JSONDecodeError) as e:
        return _err(e)


# ── entrypoint ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    mcp.run()
