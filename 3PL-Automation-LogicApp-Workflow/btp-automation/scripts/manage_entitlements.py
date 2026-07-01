#!/usr/bin/env python3
"""
Bulk entitlement management script.
Edit the ASSIGNMENTS list below and run to apply changes.

Usage:
    python scripts/manage_entitlements.py --apply
    python scripts/manage_entitlements.py --list
    python scripts/manage_entitlements.py --list --subaccount <guid>
"""
import sys
import argparse
sys.path.insert(0, str(__import__('pathlib').Path(__file__).parent.parent))

from btp.config import BTPConfig
from btp.client import BTPClient
from btp.entitlements import EntitlementsService
from btp.exceptions import BTPError
import btp.output as out

# ── Configure your desired assignments here ───────────────────────────────────
ASSIGNMENTS = [
    # {"subaccount_guid": "YOUR-GUID", "service_name": "hana-cloud", "plan_name": "hana"},
    # {"subaccount_guid": "YOUR-GUID", "service_name": "destination", "plan_name": "lite"},
    # {"subaccount_guid": "YOUR-GUID", "service_name": "connectivity", "plan_name": "lite"},
]

UNASSIGNMENTS = [
    # {"subaccount_guid": "YOUR-GUID", "service_name": "old-service", "plan_name": "plan"},
]


def main():
    parser = argparse.ArgumentParser(description="BTP Entitlement Manager")
    parser.add_argument("--apply", action="store_true", help="Apply ASSIGNMENTS list")
    parser.add_argument("--list", action="store_true", help="List current assignments")
    parser.add_argument("--subaccount", default=None, help="Filter by subaccount GUID")
    parser.add_argument("--service", default=None, help="Filter by service name")
    args = parser.parse_args()

    cfg = BTPConfig()
    svc = EntitlementsService(BTPClient(cfg))

    if args.list:
        out.info("Fetching entitlement assignments...")
        try:
            data = svc.list_assignments(
                subaccount_guid=args.subaccount,
                service_name=args.service,
            )
            out.print_table(data, title="Entitlements", columns=["name", "displayName"])
        except BTPError as e:
            out.error(str(e))
            sys.exit(1)

    if args.apply:
        if not ASSIGNMENTS and not UNASSIGNMENTS:
            out.warn("No assignments configured. Edit the ASSIGNMENTS list in this script.")
            return

        if ASSIGNMENTS:
            out.info(f"Applying {len(ASSIGNMENTS)} assignment(s)...")
            try:
                svc.bulk_assign(ASSIGNMENTS)
                out.success("Assignments applied")
            except BTPError as e:
                out.error(f"Assignment failed: {e}")
                sys.exit(1)

        for u in UNASSIGNMENTS:
            try:
                svc.unassign_entitlement(u["subaccount_guid"], u["service_name"], u["plan_name"])
                out.success(f"Unassigned {u['service_name']}/{u['plan_name']}")
            except BTPError as e:
                out.error(f"Unassign failed: {e}")

    if not args.list and not args.apply:
        parser.print_help()


if __name__ == "__main__":
    main()
