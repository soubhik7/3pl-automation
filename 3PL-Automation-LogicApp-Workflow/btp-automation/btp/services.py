"""CF service marketplace management — via BTP CLI."""
import json
from typing import Dict, List, Optional

from . import btp_cli as cli
from .config import BTPConfig


class ServicesService:
    """Browse and manage CF service instances, bindings, and offerings."""

    def __init__(self, client=None, subaccount_guid: str = None):
        self._sa = subaccount_guid or BTPConfig().subaccount_guid

    # ── Offerings & plans ─────────────────────────────────────────────────────

    def list_offerings(self, subaccount_guid: str = None) -> List[Dict]:
        return cli.list_service_offerings(subaccount_guid or self._sa)

    def list_plans(self, offering_name: str = None, subaccount_guid: str = None) -> List[Dict]:
        return cli.list_service_plans(subaccount_guid or self._sa, offering_name)

    # ── Instance lifecycle ────────────────────────────────────────────────────

    def list_instances(self, subaccount_guid: str = None) -> List[Dict]:
        return cli.list_service_instances(subaccount_guid or self._sa)

    def create_instance(
        self,
        name: str,
        offering_name: str,
        plan_name: str,
        parameters: Dict = None,
        subaccount_guid: str = None,
    ) -> Dict:
        params_str = json.dumps(parameters) if parameters else None
        return cli.create_service_instance(
            subaccount_guid or self._sa,
            name,
            offering_name,
            plan_name,
            params_str,
        )

    def delete_instance(self, name: str, subaccount_guid: str = None) -> None:
        cli.delete_service_instance(subaccount_guid or self._sa, name)

    # ── Binding lifecycle ─────────────────────────────────────────────────────

    def list_bindings(self, subaccount_guid: str = None) -> List[Dict]:
        return cli.list_service_bindings(subaccount_guid or self._sa)

    def create_binding(
        self,
        binding_name: str,
        instance_name: str,
        parameters: Dict = None,
        subaccount_guid: str = None,
    ) -> Dict:
        params_str = json.dumps(parameters) if parameters else None
        return cli.create_service_binding(
            subaccount_guid or self._sa,
            binding_name,
            instance_name,
            params_str,
        )

    def get_binding(self, binding_name: str, subaccount_guid: str = None) -> Dict:
        return cli.get_service_binding(subaccount_guid or self._sa, binding_name)

    def delete_binding(self, binding_name: str, subaccount_guid: str = None) -> None:
        cli.delete_service_binding(subaccount_guid or self._sa, binding_name)
