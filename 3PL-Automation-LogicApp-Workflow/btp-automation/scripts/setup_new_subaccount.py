#!/usr/bin/env python3
"""
End-to-end subaccount setup automation.
Creates a subaccount, assigns entitlements, and enables CF environment.

Edit CONFIG below and run:
    python scripts/setup_new_subaccount.py
"""
import sys
import time
sys.path.insert(0, str(__import__('pathlib').Path(__file__).parent.parent))

from btp.config import BTPConfig
from btp.client import BTPClient
from btp.accounts import AccountsService
from btp.entitlements import EntitlementsService
from btp.provisioning import ProvisioningService
from btp.exceptions import BTPError, BTPConflictError
import btp.output as out

# ── Configure your new subaccount ─────────────────────────────────────────────
CONFIG = {
    "subaccount": {
        "display_name": "automation-test",
        "subdomain": "automation-test-sub",
        "region": "us10",
        "description": "Created by BTP automation script",
    },
    "entitlements": [
        # {"service_name": "destination", "plan_name": "lite"},
        # {"service_name": "connectivity", "plan_name": "lite"},
        # {"service_name": "xsuaa", "plan_name": "application"},
    ],
    "enable_cf": False,  # set True to provision CF org
    "cf_org_name": "automation-test-org",
    "cf_landscape": "cf-us10-001",
}


def main():
    cfg = BTPConfig()
    client = BTPClient(cfg)
    accounts = AccountsService(client)
    entitlements = EntitlementsService(client)
    provisioning = ProvisioningService(client)

    sub_cfg = CONFIG["subaccount"]
    out.info(f"Setting up subaccount: {sub_cfg['display_name']}")

    # 1. Create subaccount
    try:
        result = accounts.create_subaccount(
            display_name=sub_cfg["display_name"],
            subdomain=sub_cfg["subdomain"],
            region=sub_cfg["region"],
            description=sub_cfg.get("description", ""),
        )
        sub_guid = result.get("guid")
        out.success(f"Subaccount created: {sub_guid}")
    except BTPConflictError:
        # Already exists — find it
        subs = accounts.list_subaccounts()
        sub_guid = next(
            (s["guid"] for s in subs if s.get("subdomain") == sub_cfg["subdomain"]), None
        )
        if not sub_guid:
            out.error("Subaccount conflict but could not find existing GUID")
            sys.exit(1)
        out.warn(f"Subaccount already exists: {sub_guid}")
    except BTPError as e:
        out.error(f"Failed to create subaccount: {e}")
        sys.exit(1)

    # 2. Assign entitlements
    if CONFIG["entitlements"]:
        out.info("Assigning entitlements...")
        assignments = [
            {"subaccount_guid": sub_guid, **e}
            for e in CONFIG["entitlements"]
        ]
        try:
            entitlements.bulk_assign(assignments)
            out.success(f"Assigned {len(assignments)} entitlement(s)")
        except BTPError as e:
            out.error(f"Entitlement assignment failed: {e}")

    # 3. Enable Cloud Foundry
    if CONFIG["enable_cf"]:
        out.info("Provisioning CF environment...")
        try:
            env = provisioning.create_cf_environment(
                subaccount_guid=sub_guid,
                org_name=CONFIG["cf_org_name"],
                landscape_label=CONFIG["cf_landscape"],
            )
            out.success(f"CF environment provisioning started: {env.get('environmentInstanceID')}")
        except BTPError as e:
            out.error(f"CF provisioning failed: {e}")

    out.success(f"\nSubaccount setup complete. GUID: {sub_guid}")


if __name__ == "__main__":
    main()
