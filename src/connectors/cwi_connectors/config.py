from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List

import yaml
import jwt

from .config_schema import validate_auth_config, validate_identity_config
from .secrets import resolve_secret
from .url_validation import validate_http_url


@dataclass(frozen=True)
class ConnectorDefinition:
    connector_id: str
    connector_type: str
    enabled: bool
    config: Dict[str, Any]
    auth: Dict[str, Any]
    source_priority: int = 100
    identity: "ConnectorIdentityBinding | None" = None


@dataclass(frozen=True)
class ConnectorIdentityBinding:
    owner_id: str
    delegate_ids: List[str]
    policy_id: str | None = None


@dataclass(frozen=True)
class StateStoreConfig:
    kind: str
    path: str | None = None
    postgres_dsn: str | None = None


@dataclass(frozen=True)
class RuntimeConfig:
    tenant_id: str
    workspace_id: str
    poll_interval_seconds: int
    state_store: StateStoreConfig
    connectors: List[ConnectorDefinition]
    vector_ingest_url: str | None = None
    vector_ingest_token: str | None = None
    orchestrator_url: str | None = None
    orchestrator_token: str | None = None
    orchestrator_jwks_url: str | None = None
    orchestrator_jwks_path: str | None = None
    orchestrator_jwt_issuer: str | None = None
    orchestrator_jwt_audience: str | None = None
    orchestrator_allow_insecure_jwt: bool = False
    reconciliation_url: str | None = None
    reconciliation_token: str | None = None
    redaction_tokens: List[str] | None = None
    chunk_size: int = 800
    chunk_overlap: int = 100
    retention_days: int | None = None
    admin_token: str | None = None
    admin_enabled: bool = False
    delivery_queue_path: str | None = None
    run_journal_path: str | None = None
    retention_state_path: str | None = None


def load_runtime_config(path: str) -> RuntimeConfig:
    raw = _load_yaml(path)
    config_path = Path(path).resolve()

    tenant_id = str(raw.get("tenant_id") or "").strip()
    workspace_id = str(raw.get("workspace_id") or "").strip()
    if not tenant_id or not workspace_id:
        raise ValueError("tenant_id and workspace_id are required")

    poll_interval = int(raw.get("poll_interval_seconds") or 300)

    store_raw = raw.get("state_store") or {}
    store_kind = str(store_raw.get("kind") or "file").strip()
    if store_kind not in {"file", "postgres"}:
        raise ValueError("state_store.kind must be 'file' or 'postgres'")
    state_store = StateStoreConfig(
        kind=store_kind,
        path=store_raw.get("path"),
        postgres_dsn=store_raw.get("postgres_dsn"),
    )

    connectors = []
    for entry in raw.get("connectors", []) or []:
        connector_id = str(entry.get("id") or entry.get("connector_id") or "").strip()
        connector_type = str(entry.get("type") or "").strip()
        if not connector_id or not connector_type:
            raise ValueError("connector entry requires id and type")
        enabled = bool(entry.get("enabled", True))
        config = entry.get("config") or {}
        auth = entry.get("auth") or {}
        source_priority = int(entry.get("source_priority") or entry.get("priority") or 100)
        auth = validate_auth_config(auth).raw
        identity = _parse_identity_binding(entry.get("identity"))
        connectors.append(
            ConnectorDefinition(
                connector_id=connector_id,
                connector_type=connector_type,
                enabled=enabled,
                config=config,
                auth=auth,
                source_priority=source_priority,
                identity=identity,
            )
        )

    admin_token = resolve_secret(raw.get("admin_token"))
    admin_enabled = bool(raw.get("admin_enabled", admin_token is not None))
    reconciliation_url = raw.get("reconciliation_url") or raw.get("orchestrator_url")
    reconciliation_token = resolve_secret(raw.get("reconciliation_token") or raw.get("orchestrator_token"))
    allow_insecure_jwt = _bool_value(raw.get("orchestrator_allow_insecure_jwt", False))

    delivery_queue_path = _resolve_artifact_path(
        raw.get("delivery_queue_path"),
        config_path,
        state_store.path,
        "connectors-queue.json",
    )
    run_journal_path = _resolve_artifact_path(
        raw.get("run_journal_path"),
        config_path,
        state_store.path,
        "connectors-runs.json",
    )
    retention_state_path = _resolve_artifact_path(
        raw.get("retention_state_path"),
        config_path,
        state_store.path,
        "connectors-retention.json",
    )

    jwks_path = raw.get("orchestrator_jwks_path")
    if jwks_path:
        candidate = Path(jwks_path)
        if not candidate.is_absolute():
            candidate = (config_path.parent / candidate).resolve()
        jwks_path = str(candidate)

    runtime = RuntimeConfig(
        tenant_id=tenant_id,
        workspace_id=workspace_id,
        poll_interval_seconds=poll_interval,
        state_store=state_store,
        connectors=connectors,
        vector_ingest_url=raw.get("vector_ingest_url"),
        vector_ingest_token=resolve_secret(raw.get("vector_ingest_token")),
        orchestrator_url=raw.get("orchestrator_url"),
        orchestrator_token=resolve_secret(raw.get("orchestrator_token")),
        orchestrator_jwks_url=raw.get("orchestrator_jwks_url"),
        orchestrator_jwks_path=jwks_path,
        orchestrator_jwt_issuer=raw.get("orchestrator_jwt_issuer"),
        orchestrator_jwt_audience=raw.get("orchestrator_jwt_audience"),
        orchestrator_allow_insecure_jwt=allow_insecure_jwt,
        reconciliation_url=reconciliation_url,
        reconciliation_token=reconciliation_token,
        redaction_tokens=list(raw.get("redaction_tokens", []) or []),
        chunk_size=int(raw.get("chunk_size", 800)),
        chunk_overlap=int(raw.get("chunk_overlap", 100)),
        retention_days=raw.get("retention_days"),
        admin_token=admin_token,
        admin_enabled=admin_enabled,
        delivery_queue_path=delivery_queue_path,
        run_journal_path=run_journal_path,
        retention_state_path=retention_state_path,
    )
    _validate_runtime(runtime)
    return runtime


def _load_yaml(path: str) -> Dict[str, Any]:
    payload = Path(path).read_text(encoding="utf-8")
    data = yaml.safe_load(payload) or {}
    if not isinstance(data, dict):
        raise ValueError("config file must be a mapping")
    return data


def _validate_runtime(config: RuntimeConfig) -> None:
    if not config.connectors:
        raise ValueError("At least one connector is required")
    if config.poll_interval_seconds <= 0:
        raise ValueError("poll_interval_seconds must be positive")
    if config.chunk_size <= 0:
        raise ValueError("chunk_size must be positive")
    if config.chunk_overlap < 0:
        raise ValueError("chunk_overlap must be non-negative")
    if config.chunk_overlap >= config.chunk_size:
        raise ValueError("chunk_overlap must be smaller than chunk_size")
    if config.retention_days is not None and config.retention_days <= 0:
        raise ValueError("retention_days must be positive when set")
    if config.vector_ingest_url and not config.vector_ingest_token:
        raise ValueError("vector_ingest_token is required when vector_ingest_url is set")
    if config.orchestrator_url and not config.orchestrator_token:
        raise ValueError("orchestrator_token is required when orchestrator_url is set")
    if config.orchestrator_jwks_path and not Path(config.orchestrator_jwks_path).exists():
        raise ValueError("orchestrator_jwks_path does not exist")
    if config.orchestrator_token and not (config.orchestrator_jwks_url or config.orchestrator_jwks_path):
        if not config.orchestrator_allow_insecure_jwt:
            raise ValueError("orchestrator_jwks_url/path required unless allow_insecure_jwt is true")
    if config.reconciliation_url and not config.reconciliation_token:
        raise ValueError("reconciliation_token is required when reconciliation_url is set")
    if config.vector_ingest_url:
        validate_http_url(config.vector_ingest_url, "vector_ingest_url")
    if config.orchestrator_url:
        validate_http_url(config.orchestrator_url, "orchestrator_url")
    if config.reconciliation_url:
        validate_http_url(config.reconciliation_url, "reconciliation_url")
    _validate_identity_bindings(config)


def _parse_identity_binding(raw: Dict[str, Any] | None) -> ConnectorIdentityBinding | None:
    if not raw:
        return None
    validated = validate_identity_config(raw).raw
    owner_id = str(validated.get("owner_id") or "").strip()
    delegate_ids = [str(value).strip() for value in (validated.get("delegate_ids") or []) if str(value).strip()]
    policy_id = validated.get("policy_id")
    return ConnectorIdentityBinding(owner_id=owner_id, delegate_ids=delegate_ids, policy_id=policy_id)


def _validate_identity_bindings(config: RuntimeConfig) -> None:
    bindings = [connector for connector in config.connectors if connector.identity]
    if not bindings:
        return
    if not config.orchestrator_token:
        raise ValueError("orchestrator_token is required when connector identity bindings are set")
    subjects = _token_subjects(
        config.orchestrator_token,
        jwks_url=config.orchestrator_jwks_url,
        jwks_path=config.orchestrator_jwks_path,
        issuer=config.orchestrator_jwt_issuer,
        audience=config.orchestrator_jwt_audience,
        allow_insecure=config.orchestrator_allow_insecure_jwt,
    )
    if not subjects:
        raise ValueError("orchestrator_token must be a JWT with subject claims when identity bindings are set")
    for connector in bindings:
        identity = connector.identity
        if not identity:
            continue
        allowed = {identity.owner_id, *identity.delegate_ids}
        if subjects.isdisjoint(allowed):
            raise ValueError(
                "orchestrator_token subject is not authorized for connector "
                f"{connector.connector_id}"
            )


def _token_subjects(
    token: str,
    jwks_url: str | None = None,
    jwks_path: str | None = None,
    issuer: str | None = None,
    audience: str | None = None,
    allow_insecure: bool = False,
) -> set[str]:
    if not token or token.count(".") < 2:
        return set()
    jwks_source = jwks_url
    if not jwks_source and jwks_path:
        jwks_source = Path(jwks_path).resolve().as_uri()
    try:
        if jwks_source:
            jwks_client = jwt.PyJWKClient(jwks_source)
            signing_key = jwks_client.get_signing_key_from_jwt(token)
            data = jwt.decode(
                token,
                signing_key.key,
                algorithms=[signing_key.algorithm_name],
                audience=audience,
                issuer=issuer,
                options={"verify_aud": bool(audience), "verify_iss": bool(issuer)},
            )
        else:
            if not allow_insecure:
                raise ValueError("orchestrator_jwks_url/path required for JWT validation")
            data = jwt.decode(
                token,
                options={"verify_signature": False, "verify_aud": False, "verify_iss": False},
            )  # nosemgrep: python.jwt.security.unverified-jwt-decode
    except jwt.PyJWTError as exc:
        raise ValueError(f"orchestrator_token validation failed: {exc}") from exc
    except ValueError:
        return set()
    subjects = set()
    for key in ("sub", "user_id", "identity_id", "uid"):
        value = data.get(key)
        if value:
            subjects.add(str(value))
    return subjects


def _resolve_artifact_path(
    raw_value: str | None,
    config_path: Path,
    state_store_path: str | None,
    default_name: str,
) -> str:
    if raw_value:
        return str(raw_value)
    if state_store_path:
        return str(Path(state_store_path).resolve().parent / default_name)
    return str(config_path.parent / default_name)


def _bool_value(value: Any, default: bool = False) -> bool:
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "on"}
    return bool(value)
