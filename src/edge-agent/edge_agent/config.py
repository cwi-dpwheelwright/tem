from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Tuple, Iterable, overload
from urllib.parse import urlparse

import jsonschema
import yaml

def _schema_resource() -> Path:
    env_path = os.getenv("CWI_EDGE_AGENT_SCHEMA_PATH")
    candidates = []
    if env_path:
        candidates.append(Path(env_path))

    here = Path(__file__).resolve()
    candidates.extend(
        [
            here.parents[1] / "configs" / "edge-agent.schema.json",
            Path("/app/configs/edge-agent.schema.json"),
            here.parents[2] / "configs" / "edge-agent.schema.json",
        ]
    )

    for candidate in candidates:
        if candidate.exists():
            return candidate

    checked = ", ".join(str(candidate) for candidate in candidates)
    raise FileNotFoundError(f"edge-agent schema not found. Checked: {checked}")


@dataclass(frozen=True)
class IdentityAuthConfig:
    login_url: str
    refresh_url: str
    tenant_id: str
    username: str
    password: str
    refresh_margin_seconds: int


@dataclass(frozen=True)
class EdgeAgentConfig:
    gateway_url: str
    tenant_id: str
    poll_interval_seconds: float
    buffer_path: str
    buffer_max_items: int | None
    buffer_max_bytes: int | None
    buffer_drop_policy: str
    ingest_timeout_seconds: float
    identity_auth: IdentityAuthConfig
    mtls_cert_path: str | None
    mtls_key_path: str | None
    mtls_revoked_serials_path: str | None
    mtls_ca_bundle_path: str | None
    mtls_crl_url: str | None
    mtls_crl_cache_path: str | None
    mtls_crl_refresh_seconds: int | None
    mtls_pinned_fingerprints: Tuple[str, ...]
    mtls_verify_hostname: bool
    mtls_reload_interval_seconds: int | None
    mtls_reload_on_sighup: bool
    survival_enabled: bool
    survival_signal_url: str | None
    survival_emit_interval_seconds: int
    survival_min_severity: float
    survival_min_change: float
    survival_signal_type: str
    degradation_buffer_warning_ratio: float
    degradation_buffer_critical_ratio: float
    degradation_disk_warning_free_ratio: float
    degradation_disk_critical_free_ratio: float
    degradation_network_failure_threshold: int
    degradation_network_open_severity: float
    degradation_network_half_open_severity: float
    otel_enabled: bool
    otel_service_name: str
    otel_otlp_endpoint: str | None
    otel_headers: Dict[str, str]
    otel_metrics_interval_seconds: int
    otel_traces_enabled: bool
    otel_metrics_enabled: bool
    metrics_enabled: bool
    metrics_node_id: str | None
    metrics_endpoints: Tuple[str, ...]
    metrics_collector: str
    metrics_queue_path: str
    metrics_queue_max_items: int | None
    metrics_queue_max_bytes: int | None
    metrics_queue_drop_policy: str
    metrics_flush_interval_seconds: int
    metrics_max_batch: int
    metrics_timeout_seconds: float
    metrics_backoff_base_seconds: float
    metrics_backoff_max_seconds: float
    metrics_failure_threshold: int
    metrics_recovery_seconds: float


def load_config(path: str | Path) -> Tuple[EdgeAgentConfig, str]:
    config_path = Path(path)
    raw = _read_config(config_path)
    _validate_schema(raw)

    identity_auth, auth_source = _resolve_auth(raw, config_path)
    _validate_gateway_url(raw["gateway_url"])

    mtls_cert, mtls_key, revoked_serials, ca_bundle, crl_url, crl_cache, crl_refresh, pins, verify_hostname, reload_interval, reload_on_sighup = _resolve_mtls(
        raw, config_path
    )
    survival = raw.get("survival", {}) if isinstance(raw.get("survival"), dict) else {}
    degradation = raw.get("degradation", {}) if isinstance(raw.get("degradation"), dict) else {}
    otel = raw.get("otel", {}) if isinstance(raw.get("otel"), dict) else {}
    metrics = raw.get("metrics", {}) if isinstance(raw.get("metrics"), dict) else {}
    metrics_enabled = _resolve_optional_bool(metrics.get("enabled"), False)
    metrics_node_id = _resolve_optional_str(metrics.get("node_id"))
    metrics_endpoints = _resolve_metrics_endpoints(metrics, raw["gateway_url"])
    metrics_queue_path = _resolve_optional_str(
        metrics.get("queue_path"),
        default=str(Path(raw["buffer_path"]).with_suffix(".metrics.jsonl")),
    )
    metrics_queue_max_items = _resolve_optional_int(metrics.get("queue_max_items"))
    metrics_queue_max_bytes = _resolve_optional_int(metrics.get("queue_max_bytes"))
    metrics_queue_drop_policy = _resolve_buffer_drop_policy(metrics.get("queue_drop_policy"))
    metrics_flush_interval_seconds = _resolve_optional_int(
        metrics.get("flush_interval_seconds"), default=int(raw["poll_interval_seconds"])
    ) or int(raw["poll_interval_seconds"])
    metrics_max_batch = _resolve_optional_int(metrics.get("max_batch"), default=25) or 25
    metrics_timeout_seconds = _resolve_optional_float(metrics.get("timeout_seconds"), 10.0)
    metrics_backoff_base_seconds = _resolve_optional_float(
        metrics.get("backoff_base_seconds"), 1.0
    )
    metrics_backoff_max_seconds = _resolve_optional_float(metrics.get("backoff_max_seconds"), 30.0)
    metrics_failure_threshold = _resolve_optional_int(metrics.get("failure_threshold"), default=3) or 3
    metrics_recovery_seconds = _resolve_optional_float(metrics.get("recovery_seconds"), 30.0)
    metrics_collector = _resolve_optional_str(metrics.get("collector"), default="simulated")

    if metrics_enabled and not metrics_node_id:
        raise ValueError("metrics.node_id is required when metrics.enabled is true.")

    config = EdgeAgentConfig(
        gateway_url=raw["gateway_url"],
        tenant_id=raw["tenant_id"],
        poll_interval_seconds=float(raw["poll_interval_seconds"]),
        buffer_path=raw["buffer_path"],
        buffer_max_items=_resolve_optional_int(raw.get("buffer_max_items")),
        buffer_max_bytes=_resolve_optional_int(raw.get("buffer_max_bytes")),
        buffer_drop_policy=_resolve_buffer_drop_policy(raw.get("buffer_drop_policy")),
        ingest_timeout_seconds=_resolve_optional_float(raw.get("ingest_timeout_seconds"), 10.0),
        identity_auth=identity_auth,
        mtls_cert_path=mtls_cert,
        mtls_key_path=mtls_key,
        mtls_revoked_serials_path=revoked_serials,
        mtls_ca_bundle_path=ca_bundle,
        mtls_crl_url=crl_url,
        mtls_crl_cache_path=crl_cache,
        mtls_crl_refresh_seconds=crl_refresh,
        mtls_pinned_fingerprints=pins,
        mtls_verify_hostname=verify_hostname,
        mtls_reload_interval_seconds=reload_interval,
        mtls_reload_on_sighup=reload_on_sighup,
        survival_enabled=_resolve_optional_bool(survival.get("enabled"), True),
        survival_signal_url=_resolve_optional_str(survival.get("endpoint")),
        survival_emit_interval_seconds=_resolve_optional_int(
            survival.get("emit_interval_seconds"), default=60
        ) or 60,
        survival_min_severity=_resolve_optional_float(survival.get("min_severity"), 0.1),
        survival_min_change=_resolve_optional_float(survival.get("min_change"), 0.05),
        survival_signal_type=_resolve_optional_str(
            survival.get("signal_type"), default="edge_agent_degradation"
        ),
        degradation_buffer_warning_ratio=_resolve_optional_float(
            degradation.get("buffer_warning_ratio"), 0.7
        ),
        degradation_buffer_critical_ratio=_resolve_optional_float(
            degradation.get("buffer_critical_ratio"), 0.9
        ),
        degradation_disk_warning_free_ratio=_resolve_optional_float(
            degradation.get("disk_warning_free_ratio"), 0.1
        ),
        degradation_disk_critical_free_ratio=_resolve_optional_float(
            degradation.get("disk_critical_free_ratio"), 0.05
        ),
        degradation_network_failure_threshold=_resolve_optional_int(
            degradation.get("network_failure_threshold"), default=3
        ) or 3,
        degradation_network_open_severity=_resolve_optional_float(
            degradation.get("network_open_severity"), 0.95
        ),
        degradation_network_half_open_severity=_resolve_optional_float(
            degradation.get("network_half_open_severity"), 0.7
        ),
        otel_enabled=_resolve_optional_bool(otel.get("enabled"), False),
        otel_service_name=_resolve_optional_str(otel.get("service_name"), default="cwi-edge-agent"),
        otel_otlp_endpoint=_resolve_optional_str(otel.get("otlp_endpoint")),
        otel_headers=_resolve_headers(otel.get("headers")),
        otel_metrics_interval_seconds=_resolve_optional_int(
            otel.get("metrics_interval_seconds"), default=15
        ) or 15,
        otel_traces_enabled=_resolve_optional_bool(otel.get("traces_enabled"), True),
        otel_metrics_enabled=_resolve_optional_bool(otel.get("metrics_enabled"), True),
        metrics_enabled=metrics_enabled,
        metrics_node_id=metrics_node_id,
        metrics_endpoints=metrics_endpoints,
        metrics_collector=metrics_collector,
        metrics_queue_path=metrics_queue_path,
        metrics_queue_max_items=metrics_queue_max_items,
        metrics_queue_max_bytes=metrics_queue_max_bytes,
        metrics_queue_drop_policy=metrics_queue_drop_policy,
        metrics_flush_interval_seconds=metrics_flush_interval_seconds,
        metrics_max_batch=metrics_max_batch,
        metrics_timeout_seconds=metrics_timeout_seconds,
        metrics_backoff_base_seconds=metrics_backoff_base_seconds,
        metrics_backoff_max_seconds=metrics_backoff_max_seconds,
        metrics_failure_threshold=metrics_failure_threshold,
        metrics_recovery_seconds=metrics_recovery_seconds,
    )
    return config, auth_source


def _read_config(path: Path) -> Dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(f"Config not found: {path}")
    data = path.read_text(encoding="utf-8")
    if path.suffix.lower() in {".yaml", ".yml"}:
        return yaml.safe_load(data) or {}
    return json.loads(data)


def _validate_schema(raw: Dict[str, Any]) -> None:
    schema = json.loads(_schema_resource().read_text(encoding="utf-8"))
    validator = jsonschema.Draft202012Validator(schema, format_checker=jsonschema.FormatChecker())
    errors = sorted(validator.iter_errors(raw), key=lambda e: e.path)
    if errors:
        messages = "; ".join(error.message for error in errors)
        raise ValueError(f"Config schema validation failed: {messages}")


def _resolve_auth(
    raw: Dict[str, Any],
    config_path: Path,
) -> Tuple[IdentityAuthConfig, str]:
    identity_auth = _resolve_identity_auth(raw, config_path)
    if identity_auth is not None:
        return identity_auth, "identity"

    raise ValueError(
        "Authentication is required via edge-agent identity credentials."
    )


def _resolve_identity_auth(raw: Dict[str, Any], config_path: Path) -> IdentityAuthConfig | None:
    identity = raw.get("identity")
    if not isinstance(identity, dict):
        identity = {}

    username = _resolve_identity_secret(
        identity=identity,
        value_key="username",
        env_name_key="username_env",
        default_env_name="CWI_EDGE_AGENT_IDENTITY_USERNAME",
    )
    password = _resolve_identity_secret(
        identity=identity,
        value_key="password",
        env_name_key="password_env",
        default_env_name="CWI_EDGE_AGENT_IDENTITY_PASSWORD",
        path_key="password_path",
        default_path_env_name="CWI_EDGE_AGENT_IDENTITY_PASSWORD_PATH",
        config_path=config_path,
    )

    if not username and not password:
        return None
    if not username or not password:
        raise ValueError("edge-agent identity auth requires both username and password.")

    login_url = os.getenv("CWI_EDGE_AGENT_IDENTITY_LOGIN_URL") or _resolve_optional_str(
        identity.get("login_url")
    )
    refresh_url = os.getenv("CWI_EDGE_AGENT_IDENTITY_REFRESH_URL") or _resolve_optional_str(
        identity.get("refresh_url")
    )
    base_url = os.getenv("CWI_EDGE_AGENT_IDENTITY_URL") or os.getenv("IDENTITY_URL") or _resolve_optional_str(
        identity.get("base_url")
    )

    if not login_url and base_url:
        login_url = base_url.rstrip("/") + "/auth/login"
    if not refresh_url and base_url:
        refresh_url = base_url.rstrip("/") + "/auth/refresh"

    if not login_url or not refresh_url:
        raise ValueError("edge-agent identity auth requires login_url/refresh_url or identity base URL.")

    _validate_http_url("edge-agent identity login_url", login_url)
    _validate_http_url("edge-agent identity refresh_url", refresh_url)

    tenant_id = (
        os.getenv("CWI_EDGE_AGENT_IDENTITY_TENANT_ID")
        or _resolve_optional_str(identity.get("tenant_id"))
        or raw["tenant_id"]
    )
    refresh_margin_seconds = _resolve_optional_int(
        os.getenv("CWI_EDGE_AGENT_IDENTITY_REFRESH_MARGIN_SECONDS")
        or identity.get("refresh_margin_seconds"),
        default=60,
    ) or 60

    return IdentityAuthConfig(
        login_url=login_url,
        refresh_url=refresh_url,
        tenant_id=tenant_id,
        username=username,
        password=password,
        refresh_margin_seconds=refresh_margin_seconds,
    )


def _validate_gateway_url(value: str) -> None:
    _validate_http_url("gateway_url", value)


def _validate_http_url(name: str, value: str) -> None:
    parsed = urlparse(value)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise ValueError(f"{name} must be a valid http(s) URL.")


def _resolve_mtls(
    raw: Dict[str, Any],
    config_path: Path,
) -> Tuple[
    str | None,
    str | None,
    str | None,
    str | None,
    str | None,
    str | None,
    int | None,
    Tuple[str, ...],
    bool,
    int | None,
    bool,
]:
    mtls = raw.get("mtls")
    if not isinstance(mtls, dict):
        return None, None, None, None, None, None, None, tuple(), True, None, False
    cert = mtls.get("cert_path")
    key = mtls.get("key_path")
    cert_path = None
    key_path = None
    if cert and key:
        cert_path = (config_path.parent / cert).resolve()
        key_path = (config_path.parent / key).resolve()
        if not cert_path.exists():
            raise FileNotFoundError(f"mTLS cert file not found: {cert_path}")
        if not key_path.exists():
            raise FileNotFoundError(f"mTLS key file not found: {key_path}")
    revoked = mtls.get("revoked_serials_path")
    revoked_path = None
    if isinstance(revoked, str) and revoked.strip():
        revoked_candidate = (config_path.parent / revoked).resolve()
        if not revoked_candidate.exists():
            raise FileNotFoundError(f"mTLS revoked serials file not found: {revoked_candidate}")
        revoked_path = str(revoked_candidate)
    ca_bundle = mtls.get("ca_bundle_path")
    ca_bundle_path = None
    if isinstance(ca_bundle, str) and ca_bundle.strip():
        ca_bundle_candidate = (config_path.parent / ca_bundle).resolve()
        if not ca_bundle_candidate.exists():
            raise FileNotFoundError(f"mTLS CA bundle file not found: {ca_bundle_candidate}")
        ca_bundle_path = str(ca_bundle_candidate)
    crl_url = mtls.get("crl_url")
    crl_cache = mtls.get("crl_cache_path")
    crl_cache_path = None
    if isinstance(crl_cache, str) and crl_cache.strip():
        crl_cache_path = str((config_path.parent / crl_cache).resolve())
    crl_refresh = _resolve_optional_int(mtls.get("crl_refresh_seconds"))
    pins = _resolve_fingerprint_list(mtls.get("pinned_fingerprints"))
    verify_hostname = _resolve_optional_bool(mtls.get("verify_hostname"), True)
    reload_interval = _resolve_optional_int(mtls.get("reload_interval_seconds"))
    reload_on_sighup = _resolve_optional_bool(mtls.get("reload_on_sighup"), False)
    return (
        str(cert_path) if cert_path else None,
        str(key_path) if key_path else None,
        revoked_path,
        ca_bundle_path,
        crl_url,
        crl_cache_path,
        crl_refresh,
        pins,
        verify_hostname,
        reload_interval,
        reload_on_sighup,
    )


def _resolve_optional_int(value: Any, default: int | None = None) -> int | None:
    if value is None:
        return default
    try:
        parsed = int(value)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"Expected integer value, got {value}") from exc
    if parsed < 0:
        raise ValueError("Expected non-negative integer value.")
    return parsed


def _resolve_optional_float(value: Any, default: float | None = None) -> float:
    if value is None:
        if default is None:
            raise ValueError("Expected float value, got null.")
        return default
    try:
        parsed = float(value)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"Expected float value, got {value}") from exc
    return parsed


def _resolve_optional_bool(value: Any, default: bool) -> bool:
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        lowered = value.strip().lower()
        if lowered in {"true", "1", "yes", "y"}:
            return True
        if lowered in {"false", "0", "no", "n"}:
            return False
    raise ValueError(f"Expected boolean value, got {value}")


@overload
def _resolve_optional_str(value: Any, default: None = None) -> str | None: ...


@overload
def _resolve_optional_str(value: Any, default: str) -> str: ...


def _resolve_optional_str(value: Any, default: str | None = None) -> str | None:
    if value is None:
        return default
    if isinstance(value, str):
        return value.strip()
    raise ValueError(f"Expected string value, got {value}")


def _resolve_headers(value: Any) -> Dict[str, str]:
    if value is None:
        return {}
    if isinstance(value, dict):
        return {str(k): str(v) for k, v in value.items()}
    if isinstance(value, str):
        headers: Dict[str, str] = {}
        for part in value.split(","):
            if not part.strip():
                continue
            if "=" not in part:
                raise ValueError(f"Invalid header format: {part}")
            key, val = part.split("=", 1)
            headers[key.strip()] = val.strip()
        return headers
    raise ValueError(f"Expected headers map or string, got {value}")


def _resolve_fingerprint_list(value: Any) -> Tuple[str, ...]:
    if value is None:
        return tuple()
    if isinstance(value, str):
        items = [value]
    elif isinstance(value, Iterable):
        items = list(value)
    else:
        raise ValueError(f"Expected list of fingerprints, got {value}")
    cleaned: list[str] = []
    for item in items:
        if not item:
            continue
        cleaned.append(str(item).strip())
    return tuple(cleaned)


def _resolve_buffer_drop_policy(value: Any) -> str:
    if value is None:
        return "drop_oldest"
    if isinstance(value, str) and value in {"drop_oldest", "drop_newest"}:
        return value
    raise ValueError("buffer_drop_policy must be drop_oldest or drop_newest.")


def _resolve_metrics_endpoints(metrics: Dict[str, Any], gateway_url: str) -> Tuple[str, ...]:
    endpoints = metrics.get("endpoints")
    if isinstance(endpoints, list):
        cleaned = tuple(str(item).strip() for item in endpoints if str(item).strip())
        if cleaned:
            return cleaned
    endpoint = metrics.get("endpoint")
    if isinstance(endpoint, str) and endpoint.strip():
        return (endpoint.strip(),)
    return (gateway_url.rstrip("/"),)


def _resolve_identity_secret(
    *,
    identity: Dict[str, Any],
    value_key: str,
    env_name_key: str,
    default_env_name: str,
    path_key: str | None = None,
    default_path_env_name: str | None = None,
    config_path: Path | None = None,
) -> str | None:
    configured_env_name = _resolve_optional_str(identity.get(env_name_key))
    if configured_env_name:
        configured_env_value = os.getenv(configured_env_name)
        if configured_env_value:
            return configured_env_value.strip()

    env_value = os.getenv(default_env_name)
    if env_value:
        return env_value.strip()

    configured_value = _resolve_optional_str(identity.get(value_key))
    if configured_value:
        return configured_value

    if path_key:
        configured_path_env = None
        if default_path_env_name:
            configured_path_env = os.getenv(default_path_env_name)
        configured_path = configured_path_env or _resolve_optional_str(identity.get(path_key))
        if configured_path:
            if config_path is None:
                raise ValueError("config_path is required to resolve identity secret paths.")
            candidate = (config_path.parent / configured_path).resolve()
            if not candidate.exists():
                raise FileNotFoundError(f"Identity secret file not found: {candidate}")
            return candidate.read_text(encoding="utf-8").strip()

    return None
