"""HTTP client used only for XSUAA REST API (roles, role-collections).
All account/entitlement/provisioning operations go through btp_cli.py."""
import time
import requests
from typing import Any, Dict

from .auth import TokenManager
from .config import BTPConfig
from .exceptions import (
    BTPAuthError, BTPConflictError, BTPError, BTPNotFoundError, BTPValidationError,
)

_token_mgr = TokenManager()


class BTPClient:
    def __init__(self, config: BTPConfig = None):
        self.cfg = config or BTPConfig()
        self._session = requests.Session()
        self._session.headers.update({"Accept": "application/json", "Content-Type": "application/json"})

    def _xsuaa_token(self) -> str:
        return _token_mgr.get_token(
            self.cfg.xsuaa_token_url,
            self.cfg.xsuaa_client_id,
            self.cfg.xsuaa_client_secret,
        )

    def _request(self, method: str, url: str, token: str,
                 params: Dict = None, json: Any = None) -> Any:
        headers = {"Authorization": f"Bearer {token}"}
        resp = self._session.request(
            method, url, headers=headers, params=params, json=json,
            timeout=self.cfg.timeout,
        )
        return self._handle(resp)

    def _handle(self, resp: requests.Response) -> Any:
        if resp.status_code == 204:
            return None
        if resp.ok:
            return resp.json() if resp.content else None
        try:
            body = resp.json()
            if not isinstance(body, dict):
                body = {"raw": body}
        except Exception:
            body = {"raw": resp.text}
        msg = (body.get("message") or
               (body.get("error", {}) or {}).get("message") if isinstance(body.get("error"), dict) else None or
               resp.text)
        if resp.status_code in (401, 403):
            raise BTPAuthError(f"HTTP {resp.status_code}: {msg}", resp.status_code, body)
        if resp.status_code == 404:
            raise BTPNotFoundError(f"Not found: {msg}", resp.status_code, body)
        if resp.status_code == 409:
            raise BTPConflictError(f"Conflict: {msg}", resp.status_code, body)
        if resp.status_code == 400:
            raise BTPValidationError(f"Bad request: {msg}", resp.status_code, body)
        raise BTPError(f"HTTP {resp.status_code}: {msg}", resp.status_code, body)

    # ── XSUAA convenience methods ─────────────────────────────────────────────

    def xsuaa_get(self, path: str, params: Dict = None) -> Any:
        return self._request("GET", f"{self.cfg.xsuaa_url}{path}", self._xsuaa_token(), params=params)

    def xsuaa_post(self, path: str, body: Dict) -> Any:
        return self._request("POST", f"{self.cfg.xsuaa_url}{path}", self._xsuaa_token(), json=body)

    def xsuaa_put(self, path: str, body: Dict) -> Any:
        return self._request("PUT", f"{self.cfg.xsuaa_url}{path}", self._xsuaa_token(), json=body)

    def xsuaa_delete(self, path: str) -> Any:
        return self._request("DELETE", f"{self.cfg.xsuaa_url}{path}", self._xsuaa_token())
