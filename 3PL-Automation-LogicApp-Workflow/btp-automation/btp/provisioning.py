"""Environment instance provisioning — via BTP CLI."""
from typing import Dict, List, Optional
from . import btp_cli as cli


class ProvisioningService:
    """Create, list, and delete runtime environment instances. Uses BTP CLI."""

    def __init__(self, client=None):
        pass  # btp_cli module handles auth; client arg accepted for API consistency

    def list_available_environments(self, subaccount_guid: str) -> List[Dict]:
        return cli.list_available_environments(subaccount_guid)

    def list_environment_instances(self, subaccount_guid: str) -> List[Dict]:
        return cli.list_environment_instances(subaccount_guid)

    def get_environment_instance(
        self, subaccount_guid: str, environment_instance_id: str
    ) -> Dict:
        return cli.get_environment_instance(subaccount_guid, environment_instance_id)

    def create_cf_environment(
        self,
        subaccount_guid: str,
        org_name: str,
        display_name: str = None,
        landscape_label: str = None,
    ) -> Dict:
        return cli.create_environment_instance(
            subaccount_guid=subaccount_guid,
            name=display_name or org_name,
            environment_type="cloudfoundry",
            plan="standard",
            landscape_label=landscape_label,
        )

    def create_kyma_environment(
        self,
        subaccount_guid: str,
        cluster_name: str,
        plan_name: str = "trial",
    ) -> Dict:
        return cli.create_environment_instance(
            subaccount_guid=subaccount_guid,
            name=cluster_name,
            environment_type="kyma",
            plan=plan_name,
        )

    def delete_environment_instance(
        self, subaccount_guid: str, environment_instance_id: str
    ) -> None:
        cli.delete_environment_instance(subaccount_guid, environment_instance_id)
