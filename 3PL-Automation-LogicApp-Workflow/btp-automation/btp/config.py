"""Centralized BTP configuration loader."""
import os
from pathlib import Path
import yaml
from dotenv import load_dotenv

_CONFIG_DIR = Path(__file__).parent.parent / "config"
_ENV_FILE = Path(__file__).parent.parent / ".env"


class BTPConfig:
    """Single source of truth for all BTP connection settings."""

    def __init__(self, config_file: str = None, env_file: str = None):
        load_dotenv(env_file or _ENV_FILE)
        cfg_path = config_file or (_CONFIG_DIR / "btp_config.yaml")
        with open(cfg_path) as f:
            self._cfg = yaml.safe_load(f)

    # ── account identifiers ──────────────────────────────────────────────────
    @property
    def global_account_guid(self) -> str:
        return self._cfg["global_account"]["guid"]

    @property
    def global_account_subdomain(self) -> str:
        return self._cfg["global_account"]["subdomain"]

    @property
    def subaccount_guid(self) -> str:
        return self._cfg["subaccount"]["guid"]

    @property
    def subaccount_subdomain(self) -> str:
        return self._cfg["subaccount"]["subdomain"]

    # ── endpoints ────────────────────────────────────────────────────────────
    @property
    def accounts_url(self) -> str:
        return self._cfg["endpoints"]["accounts_service"]

    @property
    def entitlements_url(self) -> str:
        return self._cfg["endpoints"]["entitlements_service"]

    @property
    def provisioning_url(self) -> str:
        return self._cfg["endpoints"]["provisioning_service"]

    @property
    def xsuaa_url(self) -> str:
        return self._cfg["endpoints"]["xsuaa_api"]

    @property
    def cf_api_url(self) -> str:
        return self._cfg["cloud_foundry"]["api_endpoint"]

    # ── CIS credentials ──────────────────────────────────────────────────────
    @property
    def cis_client_id(self) -> str:
        key = self._cfg["auth"]["cis_central"]["client_id_env"]
        return os.environ[key]

    @property
    def cis_client_secret(self) -> str:
        key = self._cfg["auth"]["cis_central"]["client_secret_env"]
        return os.environ[key]

    @property
    def cis_token_url(self) -> str:
        return self._cfg["auth"]["cis_central"]["token_url"]

    # ── XSUAA credentials ────────────────────────────────────────────────────
    @property
    def xsuaa_client_id(self) -> str:
        key = self._cfg["auth"]["xsuaa"]["client_id_env"]
        return os.environ[key]

    @property
    def xsuaa_client_secret(self) -> str:
        key = self._cfg["auth"]["xsuaa"]["client_secret_env"]
        return os.environ[key]

    @property
    def xsuaa_token_url(self) -> str:
        return self._cfg["auth"]["xsuaa"]["token_url"]

    # ── API path prefixes ────────────────────────────────────────────────────
    @property
    def accounts_path(self) -> str:
        return self._cfg["api_paths"]["accounts"]

    @property
    def entitlements_path(self) -> str:
        return self._cfg["api_paths"]["entitlements"]

    @property
    def provisioning_path(self) -> str:
        return self._cfg["api_paths"]["provisioning"]

    @property
    def authorization_path(self) -> str:
        return self._cfg["api_paths"]["authorization"]

    # ── defaults ─────────────────────────────────────────────────────────────
    @property
    def timeout(self) -> int:
        return self._cfg["defaults"]["timeout_seconds"]

    @property
    def output_format(self) -> str:
        return self._cfg["defaults"].get("output_format", "table")
