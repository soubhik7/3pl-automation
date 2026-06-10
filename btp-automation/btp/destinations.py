"""
Destination Service REST API.

Reads credentials from a BTP service binding (created via `btp create services/binding`)
and calls the Destination Service REST API to manage HTTP/RFC/LDAP destinations.

Typical setup:
    1. You already have a `destination` service instance (e.g. btp-mcp-destination)
    2. Create a binding:  btp create services/binding --binding dest-key --instance-name btp-mcp-destination --subaccount <guid>
    3. DestinationService auto-detects the binding and uses its credentials.
"""
import json
import requests
from typing import Dict, List, Optional

from .exceptions import BTPError, BTPAuthError
from . import btp_cli as cli


# ── helpers ───────────────────────────────────────────────────────────────────

def _fetch_token(credentials: Dict) -> str:
    """Client-credentials OAuth token from destination service binding creds."""
    token_url = credentials.get("url") or credentials.get("tokenServiceURL", "")
    if not token_url:
        raise BTPError("Token URL missing from destination service credentials")
    if not token_url.endswith("/oauth/token"):
        token_url = token_url.rstrip("/") + "/oauth/token"
    resp = requests.post(
        token_url,
        data={
            "grant_type": "client_credentials",
            "client_id": credentials["clientid"],
            "client_secret": credentials["clientsecret"],
        },
        timeout=30,
    )
    if resp.status_code == 401:
        raise BTPAuthError("Destination service OAuth failed — check client credentials")
    resp.raise_for_status()
    return resp.json()["access_token"]


def _extract_credentials(binding: Dict) -> Dict:
    """Pull the nested credentials dict out of a BTP binding response."""
    creds = binding.get("credentials")
    if isinstance(creds, str):
        try:
            creds = json.loads(creds)
        except json.JSONDecodeError:
            creds = None
    if not creds:
        raise BTPError(
            "Binding has no credentials. Make sure the binding is fully provisioned.\n"
            "Re-fetch with: btp get services/binding --name <binding> --subaccount <guid>"
        )
    return creds


# ── service class ─────────────────────────────────────────────────────────────

class DestinationService:
    """
    CRUD for BTP Destinations via the Destination Service REST API.

    Auto-detects a destination service binding in the subaccount.
    Pass `binding_name` explicitly if you have multiple bindings.
    """

    _SUBACCOUNT_PATH = "/destination-configuration/v1/subaccountDestinations"
    _INSTANCE_PATH   = "/destination-configuration/v1/instanceDestinations"

    def __init__(self, subaccount_guid: str, binding_name: str = None):
        self._sa = subaccount_guid
        binding_name = binding_name or self._auto_detect_binding()
        binding = cli.get_service_binding(subaccount_guid, binding_name)
        creds = _extract_credentials(binding)
        self._base_url = creds["uri"].rstrip("/")
        self._token = _fetch_token(creds)

    def _auto_detect_binding(self) -> str:
        bindings = cli.list_service_bindings(self._sa)
        for b in bindings:
            name = b.get("name", "").lower()
            instance_name = b.get("context", {}).get("instance_name", "").lower()
            if "destination" in name or "dest" in name:
                return b["name"]
            if "destination" in instance_name or instance_name.startswith("dest"):
                return b["name"]
        raise BTPError(
            "No destination service binding found in this subaccount.\n"
            "Create one first:\n"
            "  btp-auto services create-binding --binding <name> --instance <destination-instance>"
        )

    def _headers(self) -> Dict:
        return {"Authorization": f"Bearer {self._token}", "Content-Type": "application/json"}

    def _get(self, path: str) -> requests.Response:
        resp = requests.get(f"{self._base_url}{path}", headers=self._headers(), timeout=30)
        if resp.status_code == 401:
            raise BTPAuthError("Destination service token expired — re-create the DestinationService object")
        return resp

    # ── subaccount destinations ───────────────────────────────────────────────

    def list(self) -> List[Dict]:
        resp = self._get(self._SUBACCOUNT_PATH)
        resp.raise_for_status()
        return resp.json()

    def get(self, name: str) -> Dict:
        resp = self._get(f"{self._SUBACCOUNT_PATH}/{name}")
        if resp.status_code == 404:
            raise BTPError(f"Destination '{name}' not found")
        resp.raise_for_status()
        return resp.json()

    def create(self, config: Dict) -> Dict:
        """
        Create a destination.

        Minimal HTTP destination config:
            {
                "Name": "my-backend",
                "Type": "HTTP",
                "URL": "https://api.example.com",
                "Authentication": "NoAuthentication",
                "ProxyType": "Internet"
            }

        Minimal RFC destination config:
            {
                "Name": "my-sap-system",
                "Type": "RFC",
                "jco.client.ashost": "<host>",
                "jco.client.sysnr": "00",
                "jco.client.client": "100",
                "Authentication": "BasicAuthentication",
                "User": "<user>",
                "Password": "<password>"
            }
        """
        _validate_destination(config)
        resp = requests.post(
            f"{self._base_url}{self._SUBACCOUNT_PATH}",
            headers=self._headers(),
            json=config,
            timeout=30,
        )
        resp.raise_for_status()
        return config

    def update(self, config: Dict) -> Dict:
        """Full replace of an existing destination."""
        _validate_destination(config)
        resp = requests.put(
            f"{self._base_url}{self._SUBACCOUNT_PATH}",
            headers=self._headers(),
            json=config,
            timeout=30,
        )
        resp.raise_for_status()
        return config

    def delete(self, name: str) -> None:
        resp = requests.delete(
            f"{self._base_url}{self._SUBACCOUNT_PATH}/{name}",
            headers=self._headers(),
            timeout=30,
        )
        if resp.status_code == 404:
            raise BTPError(f"Destination '{name}' not found")
        resp.raise_for_status()

    # ── convenience builders ─────────────────────────────────────────────────

    @staticmethod
    def http_destination(
        name: str,
        url: str,
        auth: str = "NoAuthentication",
        proxy: str = "Internet",
        user: str = None,
        password: str = None,
        extra: Dict = None,
    ) -> Dict:
        """Build a minimal HTTP destination dict ready for create()."""
        cfg = {"Name": name, "Type": "HTTP", "URL": url,
               "Authentication": auth, "ProxyType": proxy}
        if user:
            cfg["User"] = user
        if password:
            cfg["Password"] = password
        if extra:
            cfg.update(extra)
        return cfg

    @staticmethod
    def oauth_destination(
        name: str,
        url: str,
        client_id: str,
        client_secret: str,
        token_url: str,
        extra: Dict = None,
    ) -> Dict:
        """Build an OAuth2 client-credentials destination dict."""
        cfg = {
            "Name": name,
            "Type": "HTTP",
            "URL": url,
            "Authentication": "OAuth2ClientCredentials",
            "ProxyType": "Internet",
            "clientId": client_id,
            "clientSecret": client_secret,
            "tokenServiceURL": token_url,
        }
        if extra:
            cfg.update(extra)
        return cfg


def _validate_destination(config: Dict) -> None:
    for field in ("Name", "Type"):
        if not config.get(field):
            raise BTPError(f"Destination config must include '{field}'")
    if config["Type"] == "HTTP" and not config.get("URL"):
        raise BTPError("HTTP destination must include 'URL'")
