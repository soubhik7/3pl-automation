"""OAuth2 token manager with automatic refresh and caching."""
import time
import requests
from dataclasses import dataclass, field
from typing import Dict


@dataclass
class _TokenCache:
    access_token: str = ""
    expires_at: float = 0.0


class TokenManager:
    """Fetches and caches OAuth2 client-credentials tokens per token URL."""

    _cache: Dict[str, _TokenCache] = field(default_factory=dict)

    def __init__(self):
        self._cache: Dict[str, _TokenCache] = {}

    def get_token(self, token_url: str, client_id: str, client_secret: str,
                  scope: str = None) -> str:
        cache_key = f"{token_url}:{client_id}"
        cached = self._cache.get(cache_key)
        if cached and time.time() < cached.expires_at - 60:
            return cached.access_token

        data = {"grant_type": "client_credentials"}
        if scope:
            data["scope"] = scope

        resp = requests.post(
            token_url,
            auth=(client_id, client_secret),
            data=data,
            timeout=30,
        )
        resp.raise_for_status()
        token_data = resp.json()

        self._cache[cache_key] = _TokenCache(
            access_token=token_data["access_token"],
            expires_at=time.time() + token_data.get("expires_in", 3600),
        )
        return self._cache[cache_key].access_token

    def invalidate(self, token_url: str, client_id: str):
        self._cache.pop(f"{token_url}:{client_id}", None)
