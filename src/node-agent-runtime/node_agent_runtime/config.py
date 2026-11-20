"""Configuration loading for the node agent runtime."""

from __future__ import annotations

from dataclasses import dataclass, field
import ipaddress
from pathlib import Path
from typing import Any, Mapping
import os
import string

import yaml

from .node_id import DEFAULT_MACHINE_ID_PATH, derive_node_id_from_machine_id


@dataclass(frozen=True)
class IdentityConfig:
    node_id: str
    private_key_path: Path | None
    public_key_path: Path | None
    key_type: str = "ed25519"
    private_key_pem_env: str | None = None
    public_key_pem_env: str | None = None
    persist_keys: bool = True


@dataclass(frozen=True)
class StorageConfig:
    state_dir: Path | None = None


@dataclass(frozen=True)
class DegradationConfig:
    cpu_warning_load: float = 1.0
    cpu_critical_load: float = 2.0
    cpu_warning_percent: float = 70.0
    cpu_critical_percent: float = 90.0
    disk_warning_free_ratio: float = 0.1
    disk_critical_free_ratio: float = 0.05
    memory_warning_free_ratio: float = 0.2
    memory_critical_free_ratio: float = 0.1
    transport_url: str | None = None
    transport_timeout_seconds: int = 5


@dataclass(frozen=True)
class ReadinessConfig:
    max_degradation_severity: float = 0.9
    dependency_urls: tuple[str, ...] = ()
    dependency_timeout_seconds: int = 2


@dataclass(frozen=True)
class ActivationConfig:
    require_signature: bool = True
    expected_issuer: str | None = None
    expected_audience: str | None = None
    public_key_path: Path | None = None
    public_key_pem: str | None = None
    handshake_path: Path | None = None
    nonce_cache_max_entries: int = 10000
    status_url: str | None = None
    status_timeout_seconds: int = 5


@dataclass(frozen=True)
class KillSwitchConfig:
    admin_url: str | None = None
    tenant_id: str | None = None
    break_glass_token_env: str | None = None
    state_path: Path | None = None
    retry_attempts: int = 3
    retry_backoff_seconds: int = 1


@dataclass(frozen=True)
class TamperConfig:
    indicator_files_exist: tuple[Path, ...] = ()
    indicator_files_sha256: dict[Path, str] = field(default_factory=dict)
    indicator_env_vars: tuple[str, ...] = ()
    policy_action: str = "ignore"
    policy_minimum_severity: float = 1.0
    policy_ticket_path: Path | None = None


@dataclass(frozen=True)
class BundleConfig:
    sealed_bundle_path: Path | None = None
    unseal_dir: Path | None = None
    crypto_provider: str = "noop"
    crypto_key_path: Path | None = None
    crypto_key_env: str | None = None


@dataclass(frozen=True)
class CacheConfig:
    cache_dir: Path | None = None
    max_bytes: int = 100 * 1024 * 1024
    max_items: int = 1000
    require_sha256: bool = True
    require_signature: bool = False
    signature_public_key_path: Path | None = None
    signature_public_key_pem_env: str | None = None


@dataclass(frozen=True)
class BaselinePacksConfig:
    roots: tuple[Path, ...] = ()


@dataclass(frozen=True)
class LocalModelConfig:
    enabled: bool = False
    provider: str = "ollama"
    base_url: str = "http://127.0.0.1:11434"
    base_url_env: str | None = None
    model: str = ""
    model_env: str | None = None
    model_family: str = "7b/8b-instruct"
    quantization: str = "Q4"
    context_tokens: int = 4096
    max_vram_gb: float = 6.0
    timeout_seconds: int = 45


@dataclass(frozen=True)
class ResilienceConfig:
    queue_dir: Path | None = None
    max_entries: int = 1000
    max_bytes: int = 10 * 1024 * 1024
    max_attempts: int = 3
    backoff_seconds: int = 1


@dataclass(frozen=True)
class DataBoundaryConfig:
    allow_raw_data: bool = False
    allowed_export_paths: tuple[Path, ...] = ()
    allowed_export_hosts: tuple[str, ...] = ()


@dataclass(frozen=True)
class UpdateConfig:
    updates_dir: Path | None = None
    current_dir: Path | None = None
    manifest_filename: str = "manifest.json"
    require_manifest: bool = False
    require_signature: bool = False
    signature_public_key_path: Path | None = None
    signature_public_key_pem_env: str | None = None


@dataclass(frozen=True)
class PriorityConfig:
    survival_mode: bool = False
    min_priority_level: int = 50
    header_name: str = "X-CWI-Priority"


@dataclass(frozen=True)
class MetricsSigningConfig:
    require_signature: bool = False
    private_key_path: Path | None = None
    private_key_pem_env: str | None = None
    public_key_path: Path | None = None
    public_key_pem_env: str | None = None
    key_id: str | None = None
    replay_window: int = 1000


@dataclass(frozen=True)
class MetricsIngestionConfig:
    allowed_node_ids: tuple[str, ...] = ()
    allowed_key_ids: tuple[str, ...] = ()
    allowed_interfaces: tuple[str, ...] = ()
    allowed_interface_types: tuple[str, ...] = ()


@dataclass(frozen=True)
class RerouteConfig:
    warning_threshold: float = 0.7
    critical_threshold: float = 0.9
    clear_threshold: float = 0.5
    cooldown_seconds: int = 60
    transport_url: str | None = None
    transport_timeout_seconds: int = 5


@dataclass(frozen=True)
class LinkMetricsTransportConfig:
    transport_url: str | None = None
    transport_timeout_seconds: int = 5
    use_envelope: bool = True


@dataclass(frozen=True)
class JobRunnerConfig:
    allow_commands: tuple[str, ...] = ()
    default_timeout_seconds: int = 60
    working_dir: Path | None = None


@dataclass(frozen=True)
class StatusConfig:
    status_url: str | None = None
    timeout_seconds: int = 5


@dataclass(frozen=True)
class QrServiceConfig:
    identifier_version: str = "machine-id-sha256-v1"
    allowed_networks: tuple[str, ...] = (
        "127.0.0.1/32",
        "::1/128",
        "100.64.0.0/10",
        "fd7a:115c:a1e0::/48",
    )


@dataclass(frozen=True)
class ControlSessionConfig:
    connect_url: str | None = None
    tenant_id: str | None = None
    tenant_id_env: str | None = None
    owner_user_id: str | None = None
    owner_user_id_env: str | None = None
    friendly_name: str | None = None
    friendly_name_env: str | None = None
    runtime_version: str = "0.1.0"
    policy_profile_version: str = "1.0"
    protocol_version: str = "1.0"
    keepalive_interval_seconds: int = 20
    heartbeat_interval_seconds: int = 45
    reconnect_backoff_seconds: tuple[int, ...] = (1, 2, 5, 10, 30)
    work_token_issuer: str = "cwi-orchestrator"
    work_token_secret: str | None = None
    work_token_secret_env: str | None = None
    replay_cache_max_entries: int = 10000


@dataclass(frozen=True)
class EnrollmentTlsConfig:
    cert_path: Path | None = None
    key_path: Path | None = None
    ca_bundle_path: Path | None = None
    verify_hostname: bool = True
    cert_paths: tuple[Path, ...] = ()
    key_paths: tuple[Path, ...] = ()
    rotation_seconds: int = 0
    rotation_grace_seconds: int = 0
    revocation_denylist_path: Path | None = None
    revocation_crl_url: str | None = None
    revocation_crl_path: Path | None = None
    revocation_crl_cache_path: Path | None = None
    revocation_crl_refresh_seconds: int = 3600
    revocation_crl_timeout_seconds: int = 5
    revocation_fail_open: bool = False


@dataclass(frozen=True)
class EnrollmentAuthConfig:
    token: str | None = None
    token_env: str | None = None
    token_type: str = "Bearer"
    token_rotation_seconds: int = 0
    tokens: tuple[str, ...] = ()


@dataclass(frozen=True)
class EnrollmentConfig:
    request_timeout_seconds: int = 10
    protocol_version: str = "1.0"
    cert_fingerprint: str | None = None
    tls: EnrollmentTlsConfig = field(default_factory=EnrollmentTlsConfig)
    auth: EnrollmentAuthConfig = field(default_factory=EnrollmentAuthConfig)


@dataclass(frozen=True)
class AgentConfig:
    coordinator_url: str
    enrollment_url: str
    identity: IdentityConfig
    storage: StorageConfig = field(default_factory=StorageConfig)
    degradation: DegradationConfig = field(default_factory=DegradationConfig)
    readiness: ReadinessConfig = field(default_factory=ReadinessConfig)
    activation: ActivationConfig = field(default_factory=ActivationConfig)
    kill_switch: KillSwitchConfig = field(default_factory=KillSwitchConfig)
    tamper: TamperConfig = field(default_factory=TamperConfig)
    bundle: BundleConfig = field(default_factory=BundleConfig)
    cache: CacheConfig = field(default_factory=CacheConfig)
    baseline_packs: BaselinePacksConfig = field(default_factory=BaselinePacksConfig)
    local_model: LocalModelConfig = field(default_factory=LocalModelConfig)
    resilience: ResilienceConfig = field(default_factory=ResilienceConfig)
    data_boundary: DataBoundaryConfig = field(default_factory=DataBoundaryConfig)
    update: UpdateConfig = field(default_factory=UpdateConfig)
    priority: PriorityConfig = field(default_factory=PriorityConfig)
    metrics_signing: MetricsSigningConfig = field(default_factory=MetricsSigningConfig)
    metrics_ingestion: MetricsIngestionConfig = field(default_factory=MetricsIngestionConfig)
    link_metrics: LinkMetricsTransportConfig = field(default_factory=LinkMetricsTransportConfig)
    reroute: RerouteConfig = field(default_factory=RerouteConfig)
    job_runner: JobRunnerConfig = field(default_factory=JobRunnerConfig)
    status: StatusConfig = field(default_factory=StatusConfig)
    qr_service: QrServiceConfig = field(default_factory=QrServiceConfig)
    control_session: ControlSessionConfig = field(default_factory=ControlSessionConfig)
    enrollment: EnrollmentConfig = field(default_factory=EnrollmentConfig)


class ConfigError(ValueError):
    """Raised when config validation fails."""


def load_config(path: str | Path) -> AgentConfig:
    config_path = Path(path)
    data = _load_yaml(config_path)
    base_dir = config_path.parent

    coordinator_url = _require_str(data, "coordinator_url")
    enrollment_url = _require_str(data, "enrollment_url")
    identity = _load_identity(data, base_dir)
    storage = _load_storage(data, base_dir)
    degradation = _load_degradation(data)
    readiness = _load_readiness(data)
    activation = _load_activation(data, base_dir)
    kill_switch = _load_kill_switch(data, base_dir)
    tamper = _load_tamper(data, base_dir)
    bundle = _load_bundle(data, base_dir)
    cache = _load_cache(data, base_dir)
    baseline_packs = _load_baseline_packs(data, base_dir)
    local_model = _load_local_model(data)
    resilience = _load_resilience(data, base_dir)
    data_boundary = _load_data_boundary(data, base_dir)
    update = _load_update(data, base_dir)
    priority = _load_priority(data)
    metrics_signing = _load_metrics_signing(data, base_dir)
    metrics_ingestion = _load_metrics_ingestion(data)
    link_metrics = _load_link_metrics(data)
    reroute = _load_reroute(data)
    job_runner = _load_job_runner(data, base_dir)
    status = _load_status(data)
    qr_service = _load_qr_service(data)
    control_session = _load_control_session(data)
    enrollment = _load_enrollment(data, base_dir)

    return AgentConfig(
        coordinator_url=coordinator_url,
        enrollment_url=enrollment_url,
        identity=identity,
        storage=storage,
        degradation=degradation,
        readiness=readiness,
        activation=activation,
        kill_switch=kill_switch,
        tamper=tamper,
        bundle=bundle,
        cache=cache,
        baseline_packs=baseline_packs,
        local_model=local_model,
        resilience=resilience,
        data_boundary=data_boundary,
        update=update,
        priority=priority,
        metrics_signing=metrics_signing,
        metrics_ingestion=metrics_ingestion,
        link_metrics=link_metrics,
        reroute=reroute,
        job_runner=job_runner,
        status=status,
        qr_service=qr_service,
        control_session=control_session,
        enrollment=enrollment,
    )


def ensure_state_dir(config: AgentConfig) -> Path | None:
    if config.storage.state_dir is None:
        return None
    config.storage.state_dir.mkdir(parents=True, exist_ok=True)
    return config.storage.state_dir


def _load_yaml(path: Path) -> Mapping[str, Any]:
    if not path.exists():
        raise ConfigError(f"Config not found: {path}")
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ConfigError("Config must be a YAML mapping")
    return data


def _require_str(data: Mapping[str, Any], key: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Missing or invalid '{key}'")
    return value.strip()

def _optional_str(data: Mapping[str, Any], key: str) -> str | None:
    value = data.get(key)
    if value is None:
        return None
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Invalid '{key}'")
    return value.strip()


def _load_identity(data: Mapping[str, Any], base_dir: Path) -> IdentityConfig:
    identity = data.get("identity")
    if not isinstance(identity, dict):
        raise ConfigError("Missing identity section")

    node_id = _optional_str(identity, "node_id") or _optional_str(identity, "device_id")
    if node_id and node_id.lower() == "auto":
        machine_id_path_value = _optional_str(identity, "machine_id_path")
        machine_id_path = (
            _resolve_path(base_dir, machine_id_path_value)
            if machine_id_path_value
            else DEFAULT_MACHINE_ID_PATH
        )
        try:
            node_id = derive_node_id_from_machine_id(machine_id_path)
        except ValueError as exc:
            raise ConfigError(f"Unable to derive identity.node_id automatically: {exc}") from exc
    if not node_id:
        raise ConfigError("Missing identity.node_id (or legacy identity.device_id)")

    private_key_path_value = _optional_str(identity, "private_key_path")
    public_key_path_value = _optional_str(identity, "public_key_path")
    persist_keys = _resolve_optional_bool(identity.get("persist_keys"), True)
    if persist_keys and (not private_key_path_value or not public_key_path_value):
        raise ConfigError("identity.private_key_path and identity.public_key_path are required")

    private_key_path = (
        _resolve_path(base_dir, private_key_path_value) if private_key_path_value else None
    )
    public_key_path = (
        _resolve_path(base_dir, public_key_path_value) if public_key_path_value else None
    )

    private_key_pem_env = _optional_str(identity, "private_key_pem_env")
    public_key_pem_env = _optional_str(identity, "public_key_pem_env")

    key_type = identity.get("key_type", "ed25519")
    if not isinstance(key_type, str) or not key_type.strip():
        raise ConfigError("Invalid identity.key_type")
    return IdentityConfig(
        node_id=node_id,
        private_key_path=private_key_path,
        public_key_path=public_key_path,
        key_type=key_type.strip(),
        private_key_pem_env=private_key_pem_env,
        public_key_pem_env=public_key_pem_env,
        persist_keys=persist_keys,
    )


def _load_storage(data: Mapping[str, Any], base_dir: Path) -> StorageConfig:
    storage = data.get("storage")
    if storage is None:
        return StorageConfig()
    if not isinstance(storage, dict):
        raise ConfigError("Invalid storage section")
    state_dir_value = storage.get("state_dir")
    if state_dir_value is None:
        return StorageConfig()
    if not isinstance(state_dir_value, str) or not state_dir_value.strip():
        raise ConfigError("Invalid storage.state_dir")
    return StorageConfig(state_dir=_resolve_path(base_dir, state_dir_value.strip()))


def _load_degradation(data: Mapping[str, Any]) -> DegradationConfig:
    degradation = data.get("degradation")
    if degradation is None:
        return DegradationConfig()
    if not isinstance(degradation, dict):
        raise ConfigError("Invalid degradation section")
    cpu_warning = _resolve_optional_float(degradation.get("cpu_warning_load"), 1.0)
    cpu_critical = _resolve_optional_float(degradation.get("cpu_critical_load"), 2.0)
    cpu_warning_percent = _resolve_optional_float(degradation.get("cpu_warning_percent"), 70.0)
    cpu_critical_percent = _resolve_optional_float(degradation.get("cpu_critical_percent"), 90.0)
    disk_warning = _resolve_optional_float(degradation.get("disk_warning_free_ratio"), 0.1)
    disk_critical = _resolve_optional_float(degradation.get("disk_critical_free_ratio"), 0.05)
    memory_warning = _resolve_optional_float(degradation.get("memory_warning_free_ratio"), 0.2)
    memory_critical = _resolve_optional_float(degradation.get("memory_critical_free_ratio"), 0.1)
    transport_url = _optional_str(degradation, "transport_url")
    transport_timeout_seconds = _resolve_optional_int(
        degradation.get("transport_timeout_seconds"),
        5,
        minimum=1,
    )
    return DegradationConfig(
        cpu_warning_load=cpu_warning,
        cpu_critical_load=cpu_critical,
        cpu_warning_percent=cpu_warning_percent,
        cpu_critical_percent=cpu_critical_percent,
        disk_warning_free_ratio=disk_warning,
        disk_critical_free_ratio=disk_critical,
        memory_warning_free_ratio=memory_warning,
        memory_critical_free_ratio=memory_critical,
        transport_url=transport_url,
        transport_timeout_seconds=transport_timeout_seconds,
    )


def _load_readiness(data: Mapping[str, Any]) -> ReadinessConfig:
    readiness = data.get("readiness")
    if readiness is None:
        return ReadinessConfig()
    if not isinstance(readiness, dict):
        raise ConfigError("Invalid readiness section")
    max_degradation = _resolve_optional_float(readiness.get("max_degradation_severity"), 0.9)
    timeout = _resolve_optional_int(readiness.get("dependency_timeout_seconds"), 2, minimum=1)
    urls_raw = readiness.get("dependency_urls", ())
    if isinstance(urls_raw, str):
        urls = (urls_raw.strip(),) if urls_raw.strip() else ()
    elif isinstance(urls_raw, list):
        urls = tuple(_require_str({"dependency_url": item}, "dependency_url") for item in urls_raw)
    elif urls_raw is None:
        urls = ()
    else:
        raise ConfigError("Invalid readiness.dependency_urls")
    return ReadinessConfig(
        max_degradation_severity=max_degradation,
        dependency_urls=urls,
        dependency_timeout_seconds=timeout,
    )


def _load_activation(data: Mapping[str, Any], base_dir: Path) -> ActivationConfig:
    activation = data.get("activation")
    if activation is None:
        return ActivationConfig()
    if not isinstance(activation, dict):
        raise ConfigError("Invalid activation section")
    require_signature = _resolve_optional_bool(activation.get("require_signature"), True)
    expected_issuer = _optional_str(activation, "expected_issuer")
    expected_audience = _optional_str(activation, "expected_audience")

    public_key_path_value = _optional_str(activation, "public_key_path")
    public_key_path = (
        _resolve_path(base_dir, public_key_path_value) if public_key_path_value else None
    )

    public_key_pem_env = _optional_str(activation, "public_key_pem_env")
    public_key_pem = os.getenv(public_key_pem_env) if public_key_pem_env else None

    handshake_path_value = _optional_str(activation, "handshake_path")
    handshake_path = (
        _resolve_path(base_dir, handshake_path_value) if handshake_path_value else None
    )
    nonce_cache_max_entries = _resolve_optional_int(
        activation.get("nonce_cache_max_entries"),
        10000,
        minimum=1,
    )
    status_url = _optional_str(activation, "status_url")
    status_timeout_seconds = _resolve_optional_int(
        activation.get("status_timeout_seconds"),
        5,
        minimum=1,
    )

    return ActivationConfig(
        require_signature=require_signature,
        expected_issuer=expected_issuer,
        expected_audience=expected_audience,
        public_key_path=public_key_path,
        public_key_pem=public_key_pem,
        handshake_path=handshake_path,
        nonce_cache_max_entries=nonce_cache_max_entries,
        status_url=status_url,
        status_timeout_seconds=status_timeout_seconds,
    )


def _load_kill_switch(data: Mapping[str, Any], base_dir: Path) -> KillSwitchConfig:
    kill_switch = data.get("kill_switch")
    if kill_switch is None:
        return KillSwitchConfig()
    if not isinstance(kill_switch, dict):
        raise ConfigError("Invalid kill_switch section")
    admin_url = _optional_str(kill_switch, "admin_url")
    tenant_id = _optional_str(kill_switch, "tenant_id")
    break_glass_token_env = _optional_str(kill_switch, "break_glass_token_env")
    state_path_value = _optional_str(kill_switch, "state_path")
    state_path = _resolve_path(base_dir, state_path_value) if state_path_value else None
    retry_attempts = _resolve_optional_int(kill_switch.get("retry_attempts"), 3, minimum=1)
    retry_backoff_seconds = _resolve_optional_int(
        kill_switch.get("retry_backoff_seconds"),
        1,
        minimum=0,
    )
    return KillSwitchConfig(
        admin_url=admin_url,
        tenant_id=tenant_id,
        break_glass_token_env=break_glass_token_env,
        state_path=state_path,
        retry_attempts=retry_attempts,
        retry_backoff_seconds=retry_backoff_seconds,
    )


def _load_tamper(data: Mapping[str, Any], base_dir: Path) -> TamperConfig:
    tamper = data.get("tamper")
    if tamper is None:
        return TamperConfig()
    if not isinstance(tamper, dict):
        raise ConfigError("Invalid tamper section")

    indicator_files_exist = _load_path_list(
        tamper.get("indicator_files_exist"),
        base_dir,
        "tamper.indicator_files_exist",
    )
    indicator_files_sha256 = _load_sha256_map(
        tamper.get("indicator_files_sha256"),
        base_dir,
        "tamper.indicator_files_sha256",
    )
    indicator_env_vars = _load_str_list(
        tamper.get("indicator_env_vars"),
        "tamper.indicator_env_vars",
    )
    policy_action = _optional_str(tamper, "policy_action") or "ignore"
    policy_action = policy_action.strip().lower()
    if policy_action not in {"ignore", "ticket", "kill", "kill_irreversible"}:
        raise ConfigError("Invalid tamper.policy_action")
    policy_minimum_severity = _resolve_optional_float(
        tamper.get("policy_minimum_severity"),
        1.0,
    )
    if policy_minimum_severity < 0 or policy_minimum_severity > 1:
        raise ConfigError("tamper.policy_minimum_severity must be between 0 and 1")
    policy_ticket_path_value = _optional_str(tamper, "policy_ticket_path")
    policy_ticket_path = (
        _resolve_path(base_dir, policy_ticket_path_value)
        if policy_ticket_path_value
        else None
    )

    return TamperConfig(
        indicator_files_exist=indicator_files_exist,
        indicator_files_sha256=indicator_files_sha256,
        indicator_env_vars=indicator_env_vars,
        policy_action=policy_action,
        policy_minimum_severity=policy_minimum_severity,
        policy_ticket_path=policy_ticket_path,
    )


def _load_bundle(data: Mapping[str, Any], base_dir: Path) -> BundleConfig:
    bundle = data.get("bundle")
    if bundle is None:
        return BundleConfig()
    if not isinstance(bundle, dict):
        raise ConfigError("Invalid bundle section")

    sealed_bundle_path_value = _optional_str(bundle, "sealed_bundle_path")
    sealed_bundle_path = (
        _resolve_path(base_dir, sealed_bundle_path_value)
        if sealed_bundle_path_value
        else None
    )
    unseal_dir_value = _optional_str(bundle, "unseal_dir")
    unseal_dir = _resolve_path(base_dir, unseal_dir_value) if unseal_dir_value else None
    crypto_provider = _optional_str(bundle, "crypto_provider") or "noop"
    crypto_key_path_value = _optional_str(bundle, "crypto_key_path")
    crypto_key_path = (
        _resolve_path(base_dir, crypto_key_path_value)
        if crypto_key_path_value
        else None
    )
    crypto_key_env = _optional_str(bundle, "crypto_key_env")

    return BundleConfig(
        sealed_bundle_path=sealed_bundle_path,
        unseal_dir=unseal_dir,
        crypto_provider=crypto_provider.strip().lower(),
        crypto_key_path=crypto_key_path,
        crypto_key_env=crypto_key_env,
    )


def _load_cache(data: Mapping[str, Any], base_dir: Path) -> CacheConfig:
    cache = data.get("cache")
    if cache is None:
        return CacheConfig()
    if not isinstance(cache, dict):
        raise ConfigError("Invalid cache section")

    cache_dir_value = _optional_str(cache, "cache_dir")
    cache_dir = _resolve_path(base_dir, cache_dir_value) if cache_dir_value else None
    max_bytes = _resolve_optional_int(cache.get("max_bytes"), 100 * 1024 * 1024, minimum=1)
    max_items = _resolve_optional_int(cache.get("max_items"), 1000, minimum=1)
    require_sha256 = _resolve_optional_bool(cache.get("require_sha256"), True)
    require_signature = _resolve_optional_bool(cache.get("require_signature"), False)

    signature_key_path_value = _optional_str(cache, "signature_public_key_path")
    signature_public_key_path = (
        _resolve_path(base_dir, signature_key_path_value) if signature_key_path_value else None
    )
    signature_public_key_pem_env = _optional_str(cache, "signature_public_key_pem_env")

    return CacheConfig(
        cache_dir=cache_dir,
        max_bytes=max_bytes,
        max_items=max_items,
        require_sha256=require_sha256,
        require_signature=require_signature,
        signature_public_key_path=signature_public_key_path,
        signature_public_key_pem_env=signature_public_key_pem_env,
    )


def _load_baseline_packs(data: Mapping[str, Any], base_dir: Path) -> BaselinePacksConfig:
    baseline = data.get("baseline_packs")
    if baseline is None:
        return BaselinePacksConfig()
    if not isinstance(baseline, dict):
        raise ConfigError("Invalid baseline_packs section")

    roots = _load_path_list(
        baseline.get("roots"),
        base_dir,
        "baseline_packs.roots",
    )
    return BaselinePacksConfig(roots=roots)


def _load_local_model(data: Mapping[str, Any]) -> LocalModelConfig:
    local_model = data.get("local_model")
    if local_model is None:
        return LocalModelConfig()
    if not isinstance(local_model, dict):
        raise ConfigError("Invalid local_model section")

    enabled = _resolve_optional_bool(local_model.get("enabled"), False)
    provider = (_optional_str(local_model, "provider") or "ollama").strip().lower()
    if provider != "ollama":
        raise ConfigError("local_model.provider must be 'ollama'")

    base_url_env = _optional_str(local_model, "base_url_env")
    base_url = (
        _resolve_env_str(base_url_env)
        or _optional_str(local_model, "base_url")
        or "http://127.0.0.1:11434"
    )
    model_env = _optional_str(local_model, "model_env")
    model = _resolve_env_str(model_env) or _optional_str(local_model, "model") or ""
    model_family = _optional_str(local_model, "model_family") or "7b/8b-instruct"
    quantization = _optional_str(local_model, "quantization") or "Q4"
    context_tokens = _resolve_optional_int(local_model.get("context_tokens"), 4096, minimum=512)
    max_vram_gb = _resolve_optional_float(local_model.get("max_vram_gb"), 6.0)
    if max_vram_gb <= 0:
        raise ConfigError("local_model.max_vram_gb must be > 0")
    timeout_seconds = _resolve_optional_int(local_model.get("timeout_seconds"), 45, minimum=1)

    if enabled and not model:
        raise ConfigError("local_model.model is required when local_model.enabled is true")

    return LocalModelConfig(
        enabled=enabled,
        provider=provider,
        base_url=base_url,
        base_url_env=base_url_env,
        model=model,
        model_env=model_env,
        model_family=model_family,
        quantization=quantization,
        context_tokens=context_tokens,
        max_vram_gb=max_vram_gb,
        timeout_seconds=timeout_seconds,
    )


def _load_resilience(data: Mapping[str, Any], base_dir: Path) -> ResilienceConfig:
    resilience = data.get("resilience")
    if resilience is None:
        return ResilienceConfig()
    if not isinstance(resilience, dict):
        raise ConfigError("Invalid resilience section")

    queue_dir_value = _optional_str(resilience, "queue_dir")
    queue_dir = _resolve_path(base_dir, queue_dir_value) if queue_dir_value else None
    max_entries = _resolve_optional_int(resilience.get("max_entries"), 1000, minimum=1)
    max_bytes = _resolve_optional_int(resilience.get("max_bytes"), 10 * 1024 * 1024, minimum=1)
    max_attempts = _resolve_optional_int(resilience.get("max_attempts"), 3, minimum=1)
    backoff_seconds = _resolve_optional_int(resilience.get("backoff_seconds"), 1, minimum=0)

    return ResilienceConfig(
        queue_dir=queue_dir,
        max_entries=max_entries,
        max_bytes=max_bytes,
        max_attempts=max_attempts,
        backoff_seconds=backoff_seconds,
    )


def _load_data_boundary(data: Mapping[str, Any], base_dir: Path) -> DataBoundaryConfig:
    boundary = data.get("data_boundary")
    if boundary is None:
        return DataBoundaryConfig()
    if not isinstance(boundary, dict):
        raise ConfigError("Invalid data_boundary section")

    allow_raw_data = _resolve_optional_bool(boundary.get("allow_raw_data"), False)
    allowed_export_paths = _load_path_list(
        boundary.get("allowed_export_paths"),
        base_dir,
        "data_boundary.allowed_export_paths",
    )
    allowed_export_hosts = _load_str_list(
        boundary.get("allowed_export_hosts"),
        "data_boundary.allowed_export_hosts",
    )

    return DataBoundaryConfig(
        allow_raw_data=allow_raw_data,
        allowed_export_paths=allowed_export_paths,
        allowed_export_hosts=allowed_export_hosts,
    )


def _load_update(data: Mapping[str, Any], base_dir: Path) -> UpdateConfig:
    update = data.get("update")
    if update is None:
        return UpdateConfig()
    if not isinstance(update, dict):
        raise ConfigError("Invalid update section")

    updates_dir_value = _optional_str(update, "updates_dir")
    updates_dir = _resolve_path(base_dir, updates_dir_value) if updates_dir_value else None
    current_dir_value = _optional_str(update, "current_dir")
    current_dir = _resolve_path(base_dir, current_dir_value) if current_dir_value else None
    manifest_filename = _optional_str(update, "manifest_filename") or "manifest.json"
    require_manifest = _resolve_optional_bool(update.get("require_manifest"), False)
    require_signature = _resolve_optional_bool(update.get("require_signature"), False)
    signature_public_key_path_value = _optional_str(update, "signature_public_key_path")
    signature_public_key_path = (
        _resolve_path(base_dir, signature_public_key_path_value)
        if signature_public_key_path_value
        else None
    )
    signature_public_key_pem_env = _optional_str(update, "signature_public_key_pem_env")

    return UpdateConfig(
        updates_dir=updates_dir,
        current_dir=current_dir,
        manifest_filename=manifest_filename,
        require_manifest=require_manifest,
        require_signature=require_signature,
        signature_public_key_path=signature_public_key_path,
        signature_public_key_pem_env=signature_public_key_pem_env,
    )


def _load_priority(data: Mapping[str, Any]) -> PriorityConfig:
    priority = data.get("priority")
    if priority is None:
        return PriorityConfig()
    if not isinstance(priority, dict):
        raise ConfigError("Invalid priority section")

    survival_mode = _resolve_optional_bool(priority.get("survival_mode"), False)
    min_priority_level = _resolve_optional_int(priority.get("min_priority_level"), 50, minimum=0)
    if min_priority_level > 100:
        raise ConfigError("priority.min_priority_level must be <= 100")
    header_name = _optional_str(priority, "header_name") or "X-CWI-Priority"

    return PriorityConfig(
        survival_mode=survival_mode,
        min_priority_level=min_priority_level,
        header_name=header_name,
    )


def _load_metrics_signing(data: Mapping[str, Any], base_dir: Path) -> MetricsSigningConfig:
    metrics = data.get("metrics_signing")
    if metrics is None:
        return MetricsSigningConfig()
    if not isinstance(metrics, dict):
        raise ConfigError("Invalid metrics_signing section")

    require_signature = _resolve_optional_bool(metrics.get("require_signature"), False)
    private_key_path_value = _optional_str(metrics, "private_key_path")
    private_key_path = _resolve_path(base_dir, private_key_path_value) if private_key_path_value else None
    private_key_pem_env = _optional_str(metrics, "private_key_pem_env")
    public_key_path_value = _optional_str(metrics, "public_key_path")
    public_key_path = _resolve_path(base_dir, public_key_path_value) if public_key_path_value else None
    public_key_pem_env = _optional_str(metrics, "public_key_pem_env")
    key_id = _optional_str(metrics, "key_id")
    replay_window = _resolve_optional_int(metrics.get("replay_window"), 1000, minimum=1)

    return MetricsSigningConfig(
        require_signature=require_signature,
        private_key_path=private_key_path,
        private_key_pem_env=private_key_pem_env,
        public_key_path=public_key_path,
        public_key_pem_env=public_key_pem_env,
        key_id=key_id,
        replay_window=replay_window,
    )


def _load_metrics_ingestion(data: Mapping[str, Any]) -> MetricsIngestionConfig:
    ingestion = data.get("metrics_ingestion")
    if ingestion is None:
        return MetricsIngestionConfig()
    if not isinstance(ingestion, dict):
        raise ConfigError("Invalid metrics_ingestion section")

    allowed_node_ids = _load_str_list(ingestion.get("allowed_node_ids"), "metrics_ingestion.allowed_node_ids")
    allowed_key_ids = _load_str_list(ingestion.get("allowed_key_ids"), "metrics_ingestion.allowed_key_ids")
    allowed_interfaces = _load_str_list(
        ingestion.get("allowed_interfaces"),
        "metrics_ingestion.allowed_interfaces",
    )
    allowed_interface_types = _load_str_list(
        ingestion.get("allowed_interface_types"),
        "metrics_ingestion.allowed_interface_types",
    )
    return MetricsIngestionConfig(
        allowed_node_ids=allowed_node_ids,
        allowed_key_ids=allowed_key_ids,
        allowed_interfaces=allowed_interfaces,
        allowed_interface_types=allowed_interface_types,
    )


def _load_link_metrics(data: Mapping[str, Any]) -> LinkMetricsTransportConfig:
    link_metrics = data.get("link_metrics")
    if link_metrics is None:
        return LinkMetricsTransportConfig()
    if not isinstance(link_metrics, dict):
        raise ConfigError("Invalid link_metrics section")

    transport_url = _optional_str(link_metrics, "transport_url")
    transport_timeout_seconds = _resolve_optional_int(
        link_metrics.get("transport_timeout_seconds"),
        5,
        minimum=1,
    )
    use_envelope = _resolve_optional_bool(link_metrics.get("use_envelope"), True)

    return LinkMetricsTransportConfig(
        transport_url=transport_url,
        transport_timeout_seconds=transport_timeout_seconds,
        use_envelope=use_envelope,
    )


def _load_reroute(data: Mapping[str, Any]) -> RerouteConfig:
    reroute = data.get("reroute")
    if reroute is None:
        return RerouteConfig()
    if not isinstance(reroute, dict):
        raise ConfigError("Invalid reroute section")

    warning_threshold = _resolve_optional_float(reroute.get("warning_threshold"), 0.7)
    critical_threshold = _resolve_optional_float(reroute.get("critical_threshold"), 0.9)
    clear_threshold = _resolve_optional_float(reroute.get("clear_threshold"), 0.5)
    cooldown_seconds = _resolve_optional_int(reroute.get("cooldown_seconds"), 60, minimum=0)
    transport_url = _optional_str(reroute, "transport_url")
    transport_timeout_seconds = _resolve_optional_int(
        reroute.get("transport_timeout_seconds"),
        5,
        minimum=1,
    )

    return RerouteConfig(
        warning_threshold=warning_threshold,
        critical_threshold=critical_threshold,
        clear_threshold=clear_threshold,
        cooldown_seconds=cooldown_seconds,
        transport_url=transport_url,
        transport_timeout_seconds=transport_timeout_seconds,
    )


def _load_job_runner(data: Mapping[str, Any], base_dir: Path) -> JobRunnerConfig:
    runner = data.get("job_runner")
    if runner is None:
        return JobRunnerConfig()
    if not isinstance(runner, dict):
        raise ConfigError("Invalid job_runner section")

    allow_commands = _load_str_list(runner.get("allow_commands"), "job_runner.allow_commands")
    default_timeout_seconds = _resolve_optional_int(
        runner.get("default_timeout_seconds"),
        60,
        minimum=1,
    )
    working_dir_value = _optional_str(runner, "working_dir")
    working_dir = _resolve_path(base_dir, working_dir_value) if working_dir_value else None

    return JobRunnerConfig(
        allow_commands=allow_commands,
        default_timeout_seconds=default_timeout_seconds,
        working_dir=working_dir,
    )


def _load_status(data: Mapping[str, Any]) -> StatusConfig:
    status = data.get("status")
    if status is None:
        return StatusConfig()
    if not isinstance(status, dict):
        raise ConfigError("Invalid status section")

    status_url = _optional_str(status, "status_url")
    timeout_seconds = _resolve_optional_int(status.get("timeout_seconds"), 5, minimum=1)

    return StatusConfig(
        status_url=status_url,
        timeout_seconds=timeout_seconds,
    )


def _load_qr_service(data: Mapping[str, Any]) -> QrServiceConfig:
    qr_service = data.get("qr_service")
    if qr_service is None:
        return QrServiceConfig()
    if not isinstance(qr_service, dict):
        raise ConfigError("Invalid qr_service section")

    identifier_version = _optional_str(qr_service, "identifier_version") or "machine-id-sha256-v1"
    allowed_networks = _load_cidr_list(
        qr_service.get("allowed_networks"),
        "qr_service.allowed_networks",
    ) or QrServiceConfig().allowed_networks

    return QrServiceConfig(
        identifier_version=identifier_version,
        allowed_networks=allowed_networks,
    )


def _load_control_session(data: Mapping[str, Any]) -> ControlSessionConfig:
    control = data.get("control_session")
    if control is None:
        return ControlSessionConfig()
    if not isinstance(control, dict):
        raise ConfigError("Invalid control_session section")

    connect_url = _optional_str(control, "connect_url")
    tenant_id_env = _optional_str(control, "tenant_id_env")
    tenant_id = _optional_str(control, "tenant_id") or _resolve_env_str(tenant_id_env)
    owner_user_id_env = _optional_str(control, "owner_user_id_env")
    owner_user_id = _optional_str(control, "owner_user_id") or _resolve_env_str(owner_user_id_env)
    friendly_name_env = _optional_str(control, "friendly_name_env")
    friendly_name = _optional_str(control, "friendly_name") or _resolve_env_str(friendly_name_env)
    runtime_version = _optional_str(control, "runtime_version") or "0.1.0"
    policy_profile_version = _optional_str(control, "policy_profile_version") or "1.0"
    protocol_version = _optional_str(control, "protocol_version") or "1.0"
    keepalive_interval_seconds = _resolve_optional_int(
        control.get("keepalive_interval_seconds"),
        20,
        minimum=1,
    )
    heartbeat_interval_seconds = _resolve_optional_int(
        control.get("heartbeat_interval_seconds"),
        45,
        minimum=1,
    )
    reconnect_backoff_values = control.get("reconnect_backoff_seconds")
    reconnect_backoff_seconds = _load_int_list(
        reconnect_backoff_values,
        "control_session.reconnect_backoff_seconds",
    ) if reconnect_backoff_values is not None else (1, 2, 5, 10, 30)
    work_token_issuer = _optional_str(control, "work_token_issuer") or "cwi-orchestrator"
    work_token_secret = _optional_str(control, "work_token_secret")
    work_token_secret_env = _optional_str(control, "work_token_secret_env")
    if work_token_secret is None and work_token_secret_env:
        env_value = os.getenv(work_token_secret_env)
        work_token_secret = env_value.strip() if env_value and env_value.strip() else None
    replay_cache_max_entries = _resolve_optional_int(
        control.get("replay_cache_max_entries"),
        10000,
        minimum=1,
    )

    return ControlSessionConfig(
        connect_url=connect_url,
        tenant_id=tenant_id,
        tenant_id_env=tenant_id_env,
        owner_user_id=owner_user_id,
        owner_user_id_env=owner_user_id_env,
        friendly_name=friendly_name,
        friendly_name_env=friendly_name_env,
        runtime_version=runtime_version,
        policy_profile_version=policy_profile_version,
        protocol_version=protocol_version,
        keepalive_interval_seconds=keepalive_interval_seconds,
        heartbeat_interval_seconds=heartbeat_interval_seconds,
        reconnect_backoff_seconds=reconnect_backoff_seconds,
        work_token_issuer=work_token_issuer,
        work_token_secret=work_token_secret,
        work_token_secret_env=work_token_secret_env,
        replay_cache_max_entries=replay_cache_max_entries,
    )


def _load_enrollment(data: Mapping[str, Any], base_dir: Path) -> EnrollmentConfig:
    enrollment = data.get("enrollment")
    if enrollment is None:
        return EnrollmentConfig()
    if not isinstance(enrollment, dict):
        raise ConfigError("Invalid enrollment section")
    timeout = enrollment.get("request_timeout_seconds", 10)
    if not isinstance(timeout, int) or timeout <= 0:
        raise ConfigError("Invalid enrollment.request_timeout_seconds")
    protocol_version = enrollment.get("protocol_version", "1.0")
    if not isinstance(protocol_version, str) or not protocol_version.strip():
        raise ConfigError("Invalid enrollment.protocol_version")
    cert_fingerprint = _optional_str(enrollment, "cert_fingerprint")
    tls = _load_enrollment_tls(enrollment, base_dir)
    auth = _load_enrollment_auth(enrollment)
    return EnrollmentConfig(
        request_timeout_seconds=timeout,
        protocol_version=protocol_version.strip(),
        cert_fingerprint=cert_fingerprint,
        tls=tls,
        auth=auth,
    )


def _load_enrollment_tls(enrollment: Mapping[str, Any], base_dir: Path) -> EnrollmentTlsConfig:
    mtls = enrollment.get("mtls")
    if mtls is None:
        return EnrollmentTlsConfig()
    if not isinstance(mtls, dict):
        raise ConfigError("Invalid enrollment.mtls section")

    cert = _optional_str(mtls, "cert_path")
    key = _optional_str(mtls, "key_path")
    cert_paths = _load_path_list(mtls.get("cert_paths"), base_dir, "enrollment.mtls.cert_paths")
    key_paths = _load_path_list(mtls.get("key_paths"), base_dir, "enrollment.mtls.key_paths")
    if cert_paths or key_paths:
        if cert or key:
            raise ConfigError("enrollment.mtls.cert_path/key_path cannot be combined with cert_paths/key_paths")
        if len(cert_paths) != len(key_paths):
            raise ConfigError("enrollment.mtls.cert_paths and enrollment.mtls.key_paths must be paired")
        for path in cert_paths:
            if not path.exists():
                raise ConfigError(f"Enrollment mTLS cert not found: {path}")
        for path in key_paths:
            if not path.exists():
                raise ConfigError(f"Enrollment mTLS key not found: {path}")

    if (cert and not key) or (key and not cert):
        raise ConfigError("enrollment.mtls.cert_path and enrollment.mtls.key_path must be paired")

    cert_path = _resolve_path(base_dir, cert) if cert else None
    key_path = _resolve_path(base_dir, key) if key else None
    if cert_path and not cert_path.exists():
        raise ConfigError(f"Enrollment mTLS cert not found: {cert_path}")
    if key_path and not key_path.exists():
        raise ConfigError(f"Enrollment mTLS key not found: {key_path}")

    ca_bundle = _optional_str(mtls, "ca_bundle_path")
    ca_bundle_path = _resolve_path(base_dir, ca_bundle) if ca_bundle else None
    if ca_bundle_path and not ca_bundle_path.exists():
        raise ConfigError(f"Enrollment mTLS CA bundle not found: {ca_bundle_path}")

    verify_hostname = _resolve_optional_bool(mtls.get("verify_hostname"), True)
    rotation_seconds = _resolve_optional_int(mtls.get("rotation_seconds"), 0, minimum=0)
    rotation_grace_seconds = _resolve_optional_int(mtls.get("rotation_grace_seconds"), 0, minimum=0)
    revocation_denylist = _optional_str(mtls, "revocation_denylist_path")
    revocation_denylist_path = _resolve_path(base_dir, revocation_denylist) if revocation_denylist else None
    if revocation_denylist_path and not revocation_denylist_path.exists():
        raise ConfigError(f"Enrollment mTLS denylist not found: {revocation_denylist_path}")
    revocation_crl_url = _optional_str(mtls, "revocation_crl_url")
    revocation_crl_path_value = _optional_str(mtls, "revocation_crl_path")
    revocation_crl_path = _resolve_path(base_dir, revocation_crl_path_value) if revocation_crl_path_value else None
    if revocation_crl_path and not revocation_crl_path.exists():
        raise ConfigError(f"Enrollment mTLS CRL not found: {revocation_crl_path}")
    revocation_crl_cache_value = _optional_str(mtls, "revocation_crl_cache_path")
    revocation_crl_cache_path = _resolve_path(base_dir, revocation_crl_cache_value) if revocation_crl_cache_value else None
    revocation_crl_refresh_seconds = _resolve_optional_int(
        mtls.get("revocation_crl_refresh_seconds"), 3600, minimum=0
    )
    revocation_crl_timeout_seconds = _resolve_optional_int(
        mtls.get("revocation_crl_timeout_seconds"), 5, minimum=1
    )
    revocation_fail_open = _resolve_optional_bool(mtls.get("revocation_fail_open"), False)
    return EnrollmentTlsConfig(
        cert_path=cert_path,
        key_path=key_path,
        ca_bundle_path=ca_bundle_path,
        verify_hostname=verify_hostname,
        cert_paths=cert_paths,
        key_paths=key_paths,
        rotation_seconds=rotation_seconds,
        rotation_grace_seconds=rotation_grace_seconds,
        revocation_denylist_path=revocation_denylist_path,
        revocation_crl_url=revocation_crl_url,
        revocation_crl_path=revocation_crl_path,
        revocation_crl_cache_path=revocation_crl_cache_path,
        revocation_crl_refresh_seconds=revocation_crl_refresh_seconds,
        revocation_crl_timeout_seconds=revocation_crl_timeout_seconds,
        revocation_fail_open=revocation_fail_open,
    )


def _load_enrollment_auth(enrollment: Mapping[str, Any]) -> EnrollmentAuthConfig:
    auth = enrollment.get("auth")
    if auth is None:
        return EnrollmentAuthConfig()
    if not isinstance(auth, dict):
        raise ConfigError("Invalid enrollment.auth section")

    token = _optional_str(auth, "token")
    token_env = _optional_str(auth, "token_env")
    token_type = _optional_str(auth, "token_type") or "Bearer"
    token_rotation_seconds = _resolve_optional_int(auth.get("token_rotation_seconds"), 0, minimum=0)
    tokens_raw = auth.get("tokens")
    tokens = _load_str_list(tokens_raw, "enrollment.auth.tokens") if tokens_raw is not None else ()

    return EnrollmentAuthConfig(
        token=token,
        token_env=token_env,
        token_type=token_type,
        token_rotation_seconds=token_rotation_seconds,
        tokens=tokens,
    )


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
    raise ConfigError(f"Expected boolean value, got {value}")


def _resolve_env_str(env_name: str | None) -> str | None:
    if not env_name:
        return None
    value = os.getenv(env_name)
    if value is None or not value.strip():
        return None
    return value.strip()


def _resolve_optional_float(value: Any, default: float) -> float:
    if value is None:
        return default
    try:
        parsed = float(value)
    except (TypeError, ValueError) as exc:
        raise ConfigError(f"Expected float value, got {value}") from exc
    return parsed


def _resolve_optional_int(value: Any, default: int, *, minimum: int | None = None) -> int:
    if value is None:
        parsed = default
    elif isinstance(value, int):
        parsed = value
    elif isinstance(value, str):
        try:
            parsed = int(value.strip())
        except ValueError as exc:
            raise ConfigError(f"Expected integer value, got {value}") from exc
    else:
        raise ConfigError(f"Expected integer value, got {value}")
    if minimum is not None and parsed < minimum:
        raise ConfigError(f"Value must be >= {minimum}")
    return parsed


def _resolve_path(base_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    if not path.is_absolute():
        path = (base_dir / path).resolve()
    return path


def _load_str_list(value: Any, key: str) -> tuple[str, ...]:
    if value is None:
        return ()
    if isinstance(value, str):
        return (value.strip(),) if value.strip() else ()
    if isinstance(value, list):
        items: list[str] = []
        for item in value:
            if not isinstance(item, str) or not item.strip():
                raise ConfigError(f"Invalid {key} entry")
            items.append(item.strip())
        return tuple(items)
    raise ConfigError(f"Invalid {key}")


def _load_int_list(value: Any, key: str) -> tuple[int, ...]:
    if isinstance(value, int):
        return (value,)
    if not isinstance(value, list):
        raise ConfigError(f"Invalid {key}")
    items: list[int] = []
    for item in value:
        items.append(_resolve_optional_int(item, 0, minimum=0))
    return tuple(items)


def _load_path_list(value: Any, base_dir: Path, key: str) -> tuple[Path, ...]:
    if value is None:
        return ()
    if isinstance(value, str):
        value = [value]
    if not isinstance(value, list):
        raise ConfigError(f"Invalid {key}")
    paths: list[Path] = []
    for item in value:
        if not isinstance(item, str) or not item.strip():
            raise ConfigError(f"Invalid {key} entry")
        paths.append(_resolve_path(base_dir, item.strip()))
    return tuple(paths)


def _load_cidr_list(value: Any, key: str) -> tuple[str, ...]:
    entries = _load_str_list(value, key)
    validated: list[str] = []
    for entry in entries:
        try:
            network = ipaddress.ip_network(entry, strict=False)
        except ValueError as exc:
            raise ConfigError(f"Invalid {key} entry: {entry}") from exc
        validated.append(str(network))
    return tuple(validated)


def _load_sha256_map(value: Any, base_dir: Path, key: str) -> dict[Path, str]:
    if value is None:
        return {}
    if not isinstance(value, dict):
        raise ConfigError(f"Invalid {key}")
    entries: dict[Path, str] = {}
    for raw_path, raw_hash in value.items():
        if not isinstance(raw_path, str) or not raw_path.strip():
            raise ConfigError(f"Invalid {key} entry path")
        if not isinstance(raw_hash, str) or not raw_hash.strip():
            raise ConfigError(f"Invalid {key} entry hash")
        digest = raw_hash.strip().lower()
        if len(digest) != 64 or any(ch not in string.hexdigits for ch in digest):
            raise ConfigError(f"Invalid {key} sha256 hash")
        entries[_resolve_path(base_dir, raw_path.strip())] = digest
    return entries
