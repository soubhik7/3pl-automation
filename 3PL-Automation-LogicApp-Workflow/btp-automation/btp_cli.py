#!/usr/bin/env python3
"""
BTP Automation CLI — full SAP BTP automation including integrations.

No-arg launch:   python btp_cli.py          → interactive TUI
Scripted use:    python btp_cli.py <group> <command> [options]

Command groups:
    accounts      Global account, subaccounts, directories
    entitlements  Service plan entitlement management
    provisioning  CF / Kyma environment instances
    auth          Roles, role collections, users
    services      Service instances and bindings (full lifecycle)
    cf            Cloud Foundry operations (spaces, apps, bindings)
    destinations  Destination Service CRUD (HTTP, OAuth, RFC)
    isuit         SAP Integration Suite (packages, iFlows, logs)
    interactive   Launch the interactive TUI explicitly
"""
import json
import os
import sys
import textwrap
import click
from pathlib import Path

from btp.config import BTPConfig
from btp.client import BTPClient
from btp.accounts import AccountsService
from btp.entitlements import EntitlementsService
from btp.provisioning import ProvisioningService
from btp.authorization import AuthorizationService
from btp.services import ServicesService
from btp.exceptions import BTPError, BTPAuthError
import btp.output as out
import btp.cf as cf_cli


def _client() -> BTPClient:
    return BTPClient(BTPConfig())


# ── Root group ────────────────────────────────────────────────────────────────

@click.group()
@click.option("--format", "fmt", default="table",
              type=click.Choice(["table", "json", "yaml"]), show_default=True)
@click.pass_context
def cli(ctx, fmt):
    """SAP BTP Full Automation CLI"""
    ctx.ensure_object(dict)
    ctx.obj["fmt"] = fmt


def _print(data, fmt="table", title="", columns=None):
    if fmt == "json":
        out.print_json(data)
    elif fmt == "yaml":
        out.print_yaml(data)
    else:
        if isinstance(data, list):
            out.print_table(data, columns=columns, title=title)
        else:
            out.print_json(data)


# ══════════════════════════════════════════════════════════════════════════════
# ACCOUNTS
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def accounts():
    """Global account, subaccounts, and directories."""


@accounts.command("global-account")
@click.pass_context
def global_account(ctx):
    """Show global account details."""
    try:
        svc = AccountsService(_client())
        data = svc.get_global_account()
        _print(data, ctx.obj["fmt"], "Global Account")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("list-subaccounts")
@click.pass_context
def list_subaccounts(ctx):
    """List all subaccounts."""
    try:
        svc = AccountsService(_client())
        data = svc.list_subaccounts()
        _print(data, ctx.obj["fmt"], "Subaccounts",
               columns=["guid", "displayName", "subdomain", "region", "state"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("get-subaccount")
@click.argument("guid")
@click.pass_context
def get_subaccount(ctx, guid):
    """Get a subaccount by GUID."""
    try:
        svc = AccountsService(_client())
        _print(svc.get_subaccount(guid), ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("create-subaccount")
@click.option("--name", required=True, help="Display name")
@click.option("--subdomain", required=True, help="Unique subdomain")
@click.option("--region", required=True, help="Region, e.g. us10")
@click.option("--description", default="", help="Description")
@click.option("--parent-guid", default=None, help="Parent directory GUID")
@click.pass_context
def create_subaccount(ctx, name, subdomain, region, description, parent_guid):
    """Create a new subaccount."""
    try:
        svc = AccountsService(_client())
        data = svc.create_subaccount(name, subdomain, region, description, parent_guid)
        out.success(f"Subaccount '{name}' created: {data.get('guid', '')}")
        _print(data, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("update-subaccount")
@click.argument("guid")
@click.option("--name", default=None)
@click.option("--description", default=None)
@click.pass_context
def update_subaccount(ctx, guid, name, description):
    """Update a subaccount."""
    try:
        svc = AccountsService(_client())
        data = svc.update_subaccount(guid, display_name=name, description=description)
        out.success(f"Subaccount {guid} updated")
        _print(data, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("delete-subaccount")
@click.argument("guid")
@click.confirmation_option(prompt="This permanently deletes the subaccount. Continue?")
@click.pass_context
def delete_subaccount(ctx, guid):
    """Delete a subaccount."""
    try:
        svc = AccountsService(_client())
        svc.delete_subaccount(guid)
        out.success(f"Subaccount {guid} deleted")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("list-directories")
@click.pass_context
def list_directories(ctx):
    """List all directories."""
    try:
        svc = AccountsService(_client())
        data = svc.list_directories()
        _print(data, ctx.obj["fmt"], "Directories",
               columns=["guid", "displayName", "state"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("create-directory")
@click.option("--name", required=True)
@click.option("--description", default="")
@click.option("--parent-guid", default=None)
@click.pass_context
def create_directory(ctx, name, description, parent_guid):
    """Create a directory."""
    try:
        svc = AccountsService(_client())
        data = svc.create_directory(name, description, parent_guid)
        out.success(f"Directory '{name}' created: {data.get('guid', '')}")
        _print(data, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@accounts.command("delete-directory")
@click.argument("guid")
@click.confirmation_option(prompt="Delete this directory?")
@click.pass_context
def delete_directory(ctx, guid):
    """Delete a directory."""
    try:
        svc = AccountsService(_client())
        svc.delete_directory(guid)
        out.success(f"Directory {guid} deleted")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# ENTITLEMENTS
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def entitlements():
    """Service plan entitlement management."""


@entitlements.command("list")
@click.option("--subaccount", default=None, help="Filter by subaccount GUID")
@click.option("--service", default=None, help="Filter by service name")
@click.pass_context
def list_entitlements(ctx, subaccount, service):
    """List entitlement assignments."""
    try:
        svc = EntitlementsService(_client())
        data = svc.list_assignments(subaccount_guid=subaccount, service_name=service)
        # Subaccount-filtered response uses {service, plan, quota} fields
        # Global response uses {name, displayName, servicePlans}
        if subaccount and data and "service" in (data[0] if data else {}):
            cols = ["service", "plan", "quota", "unlimited"]
        else:
            cols = ["name", "displayName"]
        _print(data, ctx.obj["fmt"], "Entitlements", columns=cols)
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@entitlements.command("assign")
@click.option("--subaccount", required=True, help="Subaccount GUID")
@click.option("--service", required=True, help="Service technical name")
@click.option("--plan", required=True, help="Service plan name")
@click.option("--amount", default=None, type=int, help="Quota amount (omit for unlimited)")
@click.pass_context
def assign_entitlement(ctx, subaccount, service, plan, amount):
    """Assign a service plan entitlement to a subaccount."""
    try:
        svc = EntitlementsService(_client())
        svc.assign_entitlement(subaccount, service, plan, amount)
        out.success(f"Entitlement {service}/{plan} assigned to {subaccount}")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@entitlements.command("unassign")
@click.option("--subaccount", required=True)
@click.option("--service", required=True)
@click.option("--plan", required=True)
@click.confirmation_option(prompt="Remove this entitlement?")
@click.pass_context
def unassign_entitlement(ctx, subaccount, service, plan):
    """Remove a service plan entitlement from a subaccount."""
    try:
        svc = EntitlementsService(_client())
        svc.unassign_entitlement(subaccount, service, plan)
        out.success(f"Entitlement {service}/{plan} removed from {subaccount}")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@entitlements.command("data-centers")
@click.pass_context
def data_centers(ctx):
    """List available data centers."""
    try:
        svc = EntitlementsService(_client())
        data = svc.get_allowed_data_centers()
        _print(data, ctx.obj["fmt"], "Available Data Centers",
               columns=["name", "displayName", "region", "environment"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# PROVISIONING (Environments)
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def provisioning():
    """Runtime environment instance management (CF, Kyma)."""


@provisioning.command("list-available")
@click.option("--subaccount", required=True, help="Subaccount GUID")
@click.pass_context
def list_available(ctx, subaccount):
    """List environment types available for provisioning."""
    try:
        svc = ProvisioningService(_client())
        data = svc.list_available_environments(subaccount)
        _print(data, ctx.obj["fmt"], "Available Environments",
               columns=["environmentType", "planName", "displayName"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@provisioning.command("list")
@click.option("--subaccount", required=True, help="Subaccount GUID")
@click.pass_context
def list_envs(ctx, subaccount):
    """List provisioned environment instances."""
    try:
        svc = ProvisioningService(_client())
        data = svc.list_environment_instances(subaccount)
        _print(data, ctx.obj["fmt"], "Environment Instances",
               columns=["environmentInstanceID", "environmentType", "name", "state"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@provisioning.command("create-cf")
@click.option("--subaccount", required=True)
@click.option("--org-name", required=True, help="CF org name")
@click.option("--landscape", default=None, help="Landscape label e.g. cf-us10-001")
@click.pass_context
def create_cf(ctx, subaccount, org_name, landscape):
    """Provision a Cloud Foundry environment."""
    try:
        svc = ProvisioningService(_client())
        data = svc.create_cf_environment(subaccount, org_name, landscape_label=landscape)
        out.success(f"CF environment '{org_name}' provisioning started")
        _print(data, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@provisioning.command("delete")
@click.option("--subaccount", required=True)
@click.option("--instance-id", required=True, help="Environment instance ID")
@click.confirmation_option(prompt="Delete this environment instance?")
@click.pass_context
def delete_env(ctx, subaccount, instance_id):
    """Delete an environment instance."""
    try:
        svc = ProvisioningService(_client())
        svc.delete_environment_instance(subaccount, instance_id)
        out.success(f"Environment {instance_id} deletion started")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# AUTHORIZATION (XSUAA)
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def auth():
    """Security: roles, role collections, users."""


@auth.command("list-apps")
@click.pass_context
def list_apps(ctx):
    """List registered XSUAA applications in the subaccount."""
    try:
        svc = AuthorizationService()
        data = svc.list_applications()
        _print(data, ctx.obj["fmt"], "Applications",
               columns=["appid", "xsappname", "description"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("list-roles")
@click.pass_context
def list_roles(ctx):
    """List all roles in the subaccount."""
    try:
        svc = AuthorizationService()
        data = svc.list_roles()
        _print(data, ctx.obj["fmt"], "Roles",
               columns=["name", "roleTemplateName", "roleTemplateAppId", "description"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("create-role")
@click.option("--name", required=True)
@click.option("--template", required=True, help="Role template name")
@click.option("--app-id", required=True, help="Application ID (e.g. myapp!t123)")
@click.option("--description", default="")
@click.pass_context
def create_role(ctx, name, template, app_id, description):
    """Create a role from a template."""
    try:
        svc = AuthorizationService()
        data = svc.create_role(name, template, app_id, description)
        out.success(f"Role '{name}' created")
        _print(data, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("delete-role")
@click.option("--name", required=True, help="Role name")
@click.option("--template", required=True, help="Role template name")
@click.option("--app-id", required=True, help="Application ID")
@click.confirmation_option(prompt="Delete this role?")
@click.pass_context
def delete_role(ctx, name, template, app_id):
    """Delete a role."""
    try:
        svc = AuthorizationService()
        svc.delete_role(name, template, app_id)
        out.success(f"Role '{name}' deleted")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("list-role-collections")
@click.pass_context
def list_role_collections(ctx):
    """List all role collections."""
    try:
        svc = AuthorizationService()
        data = svc.list_role_collections()
        _print(data, ctx.obj["fmt"], "Role Collections",
               columns=["name", "description", "isReadOnly"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("get-role-collection")
@click.argument("name")
@click.pass_context
def get_role_collection(ctx, name):
    """Get a role collection by name."""
    try:
        svc = AuthorizationService()
        _print(svc.get_role_collection(name), ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("create-role-collection")
@click.option("--name", required=True)
@click.option("--description", default="")
@click.pass_context
def create_role_collection(ctx, name, description):
    """Create a new role collection."""
    try:
        svc = AuthorizationService()
        data = svc.create_role_collection(name, description)
        out.success(f"Role collection '{name}' created")
        _print(data, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("delete-role-collection")
@click.argument("name")
@click.confirmation_option(prompt="Delete this role collection?")
@click.pass_context
def delete_role_collection(ctx, name):
    """Delete a role collection."""
    try:
        svc = AuthorizationService()
        svc.delete_role_collection(name)
        out.success(f"Role collection '{name}' deleted")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("add-role-to-collection")
@click.option("--collection", required=True, help="Role collection name")
@click.option("--role-name", required=True, help="Role name")
@click.option("--template", required=True, help="Role template name")
@click.option("--app-id", required=True, help="Application ID")
@click.pass_context
def add_role_to_collection(ctx, collection, role_name, template, app_id):
    """Add a role to a role collection."""
    try:
        svc = AuthorizationService()
        svc.add_role_to_collection(collection, role_name, template, app_id)
        out.success(f"Role '{role_name}' added to '{collection}'")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("list-users")
@click.pass_context
def list_users(ctx):
    """List all users in the subaccount."""
    try:
        svc = AuthorizationService()
        data = svc.list_users()
        if isinstance(data, list) and data and isinstance(data[0], str):
            _print([{"email": u} for u in data], ctx.obj["fmt"], "Users", columns=["email"])
        else:
            _print(data, ctx.obj["fmt"], "Users", columns=["id", "userName"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("assign-user")
@click.option("--collection", required=True, help="Role collection name")
@click.option("--user", required=True, help="User email")
@click.option("--origin", default="sap.default", help="IDP origin (default: sap.default)")
@click.pass_context
def assign_user(ctx, collection, user, origin):
    """Assign a user to a role collection."""
    try:
        svc = AuthorizationService()
        svc.assign_user_to_collection(collection, user, origin)
        out.success(f"User '{user}' assigned to role collection '{collection}'")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@auth.command("unassign-user")
@click.option("--collection", required=True, help="Role collection name")
@click.option("--user", required=True, help="User email")
@click.option("--origin", default="sap.default")
@click.confirmation_option(prompt="Remove user from this role collection?")
@click.pass_context
def unassign_user(ctx, collection, user, origin):
    """Remove a user from a role collection."""
    try:
        svc = AuthorizationService()
        svc.remove_user_from_collection(collection, user, origin)
        out.success(f"User '{user}' removed from '{collection}'")
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# SERVICES
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def services():
    """CF service marketplace: offerings, plans, and instances."""


@services.command("list-offerings")
@click.pass_context
def list_offerings(ctx):
    """List available service offerings."""
    try:
        from btp.services import ServicesService
        svc = ServicesService()
        data = svc.list_offerings()
        _print(data, ctx.obj["fmt"], "Service Offerings",
               columns=["name", "displayName", "description"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@services.command("list-plans")
@click.option("--offering", default=None, help="Filter by service offering name")
@click.pass_context
def list_plans(ctx, offering):
    """List service plans."""
    try:
        from btp.services import ServicesService
        svc = ServicesService()
        data = svc.list_plans(offering)
        _print(data, ctx.obj["fmt"], "Service Plans",
               columns=["name", "displayName", "serviceOfferingName", "free"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


@services.command("list-instances")
@click.pass_context
def list_instances(ctx):
    """List provisioned service instances."""
    try:
        from btp.services import ServicesService
        svc = ServicesService()
        data = svc.list_instances()
        _print(data, ctx.obj["fmt"], "Service Instances",
               columns=["name", "service_plan_id", "usable", "ready"])
    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# INTERACTIVE MODE
# ══════════════════════════════════════════════════════════════════════════════

# ══════════════════════════════════════════════════════════════════════════════
# SERVICES — full lifecycle (offerings, plans, instances, bindings)
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def services():
    """CF service marketplace: offerings, plans, instances, and bindings."""


@services.command("list-offerings")
@click.pass_context
def list_offerings(ctx):
    """List available service offerings."""
    svc = ServicesService()
    _print(svc.list_offerings(), ctx.obj["fmt"], "Service Offerings",
           columns=["name", "displayName", "description"])


@services.command("list-plans")
@click.option("--offering", default=None, help="Filter by service offering name")
@click.pass_context
def list_plans(ctx, offering):
    """List service plans."""
    svc = ServicesService()
    _print(svc.list_plans(offering), ctx.obj["fmt"], "Service Plans",
           columns=["name", "displayName", "serviceOfferingName", "free"])


@services.command("list-instances")
@click.pass_context
def list_instances(ctx):
    """List provisioned service instances."""
    svc = ServicesService()
    _print(svc.list_instances(), ctx.obj["fmt"], "Service Instances",
           columns=["name", "service_plan_id", "usable", "ready"])


@services.command("create-instance")
@click.option("--name", required=True, help="Instance name")
@click.option("--offering", required=True, help="Service offering name (e.g. xsuaa)")
@click.option("--plan", required=True, help="Service plan name (e.g. application)")
@click.option("--params", default=None, help='JSON parameters e.g. \'{"xsappname":"myapp"}\'')
@click.pass_context
def create_instance(ctx, name, offering, plan, params):
    """Create a new service instance."""
    try:
        svc = ServicesService()
        params_dict = json.loads(params) if params else None
        r = svc.create_instance(name, offering, plan, params_dict)
        out.success(f"Service instance '{name}' created")
        _print(r, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@services.command("delete-instance")
@click.argument("name")
@click.confirmation_option(prompt="Delete this service instance?")
@click.pass_context
def delete_instance(ctx, name):
    """Delete a service instance (all bindings must be removed first)."""
    try:
        ServicesService().delete_instance(name)
        out.success(f"Instance '{name}' deleted")
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@services.command("list-bindings")
@click.pass_context
def list_bindings(ctx):
    """List service bindings."""
    svc = ServicesService()
    _print(svc.list_bindings(), ctx.obj["fmt"], "Service Bindings",
           columns=["name", "service_instance_id", "ready"])


@services.command("create-binding")
@click.option("--binding", required=True, help="Binding name")
@click.option("--instance", required=True, help="Service instance name")
@click.option("--params", default=None, help="JSON parameters")
@click.pass_context
def create_binding(ctx, binding, instance, params):
    """Create a service binding (generates credentials)."""
    try:
        svc = ServicesService()
        params_dict = json.loads(params) if params else None
        r = svc.create_binding(binding, instance, params_dict)
        out.success(f"Binding '{binding}' created for instance '{instance}'")
        _print(r, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@services.command("get-binding")
@click.argument("name")
@click.pass_context
def get_binding(ctx, name):
    """Get a service binding including its credentials."""
    try:
        _print(ServicesService().get_binding(name), ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@services.command("delete-binding")
@click.argument("name")
@click.confirmation_option(prompt="Delete this binding (credentials will be revoked)?")
@click.pass_context
def delete_binding(ctx, name):
    """Delete a service binding."""
    try:
        ServicesService().delete_binding(name)
        out.success(f"Binding '{name}' deleted")
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# CLOUD FOUNDRY
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def cf():
    """Cloud Foundry: spaces, apps, CF services, and bindings."""


def _cf(fn, *args, **kwargs):
    try:
        result = fn(*args, **kwargs)
        if result:
            out.info(result)
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@cf.command("target")
def cf_target():
    """Show current CF target (org / space / user)."""
    _cf(cf_cli.target)


@cf.command("set-target")
@click.option("--org", default=None)
@click.option("--space", default=None)
def cf_set_target(org, space):
    """Set CF target org and/or space."""
    _cf(cf_cli.set_target, org, space)


@cf.command("list-spaces")
def cf_list_spaces():
    """List all spaces in the current org."""
    try:
        spaces = cf_cli.list_spaces()
        out.print_table([{"name": s} for s in spaces], columns=["name"], title="Spaces")
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@cf.command("create-space")
@click.argument("name")
def cf_create_space(name):
    """Create a CF space."""
    _cf(cf_cli.create_space, name)


@cf.command("delete-space")
@click.argument("name")
@click.confirmation_option(prompt="Delete this space?")
def cf_delete_space(name):
    """Delete a CF space."""
    _cf(cf_cli.delete_space, name)


@cf.command("list-apps")
def cf_list_apps():
    """List CF apps in current space."""
    _cf(cf_cli.list_apps)


@cf.command("push")
@click.argument("name")
@click.option("--path", default=".", help="Path to app directory")
@click.option("--no-start", is_flag=True, help="Push without starting")
def cf_push(name, path, no_start):
    """Push a CF application."""
    _cf(cf_cli.push_app, name, path, no_start)


@cf.command("delete-app")
@click.argument("name")
@click.confirmation_option(prompt="Delete this app and its routes?")
def cf_delete_app(name):
    """Delete a CF app."""
    _cf(cf_cli.delete_app, name)


@cf.command("start")
@click.argument("name")
def cf_start(name):
    """Start a CF app."""
    _cf(cf_cli.start_app, name)


@cf.command("stop")
@click.argument("name")
def cf_stop(name):
    """Stop a CF app."""
    _cf(cf_cli.stop_app, name)


@cf.command("restage")
@click.argument("name")
def cf_restage(name):
    """Restage a CF app."""
    _cf(cf_cli.restage_app, name)


@cf.command("logs")
@click.argument("name")
def cf_logs(name):
    """Show recent CF app logs."""
    _cf(cf_cli.recent_logs, name)


@cf.command("env")
@click.argument("name")
def cf_env(name):
    """Show environment variables for a CF app."""
    _cf(cf_cli.get_env, name)


@cf.command("set-env")
@click.argument("app")
@click.argument("key")
@click.argument("value")
def cf_set_env(app, key, value):
    """Set an environment variable on a CF app."""
    _cf(cf_cli.set_env, app, key, value)


@cf.command("list-services")
def cf_list_services():
    """List CF service instances in current space."""
    _cf(cf_cli.list_services)


@cf.command("create-service")
@click.option("--service", required=True, help="Service offering name")
@click.option("--plan", required=True, help="Service plan name")
@click.option("--name", required=True, help="Instance name")
@click.option("--params", default=None, help="JSON parameters")
def cf_create_service(service, plan, name, params):
    """Create a CF service instance."""
    _cf(cf_cli.create_service, service, plan, name, params)


@cf.command("delete-service")
@click.argument("name")
@click.confirmation_option(prompt="Delete this CF service instance?")
def cf_delete_service(name):
    """Delete a CF service instance."""
    _cf(cf_cli.delete_service, name)


@cf.command("bind-service")
@click.argument("app")
@click.argument("service")
def cf_bind_service(app, service):
    """Bind a CF service instance to an app."""
    _cf(cf_cli.bind_service, app, service)


@cf.command("unbind-service")
@click.argument("app")
@click.argument("service")
def cf_unbind_service(app, service):
    """Unbind a CF service instance from an app."""
    _cf(cf_cli.unbind_service, app, service)


@cf.command("create-service-key")
@click.argument("service")
@click.argument("key_name")
def cf_create_service_key(service, key_name):
    """Create a service key for a CF service instance."""
    _cf(cf_cli.create_service_key, service, key_name)


@cf.command("get-service-key")
@click.argument("service")
@click.argument("key_name")
def cf_get_service_key(service, key_name):
    """Show a service key (credentials)."""
    _cf(cf_cli.get_service_key, service, key_name)


# ══════════════════════════════════════════════════════════════════════════════
# DESTINATIONS
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def destinations():
    """Destination Service: HTTP, OAuth, RFC destinations."""


def _dest_svc(binding: str = None):
    try:
        from btp.destinations import DestinationService
        cfg = BTPConfig()
        return DestinationService(cfg.subaccount_guid, binding)
    except Exception as e:
        out.error(str(e)); sys.exit(1)


@destinations.command("list")
@click.option("--binding", default=None, help="Destination service binding name")
@click.pass_context
def dest_list(ctx, binding):
    """List all subaccount destinations."""
    try:
        data = _dest_svc(binding).list()
        _print(data, ctx.obj["fmt"], "Destinations",
               columns=["Name", "Type", "URL", "Authentication"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@destinations.command("get")
@click.argument("name")
@click.option("--binding", default=None)
@click.pass_context
def dest_get(ctx, name, binding):
    """Get a destination by name."""
    try:
        _print(_dest_svc(binding).get(name), ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@destinations.command("create-http")
@click.option("--name", required=True)
@click.option("--url", required=True)
@click.option("--auth", default="NoAuthentication",
              type=click.Choice(["NoAuthentication", "BasicAuthentication",
                                 "OAuth2ClientCredentials", "PrincipalPropagation"]))
@click.option("--proxy", default="Internet", type=click.Choice(["Internet", "OnPremise"]))
@click.option("--user", default=None)
@click.option("--password", default=None)
@click.option("--binding", default=None)
@click.pass_context
def dest_create_http(ctx, name, url, auth, proxy, user, password, binding):
    """Create an HTTP destination."""
    try:
        from btp.destinations import DestinationService
        cfg = DestinationService.http_destination(name, url, auth, proxy, user, password)
        _dest_svc(binding).create(cfg)
        out.success(f"Destination '{name}' created")
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@destinations.command("create-oauth")
@click.option("--name", required=True)
@click.option("--url", required=True, help="Target service URL")
@click.option("--client-id", required=True)
@click.option("--client-secret", required=True)
@click.option("--token-url", required=True)
@click.option("--binding", default=None)
@click.pass_context
def dest_create_oauth(ctx, name, url, client_id, client_secret, token_url, binding):
    """Create an OAuth2 client-credentials destination."""
    try:
        from btp.destinations import DestinationService
        cfg = DestinationService.oauth_destination(name, url, client_id, client_secret, token_url)
        _dest_svc(binding).create(cfg)
        out.success(f"OAuth destination '{name}' created")
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@destinations.command("delete")
@click.argument("name")
@click.option("--binding", default=None)
@click.confirmation_option(prompt="Delete this destination?")
@click.pass_context
def dest_delete(ctx, name, binding):
    """Delete a destination."""
    try:
        _dest_svc(binding).delete(name)
        out.success(f"Destination '{name}' deleted")
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# INTEGRATION SUITE
# ══════════════════════════════════════════════════════════════════════════════

@cli.group()
def isuit():
    """SAP Integration Suite: packages, iFlows, runtime, message logs."""


def _is_svc():
    try:
        from btp.integration_suite import IntegrationSuiteService
        return IntegrationSuiteService()
    except Exception as e:
        out.error(str(e)); sys.exit(1)


@isuit.command("list-packages")
@click.pass_context
def is_list_packages(ctx):
    """List integration packages."""
    try:
        _print(_is_svc().list_packages(), ctx.obj["fmt"], "Integration Packages",
               columns=["Id", "Name", "Version", "Description"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@isuit.command("list-iflows")
@click.argument("package_id")
@click.pass_context
def is_list_iflows(ctx, package_id):
    """List iFlows in a package."""
    try:
        _print(_is_svc().list_iflows(package_id), ctx.obj["fmt"], "iFlows",
               columns=["Id", "Name", "Version", "Description"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@isuit.command("deploy")
@click.argument("iflow_id")
@click.option("--version", default="active")
@click.pass_context
def is_deploy(ctx, iflow_id, version):
    """Deploy an iFlow to the runtime."""
    try:
        r = _is_svc().deploy_iflow(iflow_id, version)
        out.success(f"Deploy triggered for '{iflow_id}'")
        _print(r, ctx.obj["fmt"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@isuit.command("undeploy")
@click.argument("iflow_id")
@click.confirmation_option(prompt="Undeploy this iFlow from runtime?")
def is_undeploy(iflow_id):
    """Remove an iFlow from the runtime."""
    try:
        _is_svc().undeploy_iflow(iflow_id)
        out.success(f"iFlow '{iflow_id}' undeployed")
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@isuit.command("list-runtime")
@click.pass_context
def is_list_runtime(ctx):
    """List all deployed runtime artifacts."""
    try:
        _print(_is_svc().list_runtime_artifacts(), ctx.obj["fmt"], "Runtime Artifacts",
               columns=["Id", "Name", "Status", "Version"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@isuit.command("logs")
@click.option("--top", default=20, show_default=True)
@click.option("--status", default=None,
              type=click.Choice(["COMPLETED", "FAILED", "PROCESSING", "RETRY", "ABANDONED"]))
@click.pass_context
def is_logs(ctx, top, status):
    """Show message processing logs."""
    try:
        _print(_is_svc().get_message_logs(top, status), ctx.obj["fmt"], "Message Logs",
               columns=["MessageGuid", "Status", "LogStart", "LogEnd", "Sender", "Receiver"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


@isuit.command("failed-messages")
@click.option("--top", default=20, show_default=True)
@click.pass_context
def is_failed(ctx, top):
    """Show failed message processing logs."""
    try:
        _print(_is_svc().get_failed_messages(top), ctx.obj["fmt"], "Failed Messages",
               columns=["MessageGuid", "Status", "LogStart", "Sender", "Receiver"])
    except BTPError as e:
        out.error(str(e)); sys.exit(1)


# ══════════════════════════════════════════════════════════════════════════════
# INTERACTIVE TUI
# ══════════════════════════════════════════════════════════════════════════════

def _launch_interactive():
    """Full interactive TUI covering every BTP operation."""
    try:
        from rich.console import Console
        from rich.panel import Panel
        from rich.prompt import Prompt, Confirm
        from rich.align import Align
    except ImportError:
        print("ERROR: 'rich' is required.  Run: pip install rich")
        sys.exit(1)

    console = Console()

    # ── service initialisation ────────────────────────────────────────────────
    try:
        cfg          = BTPConfig()
        _sa_default  = cfg.subaccount_guid
        _sa_label    = cfg.subaccount_subdomain
        _ga_label    = cfg.global_account_subdomain
    except Exception as e:
        console.print(f"[red]Cannot load config:[/red] {e}")
        sys.exit(1)

    try:
        accounts_svc  = AccountsService()
        ent_svc       = EntitlementsService()
        prov_svc      = ProvisioningService()
        auth_svc      = AuthorizationService()
        services_svc  = ServicesService()
    except Exception as e:
        console.print(f"[red]Service init failed:[/red] {e}")
        sys.exit(1)

    # lazy singletons for services that may need extra config
    _dest_svc_cache  = {}
    _is_svc_cache    = {}

    def get_dest_svc(binding_name: str = None):
        key = binding_name or "__auto__"
        if key not in _dest_svc_cache:
            from btp.destinations import DestinationService
            _dest_svc_cache[key] = DestinationService(_sa_default, binding_name)
        return _dest_svc_cache[key]

    def get_is_svc():
        if "svc" not in _is_svc_cache:
            from btp.integration_suite import IntegrationSuiteService
            _is_svc_cache["svc"] = IntegrationSuiteService()
        return _is_svc_cache["svc"]

    # ── UI helpers ────────────────────────────────────────────────────────────

    def banner():
        console.print(Panel(
            Align.center(
                f"[bold cyan]SAP BTP Automation[/bold cyan]\n"
                f"[dim]Global: [cyan]{_ga_label}[/cyan]  |  "
                f"Subaccount: [cyan]{_sa_label}[/cyan] "
                f"([dim]{_sa_default[:8]}…[/dim])[/dim]"
            ),
            border_style="cyan",
            padding=(0, 2),
        ))

    def section(title: str):
        console.print(f"\n[bold blue]── {title} ──[/bold blue]")

    def menu(title: str, items: dict) -> str:
        console.print()
        console.print(Panel(f"[bold]{title}[/bold]", border_style="blue", padding=(0, 1)))
        for k, label in items.items():
            style = "dim" if k == "0" else "yellow"
            console.print(f"    [{style}]{k:>3}[/{style}]  {label}")
        console.print()
        return Prompt.ask("    [bold]Select[/bold]", choices=list(items.keys()), default="0")

    def ask(prompt: str) -> str:
        return Prompt.ask(f"    {prompt}")

    def ask_with_default(prompt: str, default: str) -> str:
        return Prompt.ask(f"    {prompt}", default=default)

    def ask_optional(prompt: str) -> str:
        return Prompt.ask(f"    {prompt} [dim](optional, Enter to skip)[/dim]", default="")

    def confirm(msg: str) -> bool:
        return Confirm.ask(f"    [yellow]{msg}[/yellow]")

    # ── ACCOUNTS submenu ──────────────────────────────────────────────────────

    def menu_accounts():
        items = {
            "1": "Show Global Account",
            "2": "List Subaccounts",
            "3": "Get Subaccount",
            "4": "Create Subaccount",
            "5": "Update Subaccount",
            "6": "Delete Subaccount",
            "7": "List Directories",
            "8": "Create Directory",
            "9": "Delete Directory",
            "0": "Back",
        }
        while True:
            choice = menu("Accounts & Hierarchy", items)
            try:
                if choice == "1":
                    section("Global Account")
                    out.print_json(accounts_svc.get_global_account())
                elif choice == "2":
                    section("Subaccounts")
                    out.print_table(
                        accounts_svc.list_subaccounts(),
                        columns=["guid", "displayName", "subdomain", "region", "state"],
                    )
                elif choice == "3":
                    guid = ask("Subaccount GUID")
                    if guid:
                        out.print_json(accounts_svc.get_subaccount(guid))
                elif choice == "4":
                    name   = ask("Display name")
                    sub    = ask("Subdomain (unique)")
                    region = ask_with_default("Region", "us10")
                    desc   = ask_optional("Description")
                    if name and sub:
                        r = accounts_svc.create_subaccount(name, sub, region, desc)
                        out.success(f"Subaccount '{name}' created → {r.get('guid', '')}")
                elif choice == "5":
                    guid = ask("Subaccount GUID")
                    name = ask_optional("New display name")
                    desc = ask_optional("New description")
                    if guid:
                        accounts_svc.update_subaccount(
                            guid,
                            display_name=name or None,
                            description=desc or None,
                        )
                        out.success("Updated")
                elif choice == "6":
                    guid = ask("Subaccount GUID to delete")
                    if guid and confirm(f"Permanently delete subaccount {guid}?"):
                        accounts_svc.delete_subaccount(guid)
                        out.success("Deleted")
                elif choice == "7":
                    section("Directories")
                    out.print_table(
                        accounts_svc.list_directories(),
                        columns=["guid", "displayName", "state"],
                    )
                elif choice == "8":
                    name = ask("Directory name")
                    desc = ask_optional("Description")
                    if name:
                        r = accounts_svc.create_directory(name, desc)
                        out.success(f"Directory '{name}' created → {r.get('guid', '')}")
                elif choice == "9":
                    guid = ask("Directory GUID to delete")
                    if guid and confirm(f"Delete directory {guid}?"):
                        accounts_svc.delete_directory(guid)
                        out.success("Deleted")
                elif choice == "0":
                    break
            except BTPError as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── ENTITLEMENTS submenu ──────────────────────────────────────────────────

    def menu_entitlements():
        items = {
            "1": "List All Entitlements (Global)",
            "2": "List Subaccount Entitlements",
            "3": "Assign Entitlement to Subaccount",
            "4": "Unassign Entitlement",
            "5": "List Available Data Centers",
            "0": "Back",
        }
        while True:
            choice = menu("Entitlements", items)
            try:
                if choice == "1":
                    section("Global Entitlements")
                    out.print_table(ent_svc.list_assignments(), columns=["name", "displayName"])
                elif choice == "2":
                    data = ent_svc.list_assignments(subaccount_guid=_sa_default)
                    cols = (
                        ["service", "plan", "quota"]
                        if data and "service" in data[0]
                        else ["name", "displayName"]
                    )
                    section(f"Entitlements for {_sa_default[:8]}…")
                    out.print_table(data, columns=cols)
                elif choice == "3":
                    guid    = ask_with_default("Subaccount GUID", _sa_default)
                    service = ask("Service name (e.g. xsuaa)")
                    plan    = ask("Plan name (e.g. application)")
                    amt_str = ask_optional("Amount (blank = unlimited/enable)")
                    amount  = int(amt_str) if amt_str else None
                    if service and plan:
                        ent_svc.assign_entitlement(guid, service, plan, amount)
                        out.success(f"{service}/{plan} assigned to {guid[:8]}…")
                elif choice == "4":
                    guid    = ask_with_default("Subaccount GUID", _sa_default)
                    service = ask("Service name")
                    plan    = ask("Plan name")
                    if service and plan and confirm(f"Remove {service}/{plan} from {guid[:8]}…?"):
                        ent_svc.unassign_entitlement(guid, service, plan)
                        out.success("Entitlement removed")
                elif choice == "5":
                    section("Available Data Centers")
                    out.print_table(
                        ent_svc.get_allowed_data_centers(),
                        columns=["name", "displayName", "region", "environment"],
                    )
                elif choice == "0":
                    break
            except BTPError as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── PROVISIONING submenu ──────────────────────────────────────────────────

    def menu_provisioning():
        items = {
            "1": "List Available Environments",
            "2": "List Environment Instances",
            "3": "Create Cloud Foundry Environment",
            "4": "Delete Environment Instance",
            "0": "Back",
        }
        while True:
            choice = menu("Provisioning / Environments", items)
            try:
                if choice == "1":
                    section("Available Environments")
                    out.print_table(
                        prov_svc.list_available_environments(_sa_default),
                        columns=["environmentType", "planName", "displayName"],
                    )
                elif choice == "2":
                    section("Environment Instances")
                    out.print_table(
                        prov_svc.list_environment_instances(_sa_default),
                        columns=["environmentInstanceID", "environmentType", "name", "state"],
                    )
                elif choice == "3":
                    guid      = ask_with_default("Subaccount GUID", _sa_default)
                    org_name  = ask("CF org name")
                    landscape = ask_optional("Landscape label (e.g. cf-us10-001)")
                    if org_name:
                        r = prov_svc.create_cf_environment(
                            guid, org_name, landscape_label=landscape or None
                        )
                        out.success(f"CF environment '{org_name}' provisioning started")
                        out.print_json(r)
                elif choice == "4":
                    guid    = ask_with_default("Subaccount GUID", _sa_default)
                    inst_id = ask("Environment instance ID")
                    if inst_id and confirm(f"Delete environment instance {inst_id}?"):
                        prov_svc.delete_environment_instance(guid, inst_id)
                        out.success("Deletion started")
                elif choice == "0":
                    break
            except BTPError as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── SECURITY submenu ──────────────────────────────────────────────────────

    def menu_security():
        items = {
            "1":  "List Applications",
            "2":  "List Roles",
            "3":  "Create Role",
            "4":  "Delete Role",
            "5":  "List Role Collections",
            "6":  "Get Role Collection",
            "7":  "Create Role Collection",
            "8":  "Delete Role Collection",
            "9":  "Add Role to Collection",
            "10": "Remove Role from Collection",
            "11": "List Users",
            "12": "Assign User to Collection",
            "13": "Remove User from Collection",
            "0":  "Back",
        }
        while True:
            choice = menu("Security: Roles & Users", items)
            try:
                if choice == "1":
                    section("Applications")
                    out.print_table(
                        auth_svc.list_applications(),
                        columns=["appid", "xsappname", "description"],
                    )
                elif choice == "2":
                    section("Roles")
                    out.print_table(
                        auth_svc.list_roles(),
                        columns=["name", "roleTemplateName", "roleTemplateAppId", "description"],
                    )
                elif choice == "3":
                    name     = ask("Role name")
                    template = ask("Role template name")
                    app_id   = ask("Application ID (e.g. myapp!t123)")
                    desc     = ask_optional("Description")
                    if name and template and app_id:
                        auth_svc.create_role(name, template, app_id, desc)
                        out.success(f"Role '{name}' created")
                elif choice == "4":
                    name     = ask("Role name")
                    template = ask("Role template name")
                    app_id   = ask("Application ID")
                    if name and template and app_id and confirm(f"Delete role '{name}'?"):
                        auth_svc.delete_role(name, template, app_id)
                        out.success(f"Role '{name}' deleted")
                elif choice == "5":
                    section("Role Collections")
                    out.print_table(
                        auth_svc.list_role_collections(),
                        columns=["name", "description", "isReadOnly"],
                    )
                elif choice == "6":
                    name = ask("Role collection name")
                    if name:
                        out.print_json(auth_svc.get_role_collection(name))
                elif choice == "7":
                    name = ask("Role collection name")
                    desc = ask_optional("Description")
                    if name:
                        auth_svc.create_role_collection(name, desc)
                        out.success(f"Role collection '{name}' created")
                elif choice == "8":
                    name = ask("Role collection name")
                    if name and confirm(f"Delete role collection '{name}'?"):
                        auth_svc.delete_role_collection(name)
                        out.success(f"Role collection '{name}' deleted")
                elif choice == "9":
                    collection = ask("Role collection name")
                    role_name  = ask("Role name")
                    template   = ask("Role template name")
                    app_id     = ask("Application ID")
                    if collection and role_name and template and app_id:
                        auth_svc.add_role_to_collection(collection, role_name, template, app_id)
                        out.success(f"Role '{role_name}' added to '{collection}'")
                elif choice == "10":
                    collection = ask("Role collection name")
                    role_name  = ask("Role name")
                    template   = ask("Role template name")
                    app_id     = ask("Application ID")
                    if (
                        collection and role_name and template and app_id
                        and confirm(f"Remove '{role_name}' from '{collection}'?")
                    ):
                        auth_svc.remove_role_from_collection(collection, role_name, template, app_id)
                        out.success("Role removed from collection")
                elif choice == "11":
                    section("Users")
                    users = auth_svc.list_users()
                    if isinstance(users, list) and users and isinstance(users[0], str):
                        out.print_table([{"email": u} for u in users], columns=["email"])
                    else:
                        out.print_table(users, columns=["id", "userName"])
                elif choice == "12":
                    collection = ask("Role collection name")
                    user       = ask("User email")
                    origin     = ask_with_default("IDP origin", "sap.default")
                    if collection and user:
                        auth_svc.assign_user_to_collection(collection, user, origin)
                        out.success(f"'{user}' assigned to '{collection}'")
                elif choice == "13":
                    collection = ask("Role collection name")
                    user       = ask("User email")
                    origin     = ask_with_default("IDP origin", "sap.default")
                    if collection and user and confirm(f"Remove '{user}' from '{collection}'?"):
                        auth_svc.remove_user_from_collection(collection, user, origin)
                        out.success("User removed from collection")
                elif choice == "0":
                    break
            except BTPError as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── SERVICES submenu (full lifecycle) ─────────────────────────────────────

    def menu_services():
        items = {
            "1": "List Service Offerings",
            "2": "List Service Plans",
            "3": "List Service Instances",
            "4": "Create Service Instance",
            "5": "Delete Service Instance",
            "6": "── Bindings ──",
            "7": "List Service Bindings",
            "8": "Create Service Binding",
            "9": "Get Service Binding (credentials)",
            "10": "Delete Service Binding",
            "0": "Back",
        }
        while True:
            choice = menu("Services: Instances & Bindings", items)
            try:
                if choice == "1":
                    section("Service Offerings")
                    out.print_table(services_svc.list_offerings(),
                                    columns=["name", "displayName", "description"])
                elif choice == "2":
                    offering = ask_optional("Filter by offering name (blank = all)")
                    out.print_table(services_svc.list_plans(offering or None),
                                    columns=["name", "service_offering_name", "free", "ready"])
                elif choice == "3":
                    section("Service Instances")
                    out.print_table(services_svc.list_instances(),
                                    columns=["name", "service_plan_id", "usable", "ready"])
                elif choice == "4":
                    name     = ask("Instance name")
                    offering = ask("Service offering (e.g. xsuaa, destination)")
                    plan     = ask("Plan name (e.g. application, lite)")
                    raw      = ask_optional('JSON parameters (e.g. {"xsappname":"myapp"})')
                    params   = json.loads(raw) if raw else None
                    if name and offering and plan:
                        r = services_svc.create_instance(name, offering, plan, params)
                        out.success(f"Instance '{name}' created")
                        if r:
                            out.print_json(r)
                elif choice == "5":
                    name = ask("Instance name to delete")
                    if name and confirm(f"Delete instance '{name}'? (bindings must be removed first)"):
                        services_svc.delete_instance(name)
                        out.success(f"Instance '{name}' deleted")
                elif choice == "6":
                    pass  # section header
                elif choice == "7":
                    section("Service Bindings")
                    out.print_table(services_svc.list_bindings(),
                                    columns=["name", "service_instance_id", "ready"])
                elif choice == "8":
                    binding  = ask("Binding name")
                    instance = ask("Service instance name")
                    raw      = ask_optional("JSON parameters (optional)")
                    params   = json.loads(raw) if raw else None
                    if binding and instance:
                        services_svc.create_binding(binding, instance, params)
                        out.success(f"Binding '{binding}' created")
                elif choice == "9":
                    binding = ask("Binding name")
                    if binding:
                        out.print_json(services_svc.get_binding(binding))
                elif choice == "10":
                    binding = ask("Binding name to delete")
                    if binding and confirm(f"Delete binding '{binding}' (credentials revoked)?"):
                        services_svc.delete_binding(binding)
                        out.success(f"Binding '{binding}' deleted")
                elif choice == "0":
                    break
            except (BTPError, json.JSONDecodeError) as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── CLOUD FOUNDRY submenu ─────────────────────────────────────────────────

    def _scaffold_app() -> str | None:
        """Interactively scaffold a new CF app and return its directory path."""
        runtime_menu = {
            "1": "Python (Flask + gunicorn)",
            "2": "Node.js (Express)",
            "3": "Static HTML5 (Staticfile buildpack)",
            "0": "Cancel",
        }
        rt = menu("Choose Runtime", runtime_menu)
        if rt == "0":
            return None

        name    = ask("App name (used as CF app name)")
        memory  = ask_with_default("Memory limit", "256M")
        inst    = ask_with_default("Instances", "1")
        desc    = ask_optional("Short description")

        if not name:
            out.error("App name is required")
            return None

        app_dir = Path(".") / "apps" / name
        app_dir.mkdir(parents=True, exist_ok=True)

        # ── manifest.yml (common structure) ──────────────────────────────────
        def write(filename: str, content: str):
            (app_dir / filename).write_text(textwrap.dedent(content).lstrip())

        # ── Python Flask ──────────────────────────────────────────────────────
        if rt == "1":
            write("app.py", f"""\
                from flask import Flask, jsonify
                import os

                app = Flask(__name__)

                @app.route('/')
                def index():
                    return jsonify({{
                        "app":    "{name}",
                        "status": "running",
                        "env":    os.environ.get("APP_ENV", "production"),
                    }})

                @app.route('/health')
                def health():
                    return jsonify({{"status": "ok"}}), 200

                if __name__ == '__main__':
                    port = int(os.environ.get('PORT', 8080))
                    app.run(host='0.0.0.0', port=port)
            """)
            write("requirements.txt", """\
                flask>=3.0.0
                gunicorn>=21.0.0
            """)
            write("Procfile", "web: gunicorn app:app --bind 0.0.0.0:$PORT --workers 2\n")
            buildpack  = "python_buildpack"
            start_cmd  = "gunicorn app:app --bind 0.0.0.0:$PORT --workers 2"

        # ── Node.js Express ───────────────────────────────────────────────────
        elif rt == "2":
            write("app.js", f"""\
                'use strict';
                const express = require('express');
                const app     = express();
                const port    = process.env.PORT || 8080;

                app.get('/', (req, res) => {{
                    res.json({{
                        app:    '{name}',
                        status: 'running',
                        env:    process.env.APP_ENV || 'production',
                    }});
                }});

                app.get('/health', (req, res) => {{
                    res.json({{ status: 'ok' }});
                }});

                app.listen(port, () => {{
                    console.log(`{name} listening on port ${{port}}`);
                }});
            """)
            pkg = {
                "name": name,
                "version": "1.0.0",
                "description": desc or f"SAP BTP CF app: {name}",
                "main": "app.js",
                "scripts": {"start": "node app.js"},
                "dependencies": {"express": "^4.18.0"},
                "engines": {"node": ">=18.0.0"},
            }
            write("package.json", json.dumps(pkg, indent=2) + "\n")
            buildpack  = "nodejs_buildpack"
            start_cmd  = "npm start"

        # ── Static HTML5 ──────────────────────────────────────────────────────
        elif rt == "3":
            write("index.html", f"""\
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="UTF-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                    <title>{name}</title>
                    <style>
                        body {{ font-family: '72', Arial, sans-serif; background: #f5f5f5;
                               display: flex; align-items: center; justify-content: center;
                               min-height: 100vh; margin: 0; }}
                        .card {{ background: white; border-radius: 8px; padding: 2rem 3rem;
                                box-shadow: 0 2px 12px rgba(0,0,0,.1); text-align: center; }}
                        h1 {{ color: #0070f2; }}
                        p  {{ color: #6a6d70; }}
                    </style>
                </head>
                <body>
                    <div class="card">
                        <h1>{name}</h1>
                        <p>{desc or "Running on SAP BTP Cloud Foundry"}</p>
                        <p><small>Deployed with btp-auto</small></p>
                    </div>
                </body>
                </html>
            """)
            write("Staticfile", "")   # presence triggers staticfile buildpack
            buildpack  = "staticfile_buildpack"
            start_cmd  = None

        # ── manifest.yml ─────────────────────────────────────────────────────
        manifest_lines = [
            "applications:",
            f"- name: {name}",
            f"  memory: {memory}",
            f"  instances: {inst}",
            "  buildpacks:",
            f"  - {buildpack}",
        ]
        if start_cmd:
            manifest_lines.append(f"  command: {start_cmd}")
        if desc:
            manifest_lines.append(f"  metadata:")
            manifest_lines.append(f"    annotations:")
            manifest_lines.append(f"      description: \"{desc}\"")
        manifest_lines += [
            "  env:",
            f"    APP_NAME: {name}",
            "    APP_ENV: production",
            "",
        ]
        (app_dir / "manifest.yml").write_text("\n".join(manifest_lines))

        # ── report ────────────────────────────────────────────────────────────
        section(f"App scaffolded → {app_dir}")
        files = [f.name for f in sorted(app_dir.iterdir())]
        console.print(f"    [green]{'  '.join(files)}[/green]")
        out.success(f"Directory: {app_dir.resolve()}")
        return str(app_dir)

    def menu_cf():
        items = {
            "1":  "Show CF Target",
            "2":  "List Spaces",
            "3":  "Create Space",
            "4":  "Delete Space",
            "5":  "── Apps ──",
            "6":  "List Apps",
            "7":  "Scaffold & Create New App",
            "8":  "Push Existing App",
            "9":  "Delete App",
            "10": "Start / Stop / Restage App",
            "11": "Recent App Logs",
            "12": "App Environment Variables",
            "13": "Set Environment Variable",
            "14": "── CF Services ──",
            "15": "List CF Services",
            "16": "Create CF Service Instance",
            "17": "Delete CF Service Instance",
            "18": "Bind Service to App",
            "19": "Unbind Service from App",
            "20": "Create Service Key",
            "21": "Get Service Key (credentials)",
            "0":  "Back",
        }
        while True:
            choice = menu("Cloud Foundry", items)
            try:
                if choice == "1":
                    out.info(cf_cli.target())
                elif choice == "2":
                    spaces = cf_cli.list_spaces()
                    out.print_table([{"name": s} for s in spaces], columns=["name"])
                elif choice == "3":
                    name = ask("Space name")
                    if name:
                        out.info(cf_cli.create_space(name))
                        out.success(f"Space '{name}' created")
                elif choice == "4":
                    name = ask("Space name to delete")
                    if name and confirm(f"Delete space '{name}'?"):
                        cf_cli.delete_space(name)
                        out.success(f"Space '{name}' deleted")
                elif choice in ("5", "14"):
                    pass  # section headers
                elif choice == "6":
                    out.info(cf_cli.list_apps())
                elif choice == "7":
                    app_path = _scaffold_app()
                    if app_path and confirm("Push the app to CF now?"):
                        app_name = Path(app_path).name
                        out.info(cf_cli.push_app(app_name, app_path, no_start=False))
                elif choice == "8":
                    name = ask("App name")
                    path = ask_with_default("App directory path", ".")
                    no_start = confirm("Push without starting?")
                    if name:
                        out.info(cf_cli.push_app(name, path, no_start))
                elif choice == "9":
                    name = ask("App name")
                    if name and confirm(f"Delete app '{name}' and its routes?"):
                        cf_cli.delete_app(name)
                        out.success(f"App '{name}' deleted")
                elif choice == "10":
                    name = ask("App name")
                    action = menu("App Action", {"1": "Start", "2": "Stop", "3": "Restage", "0": "Cancel"})
                    if name:
                        if action == "1":
                            out.info(cf_cli.start_app(name))
                        elif action == "2":
                            out.info(cf_cli.stop_app(name))
                        elif action == "3":
                            out.info(cf_cli.restage_app(name))
                elif choice == "11":
                    name = ask("App name")
                    if name:
                        out.info(cf_cli.recent_logs(name))
                elif choice == "12":
                    name = ask("App name")
                    if name:
                        out.info(cf_cli.get_env(name))
                elif choice == "13":
                    name  = ask("App name")
                    key   = ask("Variable name")
                    value = ask("Variable value")
                    if name and key and value:
                        cf_cli.set_env(name, key, value)
                        out.success(f"Set {key} on {name}")
                elif choice == "15":
                    out.info(cf_cli.list_services())
                elif choice == "16":
                    service = ask("Service offering (e.g. destination)")
                    plan    = ask("Plan (e.g. lite)")
                    name    = ask("Instance name")
                    params  = ask_optional("JSON parameters")
                    if service and plan and name:
                        out.info(cf_cli.create_service(service, plan, name, params or None))
                        out.success(f"CF service '{name}' created")
                elif choice == "17":
                    name = ask("CF service instance name")
                    if name and confirm(f"Delete CF service '{name}'?"):
                        cf_cli.delete_service(name)
                        out.success("Deleted")
                elif choice == "18":
                    app     = ask("App name")
                    service = ask("Service instance name")
                    if app and service:
                        out.info(cf_cli.bind_service(app, service))
                        out.success(f"Bound '{service}' to '{app}'")
                elif choice == "19":
                    app     = ask("App name")
                    service = ask("Service instance name")
                    if app and service:
                        out.info(cf_cli.unbind_service(app, service))
                        out.success("Unbound")
                elif choice == "20":
                    service  = ask("CF service instance name")
                    key_name = ask("Key name")
                    if service and key_name:
                        out.info(cf_cli.create_service_key(service, key_name))
                        out.success(f"Key '{key_name}' created")
                elif choice == "21":
                    service  = ask("CF service instance name")
                    key_name = ask("Key name")
                    if service and key_name:
                        out.info(cf_cli.get_service_key(service, key_name))
                elif choice == "0":
                    break
            except BTPError as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── DESTINATIONS submenu ──────────────────────────────────────────────────

    def menu_destinations():
        items = {
            "1": "List Destinations",
            "2": "Get Destination",
            "3": "Create HTTP Destination",
            "4": "Create OAuth2 Destination",
            "5": "Delete Destination",
            "0": "Back",
        }
        while True:
            choice = menu("Destinations", items)
            if choice == "0":
                break
            try:
                dest_svc = get_dest_svc()
                if choice == "1":
                    section("Destinations")
                    out.print_table(dest_svc.list(),
                                    columns=["Name", "Type", "URL", "Authentication"])
                elif choice == "2":
                    name = ask("Destination name")
                    if name:
                        out.print_json(dest_svc.get(name))
                elif choice == "3":
                    from btp.destinations import DestinationService as DS
                    name  = ask("Destination name")
                    url   = ask("Target URL")
                    auth  = ask_with_default("Authentication", "NoAuthentication")
                    proxy = ask_with_default("ProxyType", "Internet")
                    user  = ask_optional("Username (BasicAuthentication only)")
                    pw    = ask_optional("Password")
                    if name and url:
                        cfg = DS.http_destination(name, url, auth, proxy,
                                                  user or None, pw or None)
                        dest_svc.create(cfg)
                        out.success(f"Destination '{name}' created")
                elif choice == "4":
                    from btp.destinations import DestinationService as DS
                    name      = ask("Destination name")
                    url       = ask("Target URL")
                    client_id = ask("Client ID")
                    token_url  = ask("Token URL")
                    client_sec = ask("Client secret")
                    if name and url and client_id and client_sec and token_url:
                        cfg = DS.oauth_destination(name, url, client_id, client_sec, token_url)
                        dest_svc.create(cfg)
                        out.success(f"OAuth destination '{name}' created")
                elif choice == "5":
                    name = ask("Destination name to delete")
                    if name and confirm(f"Delete destination '{name}'?"):
                        dest_svc.delete(name)
                        out.success(f"Destination '{name}' deleted")
            except BTPError as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── INTEGRATION SUITE submenu ─────────────────────────────────────────────

    def menu_isuit():
        items = {
            "1": "List Integration Packages",
            "2": "List iFlows in Package",
            "3": "Deploy iFlow",
            "4": "Undeploy iFlow",
            "5": "List Runtime Artifacts (deployed)",
            "6": "Message Processing Logs",
            "7": "Failed Messages",
            "0": "Back",
        }
        while True:
            choice = menu("SAP Integration Suite", items)
            if choice == "0":
                break
            try:
                is_svc = get_is_svc()
                if choice == "1":
                    section("Integration Packages")
                    out.print_table(is_svc.list_packages(),
                                    columns=["Id", "Name", "Version", "Description"])
                elif choice == "2":
                    pkg_id = ask("Package ID")
                    if pkg_id:
                        out.print_table(is_svc.list_iflows(pkg_id),
                                        columns=["Id", "Name", "Version", "Description"])
                elif choice == "3":
                    iflow_id = ask("iFlow ID")
                    version  = ask_with_default("Version", "active")
                    if iflow_id:
                        r = is_svc.deploy_iflow(iflow_id, version)
                        out.success(f"Deploy triggered for '{iflow_id}'")
                        out.print_json(r)
                elif choice == "4":
                    iflow_id = ask("iFlow ID to undeploy")
                    if iflow_id and confirm(f"Undeploy '{iflow_id}' from runtime?"):
                        is_svc.undeploy_iflow(iflow_id)
                        out.success(f"'{iflow_id}' undeployed")
                elif choice == "5":
                    section("Runtime Artifacts")
                    out.print_table(is_svc.list_runtime_artifacts(),
                                    columns=["Id", "Name", "Status", "Version"])
                elif choice == "6":
                    top = ask_with_default("Number of logs", "20")
                    out.print_table(
                        is_svc.get_message_logs(int(top)),
                        columns=["MessageGuid", "Status", "LogStart", "Sender", "Receiver"],
                    )
                elif choice == "7":
                    top = ask_with_default("Number of logs", "20")
                    out.print_table(
                        is_svc.get_failed_messages(int(top)),
                        columns=["MessageGuid", "Status", "LogStart", "Sender", "Receiver"],
                    )
            except BTPError as e:
                out.error(str(e))
            except KeyboardInterrupt:
                break

    # ── SNAPSHOT ─────────────────────────────────────────────────────────────

    def run_snapshot():
        section("Account Snapshot")
        for label, fn, cols in [
            ("Global Account",    accounts_svc.get_global_account,   None),
            ("Subaccounts",       accounts_svc.list_subaccounts,
             ["guid", "displayName", "subdomain", "region", "state"]),
            ("Role Collections",  auth_svc.list_role_collections,
             ["name", "description", "isReadOnly"]),
            ("Service Instances", services_svc.list_instances,
             ["name", "service_plan_id", "usable", "ready"]),
        ]:
            try:
                data = fn()
                out.info(f"\n{label}")
                if isinstance(data, list):
                    out.print_table(data, columns=cols)
                else:
                    out.print_json(data)
            except BTPError as e:
                out.error(f"{label}: {e}")
        out.success("Snapshot complete")

    # ── MAIN LOOP ─────────────────────────────────────────────────────────────

    MAIN_MENU = {
        "1": ("Accounts & Hierarchy",         menu_accounts),
        "2": ("Entitlements",                  menu_entitlements),
        "3": ("Provisioning / Environments",   menu_provisioning),
        "4": ("Security: Roles & Users",       menu_security),
        "5": ("Services: Instances & Bindings", menu_services),
        "6": ("Cloud Foundry",                 menu_cf),
        "7": ("Destinations",                  menu_destinations),
        "8": ("SAP Integration Suite",         menu_isuit),
        "9": ("Account Snapshot",              run_snapshot),
        "0": ("Exit",                          None),
    }

    banner()
    while True:
        choice = menu("Main Menu", {k: v[0] for k, v in MAIN_MENU.items()})
        label, action = MAIN_MENU[choice]
        if choice == "0":
            console.print("\n[dim]Goodbye.[/dim]\n")
            break
        try:
            action()
        except KeyboardInterrupt:
            pass


# ══════════════════════════════════════════════════════════════════════════════
# INTERACTIVE COMMAND (also the default when run with no arguments)
# ══════════════════════════════════════════════════════════════════════════════

@cli.command("interactive")
def interactive_mode():
    """Launch the full interactive BTP management shell."""
    _launch_interactive()


if __name__ == "__main__":
    if len(sys.argv) == 1:
        # No arguments → go straight to the interactive TUI
        _launch_interactive()
    else:
        cli(obj={})
