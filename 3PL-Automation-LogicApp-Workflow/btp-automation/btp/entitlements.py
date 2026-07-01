"""Entitlement assignments — via BTP CLI."""
from typing import Dict, List, Optional
from . import btp_cli as cli


class EntitlementsService:
    """Manage service plan entitlements. Uses BTP CLI under the hood."""

    def __init__(self, client=None):
        pass  # btp_cli module handles auth; client arg accepted for API consistency

    def list_assignments(
        self,
        subaccount_guid: str = None,
        service_name: str = None,
    ) -> List[Dict]:
        items = cli.list_entitlements(subaccount_guid)
        if service_name:
            items = [s for s in items if s.get("name", "").lower() == service_name.lower()]
        return items

    def get_allowed_data_centers(self) -> List[Dict]:
        return cli.list_available_regions()

    def assign_entitlement(
        self,
        subaccount_guid: str,
        service_name: str,
        plan_name: str,
        amount: int = None,
    ) -> None:
        cli.assign_entitlement(subaccount_guid, service_name, plan_name, amount)

    def unassign_entitlement(
        self,
        subaccount_guid: str,
        service_name: str,
        plan_name: str,
    ) -> None:
        cli.unassign_entitlement(subaccount_guid, service_name, plan_name)

    def bulk_assign(self, assignments: List[Dict]) -> None:
        """
        Bulk-assign entitlements.
        Each entry: {subaccount_guid, service_name, plan_name, amount?}
        """
        for a in assignments:
            self.assign_entitlement(
                a["subaccount_guid"],
                a["service_name"],
                a["plan_name"],
                a.get("amount"),
            )
