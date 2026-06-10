#!/usr/bin/env python3
"""Show a full snapshot of your BTP account: global account, subaccounts, entitlements, environments, security, services."""
import sys
sys.path.insert(0, str(__import__('pathlib').Path(__file__).parent.parent))

from btp.accounts import AccountsService
from btp.entitlements import EntitlementsService
from btp.provisioning import ProvisioningService
from btp.authorization import AuthorizationService
from btp.services import ServicesService
from btp.exceptions import BTPError
import btp.output as out


def main():
    accounts = AccountsService()
    entitlements = EntitlementsService()
    provisioning = ProvisioningService()
    auth = AuthorizationService()
    services = ServicesService()

    # ── Global Account ────────────────────────────────────────────────────────
    out.info("Fetching Global Account...")
    try:
        ga = accounts.get_global_account()
        out.print_table([ga], title="Global Account",
                        columns=["guid", "displayName", "subdomain", "entityState"])
    except BTPError as e:
        out.error(f"Global account: {e}")

    # ── Subaccounts ───────────────────────────────────────────────────────────
    out.info("\nFetching Subaccounts...")
    subaccount_guids = []
    try:
        subs = accounts.list_subaccounts()
        subaccount_guids = [s.get("guid") for s in subs if s.get("guid")]
        out.print_table(subs, title="Subaccounts",
                        columns=["guid", "displayName", "subdomain", "region", "state"])
    except BTPError as e:
        out.error(f"Subaccounts: {e}")

    # ── Directories ───────────────────────────────────────────────────────────
    out.info("\nFetching Directories...")
    try:
        dirs = accounts.list_directories()
        out.print_table(dirs, title="Directories",
                        columns=["guid", "displayName", "state"])
    except BTPError as e:
        out.error(f"Directories: {e}")

    # ── Entitlements ──────────────────────────────────────────────────────────
    out.info("\nFetching Entitlement Assignments...")
    try:
        ents = entitlements.list_assignments()
        out.print_table(ents, title="Entitlements",
                        columns=["name", "displayName"])
    except BTPError as e:
        out.error(f"Entitlements: {e}")

    # ── Data Centers ──────────────────────────────────────────────────────────
    out.info("\nFetching Allowed Data Centers...")
    try:
        dcs = entitlements.get_allowed_data_centers()
        out.print_table(dcs, title="Allowed Data Centers",
                        columns=["name", "displayName", "region", "environment"])
    except BTPError as e:
        out.error(f"Data centers: {e}")

    # ── Environments per subaccount ───────────────────────────────────────────
    for guid in subaccount_guids[:3]:
        out.info(f"\nEnvironment instances in subaccount {guid}...")
        try:
            envs = provisioning.list_environment_instances(guid)
            out.print_table(envs, title=f"Environments ({guid[:8]}...)",
                            columns=["id", "environmentType", "name", "state"])
        except BTPError as e:
            out.error(f"  Environments: {e}")

    # ── Security: Role Collections ────────────────────────────────────────────
    for guid in subaccount_guids[:1]:
        out.info(f"\nFetching Role Collections for subaccount {guid[:8]}...")
        try:
            rcs = auth.list_role_collections(guid)
            out.print_table(rcs, title="Role Collections",
                            columns=["name", "description", "isReadOnly"])
        except BTPError as e:
            out.error(f"Role collections: {e}")

        # ── Security: Users ───────────────────────────────────────────────────
        out.info("\nFetching Users...")
        try:
            users = auth.list_users(guid)
            if isinstance(users, list) and users and isinstance(users[0], str):
                out.print_table([{"email": u} for u in users],
                                title="Users", columns=["email"])
            else:
                out.print_table(users, title="Users", columns=["id", "userName"])
        except BTPError as e:
            out.error(f"Users: {e}")

        # ── Services ──────────────────────────────────────────────────────────
        out.info("\nFetching Service Offerings...")
        try:
            offerings = services.list_offerings(guid)
            out.print_table(offerings, title="Service Offerings",
                            columns=["name", "displayName", "description"])
        except BTPError as e:
            out.error(f"Service offerings: {e}")

        out.info("\nFetching Service Instances...")
        try:
            instances = services.list_instances(guid)
            out.print_table(instances, title="Service Instances",
                            columns=["name", "service_plan_id", "usable", "ready"])
        except BTPError as e:
            out.error(f"Service instances: {e}")

    out.success("\nSnapshot complete.")


if __name__ == "__main__":
    main()
