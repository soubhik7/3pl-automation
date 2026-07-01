"""
SAP Integration Suite REST API.

Covers: integration packages, iFlow design-time artifacts, runtime deployment,
and message-processing log monitoring.

Setup — get credentials from an Integration Suite service key:
    1. Subscribe to Integration Suite in your BTP subaccount
    2. Create a Process Integration Runtime service instance (plan: api)
    3. Create a service key:  cf create-service-key <instance> <key-name>
    4. Read the key:          cf service-key <instance> <key-name>
    5. Pass the key's clientid / clientsecret / tokenUrl / url to this class,
       or store them in .env and reference via BTPConfig.

Environment variables (optional — avoids hard-coding):
    IS_BASE_URL        https://<tenant>.integrationsuite.cfapps.us10.hana.ondemand.com
    IS_TOKEN_URL       https://<tenant>.authentication.us10.hana.ondemand.com/oauth/token
    IS_CLIENT_ID
    IS_CLIENT_SECRET
"""
import os
import requests
from typing import Any, Dict, List, Optional

from .exceptions import BTPError, BTPAuthError


class IntegrationSuiteService:
    """
    Manage SAP Integration Suite via the OData/REST API.

    Pass credentials explicitly or set IS_* environment variables.
    """

    def __init__(
        self,
        base_url: str = None,
        token_url: str = None,
        client_id: str = None,
        client_secret: str = None,
    ):
        self._base_url = (base_url or os.environ.get("IS_BASE_URL", "")).rstrip("/")
        self._client_id     = client_id     or os.environ.get("IS_CLIENT_ID", "")
        self._client_secret = client_secret or os.environ.get("IS_CLIENT_SECRET", "")
        _token_url          = token_url     or os.environ.get("IS_TOKEN_URL", "")

        if not all([self._base_url, self._client_id, self._client_secret, _token_url]):
            raise BTPError(
                "Integration Suite credentials not configured.\n"
                "Set IS_BASE_URL, IS_TOKEN_URL, IS_CLIENT_ID, IS_CLIENT_SECRET in .env\n"
                "or pass them as constructor arguments."
            )
        self._token = self._fetch_token(_token_url)

    # ── auth ──────────────────────────────────────────────────────────────────

    def _fetch_token(self, token_url: str) -> str:
        if not token_url.endswith("/oauth/token"):
            token_url = token_url.rstrip("/") + "/oauth/token"
        resp = requests.post(
            token_url,
            data={
                "grant_type": "client_credentials",
                "client_id": self._client_id,
                "client_secret": self._client_secret,
            },
            timeout=30,
        )
        if resp.status_code == 401:
            raise BTPAuthError("Integration Suite OAuth failed — check IS_CLIENT_ID / IS_CLIENT_SECRET")
        resp.raise_for_status()
        return resp.json()["access_token"]

    def _headers(self, csrf: str = None) -> Dict:
        h = {
            "Authorization": f"Bearer {self._token}",
            "Accept": "application/json",
        }
        if csrf:
            h["X-CSRF-Token"] = csrf
        return h

    def _csrf_token(self) -> str:
        resp = requests.get(
            f"{self._base_url}/api/1.0/IntegrationPackages",
            headers={**self._headers(), "X-CSRF-Token": "Fetch"},
            timeout=30,
        )
        return resp.headers.get("X-CSRF-Token", "")

    def _odata(self, path: str) -> List[Dict]:
        resp = requests.get(
            f"{self._base_url}/api/1.0/{path}",
            headers=self._headers(),
            timeout=30,
        )
        if resp.status_code == 401:
            raise BTPAuthError("Integration Suite token expired — re-create IntegrationSuiteService")
        if resp.status_code == 404:
            raise BTPError(f"Not found: {path}")
        resp.raise_for_status()
        data = resp.json()
        return data.get("d", {}).get("results", data if isinstance(data, list) else [data])

    # ── Design-time: packages ─────────────────────────────────────────────────

    def list_packages(self) -> List[Dict]:
        """List all integration packages."""
        return self._odata("IntegrationPackages")

    def get_package(self, package_id: str) -> Dict:
        items = self._odata(f"IntegrationPackages('{package_id}')")
        return items[0] if items else {}

    # ── Design-time: iFlows ───────────────────────────────────────────────────

    def list_iflows(self, package_id: str) -> List[Dict]:
        """List all integration flows in a package."""
        return self._odata(
            f"IntegrationPackages('{package_id}')/IntegrationDesigntimeArtifacts"
        )

    def get_iflow(self, iflow_id: str, version: str = "active") -> Dict:
        items = self._odata(
            f"IntegrationDesigntimeArtifacts(Id='{iflow_id}',Version='{version}')"
        )
        return items[0] if items else {}

    # ── Runtime: deploy / undeploy ────────────────────────────────────────────

    def deploy_iflow(self, iflow_id: str, version: str = "active") -> Dict:
        """Deploy (or redeploy) an iFlow to the runtime."""
        csrf = self._csrf_token()
        resp = requests.post(
            f"{self._base_url}/api/1.0/DeployIntegrationDesigntimeArtifact"
            f"?Id='{iflow_id}'&Version='{version}'",
            headers={**self._headers(csrf), "Content-Type": "application/json"},
            timeout=60,
        )
        resp.raise_for_status()
        return resp.json() if resp.text else {"status": "deploy_triggered", "id": iflow_id}

    def list_runtime_artifacts(self) -> List[Dict]:
        """List all deployed iFlows on the runtime."""
        return self._odata("IntegrationRuntimeArtifacts")

    def get_runtime_artifact(self, iflow_id: str) -> Dict:
        items = self._odata(f"IntegrationRuntimeArtifacts('{iflow_id}')")
        return items[0] if items else {}

    def undeploy_iflow(self, iflow_id: str) -> None:
        """Remove an iFlow from the runtime."""
        csrf = self._csrf_token()
        resp = requests.delete(
            f"{self._base_url}/api/1.0/IntegrationRuntimeArtifacts('{iflow_id}')",
            headers=self._headers(csrf),
            timeout=30,
        )
        if resp.status_code == 404:
            raise BTPError(f"Runtime artifact '{iflow_id}' not found (may already be undeployed)")
        resp.raise_for_status()

    # ── Monitoring: message processing logs ───────────────────────────────────

    def get_message_logs(self, top: int = 20, status: str = None) -> List[Dict]:
        """
        Get message processing logs.
        status: COMPLETED | FAILED | PROCESSING | RETRY | ABANDONED
        """
        path = f"MessageProcessingLogs?$top={top}&$orderby=LogStart desc"
        if status:
            path += f"&$filter=Status eq '{status}'"
        return self._odata(path)

    def get_failed_messages(self, top: int = 20) -> List[Dict]:
        return self.get_message_logs(top=top, status="FAILED")

    def get_log_attachments(self, message_guid: str) -> List[Dict]:
        return self._odata(
            f"MessageProcessingLogs('{message_guid}')/Attachments"
        )

    # ── Value mappings ────────────────────────────────────────────────────────

    def list_value_mappings(self, package_id: str) -> List[Dict]:
        return self._odata(
            f"IntegrationPackages('{package_id}')/ValueMappingDesigntimeArtifacts"
        )
