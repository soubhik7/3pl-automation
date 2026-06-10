"""Global account, subaccounts, and directories — via BTP CLI."""
from typing import Dict, List, Optional
from . import btp_cli as cli


class AccountsService:
    """Full CRUD for BTP account hierarchy. Uses BTP CLI under the hood."""

    def __init__(self, client=None):
        pass  # btp_cli module handles auth; client arg accepted for API consistency

    # ── Global Account ───────────────────────────────────────────────────────

    def get_global_account(self) -> Dict:
        return cli.get_global_account()

    # ── Subaccounts ──────────────────────────────────────────────────────────

    def list_subaccounts(self) -> List[Dict]:
        return cli.list_subaccounts()

    def get_subaccount(self, subaccount_guid: str) -> Dict:
        return cli.get_subaccount(subaccount_guid)

    def create_subaccount(
        self,
        display_name: str,
        subdomain: str,
        region: str,
        description: str = "",
        beta_enabled: bool = False,
        parent_guid: str = None,
    ) -> Dict:
        return cli.create_subaccount(
            display_name, subdomain, region, description, beta_enabled, parent_guid
        )

    def update_subaccount(
        self,
        subaccount_guid: str,
        display_name: str = None,
        description: str = None,
    ) -> Dict:
        return cli.update_subaccount(subaccount_guid, display_name, description)

    def delete_subaccount(self, subaccount_guid: str) -> None:
        cli.delete_subaccount(subaccount_guid)

    # ── Directories ──────────────────────────────────────────────────────────

    def list_directories(self) -> List[Dict]:
        return cli.list_directories()

    def get_directory(self, directory_guid: str) -> Dict:
        return cli.get_directory(directory_guid)

    def create_directory(
        self,
        display_name: str,
        description: str = "",
        parent_guid: str = None,
    ) -> Dict:
        return cli.create_directory(display_name, description, parent_guid)

    def update_directory(
        self,
        directory_guid: str,
        display_name: str = None,
        description: str = None,
    ) -> Dict:
        return cli.update_directory(directory_guid, display_name, description)

    def delete_directory(self, directory_guid: str) -> None:
        cli.delete_directory(directory_guid)
