"""Cloud Foundry CLI wrapper — subprocess calls to the `cf` binary."""
import json
import subprocess
from typing import Any, Dict, List, Optional

from .exceptions import BTPError, BTPAuthError

_CF_BIN = "cf"


def _run(args: List[str], input_text: str = None) -> str:
    cmd = [_CF_BIN] + args
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, input=input_text)
    except FileNotFoundError:
        raise BTPError(
            "CF CLI not found. Install from: https://github.com/cloudfoundry/cli/releases\n"
            "Then login: cf login -a https://api.cf.us10-001.hana.ondemand.com --sso"
        )
    if result.returncode != 0:
        err = (result.stderr or result.stdout).strip()
        if any(k in err.lower() for k in ("not logged in", "not authenticated", "no api endpoint")):
            raise BTPAuthError(
                "Not logged in to CF.\n"
                "Run: cf login -a https://api.cf.us10-001.hana.ondemand.com --sso"
            )
        raise BTPError(f"CF error: {err}")
    return result.stdout.strip()


# ── Session ───────────────────────────────────────────────────────────────────

def target() -> str:
    return _run(["target"])


def set_target(org: str = None, space: str = None) -> str:
    args = ["target"]
    if org:
        args += ["-o", org]
    if space:
        args += ["-s", space]
    return _run(args)


# ── Spaces ────────────────────────────────────────────────────────────────────

def list_spaces() -> List[str]:
    output = _run(["spaces"])
    lines = output.splitlines()
    # Skip header ("Getting spaces…") and the "name" column header
    return [l.strip() for l in lines if l.strip() and not l.startswith("Getting") and l.strip() != "name"]


def create_space(name: str) -> str:
    return _run(["create-space", name])


def delete_space(name: str) -> str:
    return _run(["delete-space", name, "-f"])


# ── Apps ──────────────────────────────────────────────────────────────────────

def list_apps() -> str:
    return _run(["apps"])


def push_app(name: str, path: str = ".", no_start: bool = False) -> str:
    args = ["push", name, "-p", path]
    if no_start:
        args.append("--no-start")
    return _run(args)


def delete_app(name: str) -> str:
    return _run(["delete", name, "-f", "-r"])


def start_app(name: str) -> str:
    return _run(["start", name])


def stop_app(name: str) -> str:
    return _run(["stop", name])


def restage_app(name: str) -> str:
    return _run(["restage", name])


def recent_logs(app_name: str) -> str:
    return _run(["logs", app_name, "--recent"])


# ── App environment ───────────────────────────────────────────────────────────

def get_env(app_name: str) -> str:
    return _run(["env", app_name])


def set_env(app_name: str, key: str, value: str) -> str:
    return _run(["set-env", app_name, key, value])


def unset_env(app_name: str, key: str) -> str:
    return _run(["unset-env", app_name, key])


# ── CF Service instances ──────────────────────────────────────────────────────

def list_services() -> str:
    return _run(["services"])


def create_service(service: str, plan: str, name: str, params: str = None) -> str:
    args = ["create-service", service, plan, name]
    if params:
        args += ["-c", params]
    return _run(args)


def delete_service(name: str) -> str:
    return _run(["delete-service", name, "-f"])


# ── Service bindings (CF app ↔ service) ───────────────────────────────────────

def bind_service(app_name: str, service_name: str, params: str = None) -> str:
    args = ["bind-service", app_name, service_name]
    if params:
        args += ["-c", params]
    return _run(args)


def unbind_service(app_name: str, service_name: str) -> str:
    return _run(["unbind-service", app_name, service_name])


# ── Service keys ──────────────────────────────────────────────────────────────

def create_service_key(service_name: str, key_name: str, params: str = None) -> str:
    args = ["create-service-key", service_name, key_name]
    if params:
        args += ["-c", params]
    return _run(args)


def get_service_key(service_name: str, key_name: str) -> str:
    return _run(["service-key", service_name, key_name])


def delete_service_key(service_name: str, key_name: str) -> str:
    return _run(["delete-service-key", service_name, key_name, "-f"])


# ── Routes ────────────────────────────────────────────────────────────────────

def map_route(app_name: str, domain: str, hostname: str = None) -> str:
    args = ["map-route", app_name, domain]
    if hostname:
        args += ["--hostname", hostname]
    return _run(args)
