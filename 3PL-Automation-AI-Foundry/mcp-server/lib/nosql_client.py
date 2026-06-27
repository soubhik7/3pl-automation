"""
nosql_client.py — Cosmos NoSQL client wrapper, dedicated to these features' own containers.

Part of: 3pl-automation Solace/MuleSoft/BTP-publisher features (fully isolated from the
         main Integration Pulse agents/MCP servers)
Layer:   mcp-server lib

Purpose:  Lazily-initialised CosmosClient + get_container(platform) helper, scoped to one
          per-platform "{platform}_requests" container. Reuses the existing Cosmos NoSQL
          *account* (no separate account provisioned for 3 small tracking tables) but
          never touches any container the main app's cosmos-mcp-py tools use.
Depends:  COSMOS_ENDPOINT, COSMOS_NOSQL_KEY, COSMOS_DATABASE env vars (same account as
          the main app); COSMOS_NOSQL_CONTAINER_{SOLACE,MULESOFT,BTP}_REQUESTS (default
          "{platform}_requests" — these 2 new containers, mulesoft_requests/btp_requests,
          must be provisioned in the existing Cosmos account before the new HTTP routes
          will work, see ../../README.md).
Importance: Lazy init is critical — connecting at import time breaks the Functions
            worker indexing phase (see docs/troubleshooting.md §6 in the main repo).
"""
import os
from azure.cosmos import CosmosClient

_ENDPOINT = os.environ.get("COSMOS_ENDPOINT", "")
_KEY = os.environ.get("COSMOS_NOSQL_KEY", "")
_DATABASE = os.environ.get("COSMOS_DATABASE", "integration-pulse")

_CONTAINER_ENV_BY_PLATFORM = {
    "solace": ("COSMOS_NOSQL_CONTAINER_SOLACE_REQUESTS", "solace_requests"),
    "mulesoft": ("COSMOS_NOSQL_CONTAINER_MULESOFT_REQUESTS", "mulesoft_requests"),
    "btp": ("COSMOS_NOSQL_CONTAINER_BTP_REQUESTS", "btp_requests"),
}

_db = None


def _get_db():
    global _db
    if _db is None:
        _cosmos = CosmosClient(_ENDPOINT, credential=_KEY)
        _db = _cosmos.get_database_client(_DATABASE)
    return _db


def get_container(platform: str):
    env_name, default = _CONTAINER_ENV_BY_PLATFORM[platform]
    container_name = os.environ.get(env_name, default)
    return _get_db().get_container_client(container_name)
