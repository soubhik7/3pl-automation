"""Authorization management — roles, role collections, users — via BTP CLI."""
from typing import Dict, List, Optional
from . import btp_cli as cli
from .config import BTPConfig


class AuthorizationService:
    """Manage XSUAA roles, role collections, and user assignments in a subaccount."""

    def __init__(self, client=None, subaccount_guid: str = None):
        cfg = BTPConfig()
        self._sa = subaccount_guid or cfg.subaccount_guid

    # ── Applications ──────────────────────────────────────────────────────────

    def list_applications(self, subaccount_guid: str = None) -> List[Dict]:
        return cli.list_apps(subaccount_guid or self._sa)

    # ── Roles ─────────────────────────────────────────────────────────────────

    def list_roles(self, subaccount_guid: str = None) -> List[Dict]:
        return cli.list_roles(subaccount_guid or self._sa)

    def create_role(
        self,
        role_name: str,
        role_template_name: str,
        app_id: str,
        description: str = "",
        subaccount_guid: str = None,
    ) -> Dict:
        return cli.create_role(
            role_name, role_template_name, app_id, description,
            subaccount_guid or self._sa,
        )

    def delete_role(
        self,
        role_name: str,
        role_template_name: str,
        app_id: str,
        subaccount_guid: str = None,
    ) -> None:
        cli.delete_role(role_name, role_template_name, app_id, subaccount_guid or self._sa)

    # ── Role Collections ──────────────────────────────────────────────────────

    def list_role_collections(self, subaccount_guid: str = None) -> List[Dict]:
        return cli.list_role_collections(subaccount_guid or self._sa)

    def get_role_collection(self, name: str, subaccount_guid: str = None) -> Dict:
        return cli.get_role_collection(name, subaccount_guid or self._sa)

    def create_role_collection(
        self, name: str, description: str = "", subaccount_guid: str = None
    ) -> Dict:
        return cli.create_role_collection(name, description, subaccount_guid or self._sa)

    def update_role_collection(
        self, name: str, description: str, subaccount_guid: str = None
    ) -> Dict:
        return cli.update_role_collection(name, description, subaccount_guid or self._sa)

    def delete_role_collection(self, name: str, subaccount_guid: str = None) -> None:
        cli.delete_role_collection(name, subaccount_guid or self._sa)

    def add_role_to_collection(
        self,
        collection_name: str,
        role_name: str,
        role_template_name: str,
        app_id: str,
        subaccount_guid: str = None,
    ) -> None:
        cli.add_role_to_collection(
            role_name, role_template_name, app_id, collection_name,
            subaccount_guid or self._sa,
        )

    def remove_role_from_collection(
        self,
        collection_name: str,
        role_name: str,
        role_template_name: str,
        app_id: str,
        subaccount_guid: str = None,
    ) -> None:
        cli.remove_role_from_collection(
            role_name, role_template_name, app_id, collection_name,
            subaccount_guid or self._sa,
        )

    # ── Users ─────────────────────────────────────────────────────────────────

    def list_users(self, subaccount_guid: str = None) -> List[str]:
        return cli.list_users(subaccount_guid or self._sa)

    def assign_user_to_collection(
        self,
        collection_name: str,
        username: str,
        origin: str = "sap.default",
        subaccount_guid: str = None,
    ) -> None:
        cli.assign_user_to_collection(
            collection_name, username, subaccount_guid or self._sa, origin
        )

    def remove_user_from_collection(
        self,
        collection_name: str,
        username: str,
        origin: str = "sap.default",
        subaccount_guid: str = None,
    ) -> None:
        cli.unassign_user_from_collection(
            collection_name, username, subaccount_guid or self._sa, origin
        )
