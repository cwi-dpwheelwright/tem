from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict

import jsonschema

from .secrets import resolve_secret

SCHEMA_PATH = Path(__file__).resolve().parent / "schemas" / "connector_auth.schema.json"
IDENTITY_SCHEMA_PATH = Path(__file__).resolve().parent / "schemas" / "connector_identity.schema.json"


@dataclass(frozen=True)
class ConnectorAuthConfig:
    raw: Dict[str, Any]


@dataclass(frozen=True)
class ConnectorIdentityConfig:
    raw: Dict[str, Any]


def validate_auth_config(payload: Dict[str, Any]) -> ConnectorAuthConfig:
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    validator = jsonschema.Draft202012Validator(schema, format_checker=jsonschema.FormatChecker())
    errors = sorted(validator.iter_errors(payload), key=lambda e: e.path)
    if errors:
        message = "; ".join(error.message for error in errors)
        raise ValueError(f"Connector auth config invalid: {message}")
    return ConnectorAuthConfig(raw=resolve_auth_secrets(payload))


def validate_identity_config(payload: Dict[str, Any]) -> ConnectorIdentityConfig:
    schema = json.loads(IDENTITY_SCHEMA_PATH.read_text(encoding="utf-8"))
    validator = jsonschema.Draft202012Validator(schema, format_checker=jsonschema.FormatChecker())
    errors = sorted(validator.iter_errors(payload), key=lambda e: e.path)
    if errors:
        message = "; ".join(error.message for error in errors)
        raise ValueError(f"Connector identity config invalid: {message}")
    return ConnectorIdentityConfig(raw=payload)


def resolve_auth_secrets(payload: Dict[str, Any]) -> Dict[str, Any]:
    auth_type = payload.get("type")
    resolved = dict(payload)
    if auth_type == "oauth2":
        oauth = dict(payload.get("oauth2") or {})
        oauth["client_id"] = resolve_secret(oauth.get("client_id"))
        oauth["client_secret"] = resolve_secret(oauth.get("client_secret"))
        resolved["oauth2"] = oauth
    elif auth_type == "api_key":
        api_key = dict(payload.get("api_key") or {})
        api_key["header_name"] = resolve_secret(api_key.get("header_name"))
        api_key["value"] = resolve_secret(api_key.get("value"))
        resolved["api_key"] = api_key
    elif auth_type == "basic":
        basic = dict(payload.get("basic") or {})
        basic["username"] = resolve_secret(basic.get("username"))
        basic["password"] = resolve_secret(basic.get("password"))
        resolved["basic"] = basic
    return resolved
