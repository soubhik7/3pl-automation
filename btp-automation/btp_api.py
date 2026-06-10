#!/usr/bin/env python3
"""
BTP Automation REST API

FastAPI backend that exposes all BTP operations as JSON endpoints.
Serves both the web dashboard (static files) and the REST API.

── Run ───────────────────────────────────────────────────────────────────────
    uvicorn btp_api:app --reload --port 8000
    # Then open: http://localhost:8000

── API docs ──────────────────────────────────────────────────────────────────
    http://localhost:8000/docs    (Swagger UI)
    http://localhost:8000/redoc   (ReDoc)
"""
import json
from pathlib import Path
from typing import Any, Optional

from dotenv import load_dotenv
load_dotenv(Path(__file__).parent / ".env")

from fastapi import FastAPI, HTTPException, BackgroundTasks
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

from btp.config import BTPConfig
from btp.accounts import AccountsService
from btp.entitlements import EntitlementsService
from btp.provisioning import ProvisioningService
from btp.authorization import AuthorizationService
from btp.services import ServicesService
from btp.exceptions import BTPError
import btp.cf as cf_cli

app = FastAPI(
    title="BTP Automation API",
    description="Full SAP BTP management REST API — accounts, services, CF, destinations, Integration Suite",
    version="1.0.0",
    docs_url="/docs",
    redoc_url="/redoc",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ── Serve web dashboard ───────────────────────────────────────────────────────
_WEB_DIR = Path(__file__).parent / "btp_web"

@app.get("/", response_class=HTMLResponse, include_in_schema=False)
def root():
    index = _WEB_DIR / "index.html"
    if index.exists():
        return index.read_text()
    return "<h1>BTP Automation API</h1><p>See <a href='/docs'>/docs</a></p>"


# ── helpers ───────────────────────────────────────────────────────────────────

def _cfg():
    return BTPConfig()

def _raise(e: BTPError):
    raise HTTPException(status_code=400, detail=str(e))


# ══════════════════════════════════════════════════════════════════════════════
# ACCOUNTS
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/api/accounts/global", tags=["Accounts"])
def get_global_account():
    """Global account details."""
    try:
        return AccountsService().get_global_account()
    except BTPError as e:
        _raise(e)


@app.get("/api/accounts/subaccounts", tags=["Accounts"])
def list_subaccounts():
    """List all subaccounts."""
    try:
        return AccountsService().list_subaccounts()
    except BTPError as e:
        _raise(e)


@app.get("/api/accounts/subaccounts/{guid}", tags=["Accounts"])
def get_subaccount(guid: str):
    """Get a subaccount by GUID."""
    try:
        return AccountsService().get_subaccount(guid)
    except BTPError as e:
        _raise(e)


class CreateSubaccountRequest(BaseModel):
    display_name: str
    subdomain: str
    region: str = "us10"
    description: str = ""


@app.post("/api/accounts/subaccounts", tags=["Accounts"], status_code=201)
def create_subaccount(req: CreateSubaccountRequest):
    """Create a new subaccount."""
    try:
        return AccountsService().create_subaccount(
            req.display_name, req.subdomain, req.region, req.description)
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# ENTITLEMENTS
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/api/entitlements", tags=["Entitlements"])
def list_entitlements(subaccount_guid: Optional[str] = None):
    """List entitlements. Pass ?subaccount_guid=... to scope to a subaccount."""
    try:
        return EntitlementsService().list_assignments(subaccount_guid=subaccount_guid)
    except BTPError as e:
        _raise(e)


class AssignEntitlementRequest(BaseModel):
    service_name: str
    plan_name: str
    amount: Optional[int] = None
    subaccount_guid: Optional[str] = None


@app.post("/api/entitlements", tags=["Entitlements"])
def assign_entitlement(req: AssignEntitlementRequest):
    """Assign a service plan to the subaccount."""
    try:
        cfg = _cfg()
        guid = req.subaccount_guid or cfg.subaccount_guid
        EntitlementsService().assign_entitlement(
            guid, req.service_name, req.plan_name, req.amount)
        return {"status": "assigned", "service": req.service_name, "plan": req.plan_name}
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# PROVISIONING
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/api/environments/available", tags=["Provisioning"])
def list_available_environments():
    """Available environment types (CF, Kyma)."""
    try:
        cfg = _cfg()
        return ProvisioningService().list_available_environments(cfg.subaccount_guid)
    except BTPError as e:
        _raise(e)


@app.get("/api/environments/instances", tags=["Provisioning"])
def list_environment_instances():
    """Provisioned CF orgs and Kyma clusters."""
    try:
        cfg = _cfg()
        return ProvisioningService().list_environment_instances(cfg.subaccount_guid)
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# SECURITY
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/api/security/role-collections", tags=["Security"])
def list_role_collections():
    """List all role collections."""
    try:
        return AuthorizationService().list_role_collections()
    except BTPError as e:
        _raise(e)


@app.get("/api/security/users", tags=["Security"])
def list_users():
    """List all users in the subaccount."""
    try:
        return AuthorizationService().list_users()
    except BTPError as e:
        _raise(e)


class AssignUserRequest(BaseModel):
    user_email: str
    collection_name: str
    origin: str = "sap.default"


@app.post("/api/security/role-collections/assign", tags=["Security"])
def assign_user(req: AssignUserRequest):
    """Assign a user to a role collection."""
    try:
        AuthorizationService().assign_user_to_collection(
            req.collection_name, req.user_email, req.origin)
        return {"status": "assigned", "user": req.user_email,
                "collection": req.collection_name}
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# SERVICES
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/api/services/offerings", tags=["Services"])
def list_service_offerings():
    """List all available service offerings."""
    try:
        return ServicesService().list_offerings()
    except BTPError as e:
        _raise(e)


@app.get("/api/services/plans", tags=["Services"])
def list_service_plans(offering: Optional[str] = None):
    """List service plans. Filter with ?offering=xsuaa"""
    try:
        return ServicesService().list_plans(offering)
    except BTPError as e:
        _raise(e)


@app.get("/api/services/instances", tags=["Services"])
def list_service_instances():
    """List all service instances."""
    try:
        return ServicesService().list_instances()
    except BTPError as e:
        _raise(e)


class CreateInstanceRequest(BaseModel):
    name: str
    offering_name: str
    plan_name: str
    parameters: Optional[dict] = None


@app.post("/api/services/instances", tags=["Services"], status_code=201)
def create_service_instance(req: CreateInstanceRequest):
    """Create a new service instance."""
    try:
        result = ServicesService().create_instance(
            req.name, req.offering_name, req.plan_name, req.parameters)
        return result or {"status": "created", "name": req.name}
    except BTPError as e:
        _raise(e)


@app.delete("/api/services/instances/{name}", tags=["Services"])
def delete_service_instance(name: str):
    """Delete a service instance (bindings must be removed first)."""
    try:
        ServicesService().delete_instance(name)
        return {"status": "deleted", "name": name}
    except BTPError as e:
        _raise(e)


@app.get("/api/services/bindings", tags=["Services"])
def list_service_bindings():
    """List all service bindings."""
    try:
        return ServicesService().list_bindings()
    except BTPError as e:
        _raise(e)


class CreateBindingRequest(BaseModel):
    binding_name: str
    instance_name: str
    parameters: Optional[dict] = None


@app.post("/api/services/bindings", tags=["Services"], status_code=201)
def create_service_binding(req: CreateBindingRequest):
    """Create a service binding and generate credentials."""
    try:
        ServicesService().create_binding(
            req.binding_name, req.instance_name, req.parameters)
        return {"status": "created", "binding": req.binding_name}
    except BTPError as e:
        _raise(e)


@app.get("/api/services/bindings/{name}/credentials", tags=["Services"])
def get_binding_credentials(name: str):
    """Get credentials for a specific service binding."""
    try:
        return ServicesService().get_binding(name)
    except BTPError as e:
        _raise(e)


@app.delete("/api/services/bindings/{name}", tags=["Services"])
def delete_service_binding(name: str):
    """Delete a service binding (revokes credentials)."""
    try:
        ServicesService().delete_binding(name)
        return {"status": "deleted", "binding": name}
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# CLOUD FOUNDRY
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/api/cf/target", tags=["Cloud Foundry"])
def cf_target():
    """Current CF target (org, space, user)."""
    try:
        return {"output": cf_cli.target()}
    except BTPError as e:
        _raise(e)


@app.get("/api/cf/spaces", tags=["Cloud Foundry"])
def cf_spaces():
    """List CF spaces."""
    try:
        return [{"name": s} for s in cf_cli.list_spaces()]
    except BTPError as e:
        _raise(e)


@app.get("/api/cf/apps", tags=["Cloud Foundry"])
def cf_apps():
    """List CF applications."""
    try:
        return {"output": cf_cli.list_apps()}
    except BTPError as e:
        _raise(e)


class ScaffoldAppRequest(BaseModel):
    app_name: str
    runtime: str           # python | nodejs | static
    memory: str = "256M"
    instances: int = 1
    description: str = ""
    push: bool = True


@app.post("/api/cf/apps/scaffold", tags=["Cloud Foundry"], status_code=201)
def scaffold_and_push_app(req: ScaffoldAppRequest):
    """Scaffold a new app from template and optionally push it to CF."""
    import textwrap

    app_dir = Path(__file__).parent / "apps" / req.app_name
    app_dir.mkdir(parents=True, exist_ok=True)

    def write(filename: str, content: str):
        (app_dir / filename).write_text(textwrap.dedent(content).lstrip())

    rt = req.runtime.lower()
    if rt in ("python", "flask"):
        write("app.py", f"""\
            from flask import Flask, jsonify
            import os
            app = Flask(__name__)
            @app.route('/')
            def index():
                return jsonify({{"app": "{req.app_name}", "status": "running"}})
            @app.route('/health')
            def health():
                return jsonify({{"status": "ok"}}), 200
            if __name__ == '__main__':
                app.run(host='0.0.0.0', port=int(os.environ.get('PORT', 8080)))
        """)
        write("requirements.txt", "flask>=3.0.0\ngunicorn>=21.0.0\n")
        write("Procfile", "web: gunicorn app:app --bind 0.0.0.0:$PORT --workers 2\n")
        buildpack, cmd = "python_buildpack", \
            "gunicorn app:app --bind 0.0.0.0:$PORT --workers 2"
    elif rt in ("node", "nodejs"):
        write("app.js", f"""\
            const express = require('express');
            const app = express();
            app.get('/', (req, res) => res.json({{"app": "{req.app_name}", "status": "running"}}));
            app.get('/health', (req, res) => res.json({{"status": "ok"}}));
            app.listen(process.env.PORT || 8080);
        """)
        pkg = {
            "name": req.app_name, "version": "1.0.0",
            "scripts": {"start": "node app.js"},
            "dependencies": {"express": "^4.18.0"},
        }
        write("package.json", json.dumps(pkg, indent=2))
        buildpack, cmd = "nodejs_buildpack", "npm start"
    elif rt in ("static", "html5"):
        write("index.html", f"<html><body><h1>{req.app_name}</h1>"
              f"<p>{req.description or 'Running on SAP BTP'}</p></body></html>")
        write("Staticfile", "")
        buildpack, cmd = "staticfile_buildpack", None
    else:
        raise HTTPException(400, f"Unknown runtime '{req.runtime}'")

    manifest = ["applications:", f"- name: {req.app_name}",
                f"  memory: {req.memory}", f"  instances: {req.instances}",
                "  buildpacks:", f"  - {buildpack}"]
    if cmd:
        manifest.append(f"  command: {cmd}")
    manifest += ["  env:", f"    APP_NAME: {req.app_name}", "    APP_ENV: production", ""]
    (app_dir / "manifest.yml").write_text("\n".join(manifest))

    files = [f.name for f in sorted(app_dir.iterdir())]
    result = {"app": req.app_name, "runtime": req.runtime,
              "files": files, "directory": str(app_dir)}

    if req.push:
        try:
            result["cf_output"] = cf_cli.push_app(req.app_name, str(app_dir))
            result["status"] = "deployed"
        except BTPError as e:
            result["status"] = "scaffolded_push_failed"
            result["error"] = str(e)
    else:
        result["status"] = "scaffolded"

    return result


@app.delete("/api/cf/apps/{name}", tags=["Cloud Foundry"])
def delete_cf_app(name: str):
    """Delete a CF application."""
    try:
        cf_cli.delete_app(name)
        return {"status": "deleted", "app": name}
    except BTPError as e:
        _raise(e)


@app.get("/api/cf/services", tags=["Cloud Foundry"])
def cf_services():
    """List CF marketplace service instances."""
    try:
        return {"output": cf_cli.list_services()}
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# DESTINATIONS
# ══════════════════════════════════════════════════════════════════════════════

def _dest_svc():
    from btp.destinations import DestinationService
    cfg = _cfg()
    return DestinationService(cfg.subaccount_guid)


@app.get("/api/destinations", tags=["Destinations"])
def list_destinations():
    """List all subaccount destinations."""
    try:
        return _dest_svc().list()
    except BTPError as e:
        _raise(e)


@app.get("/api/destinations/{name}", tags=["Destinations"])
def get_destination(name: str):
    """Get a specific destination by name."""
    try:
        return _dest_svc().get(name)
    except BTPError as e:
        _raise(e)


class CreateHttpDestRequest(BaseModel):
    name: str
    url: str
    authentication: str = "NoAuthentication"
    proxy_type: str = "Internet"
    username: Optional[str] = None
    password: Optional[str] = None


@app.post("/api/destinations/http", tags=["Destinations"], status_code=201)
def create_http_destination(req: CreateHttpDestRequest):
    """Create an HTTP destination."""
    try:
        from btp.destinations import DestinationService
        cfg = DestinationService.http_destination(
            req.name, req.url, req.authentication,
            req.proxy_type, req.username, req.password)
        _dest_svc().create(cfg)
        return {"status": "created", "name": req.name}
    except BTPError as e:
        _raise(e)


class CreateOAuthDestRequest(BaseModel):
    name: str
    url: str
    client_id: str
    client_secret: str
    token_url: str


@app.post("/api/destinations/oauth", tags=["Destinations"], status_code=201)
def create_oauth_destination(req: CreateOAuthDestRequest):
    """Create an OAuth2 client-credentials destination."""
    try:
        from btp.destinations import DestinationService
        cfg = DestinationService.oauth_destination(
            req.name, req.url, req.client_id, req.client_secret, req.token_url)
        _dest_svc().create(cfg)
        return {"status": "created", "name": req.name}
    except BTPError as e:
        _raise(e)


@app.delete("/api/destinations/{name}", tags=["Destinations"])
def delete_destination(name: str):
    """Delete a destination."""
    try:
        _dest_svc().delete(name)
        return {"status": "deleted", "name": name}
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# INTEGRATION SUITE
# ══════════════════════════════════════════════════════════════════════════════

def _is_svc():
    from btp.integration_suite import IntegrationSuiteService
    return IntegrationSuiteService()


@app.get("/api/isuit/packages", tags=["Integration Suite"])
def is_packages():
    """List integration packages."""
    try:
        return _is_svc().list_packages()
    except BTPError as e:
        _raise(e)


@app.get("/api/isuit/packages/{package_id}/iflows", tags=["Integration Suite"])
def is_iflows(package_id: str):
    """List iFlows in a package."""
    try:
        return _is_svc().list_iflows(package_id)
    except BTPError as e:
        _raise(e)


@app.post("/api/isuit/iflows/{iflow_id}/deploy", tags=["Integration Suite"])
def is_deploy(iflow_id: str, version: str = "active"):
    """Deploy an iFlow to the runtime."""
    try:
        return _is_svc().deploy_iflow(iflow_id, version) or \
               {"status": "deploy_triggered", "iflow": iflow_id}
    except BTPError as e:
        _raise(e)


@app.get("/api/isuit/runtime", tags=["Integration Suite"])
def is_runtime():
    """List deployed runtime artifacts."""
    try:
        return _is_svc().list_runtime_artifacts()
    except BTPError as e:
        _raise(e)


@app.get("/api/isuit/logs", tags=["Integration Suite"])
def is_logs(top: int = 20, status: Optional[str] = None):
    """Message processing logs. Filter with ?status=FAILED"""
    try:
        return _is_svc().get_message_logs(top, status)
    except BTPError as e:
        _raise(e)


# ══════════════════════════════════════════════════════════════════════════════
# COMPOSITE — dashboard snapshot
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/api/snapshot", tags=["Dashboard"])
def snapshot():
    """
    Full account snapshot: global account, subaccounts, service instances,
    bindings, CF apps, and CF services. Used by the web dashboard.
    """
    cfg = _cfg()
    data: dict = {}

    for label, fn in [
        ("global_account",    lambda: AccountsService().get_global_account()),
        ("subaccounts",       lambda: AccountsService().list_subaccounts()),
        ("role_collections",  lambda: AuthorizationService().list_role_collections()),
        ("service_instances", lambda: ServicesService().list_instances()),
        ("service_bindings",  lambda: ServicesService().list_bindings()),
    ]:
        try:
            data[label] = fn()
        except BTPError as e:
            data[label] = {"error": str(e)}

    try:
        data["cf_target"]   = cf_cli.target()
        data["cf_services"] = cf_cli.list_services()
    except BTPError as e:
        data["cf"] = {"error": str(e)}

    return data
