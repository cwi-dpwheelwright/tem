from __future__ import annotations

import argparse
import asyncio
import json
import signal
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from .config import load_config
from .buffer import BufferItem, JsonlBuffer
from .telemetry_importer import import_telemetry_file
from .ingest_client import TelemetryIngestClient
from .opcua_collector import OpcUaCollector, build_payload_from_opcua
from .telemetry_importer import TelemetryTagMap
from .audit import emit_audit_event
from .cert_reload import CertificateWatcher
from .observability import init_observability
from .survival import compute_degradation, SurvivalSignalEmitter
from .survival_client import SurvivalSignalClient
from .metrics_collectors import SimulatedMetricsCollector, SystemMetricsCollector
from .metrics_exporter import MetricsExporter, MetricsExporterConfig
from .token_provider import build_token_provider


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="cwi-edge-agent")
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate_parser = subparsers.add_parser("validate-config", help="Validate agent config")
    validate_parser.add_argument("--config", required=True, help="Path to config file")

    run_parser = subparsers.add_parser("run", help="Run agent loop")
    run_parser.add_argument("--config", required=True, help="Path to config file")
    run_parser.add_argument("--max-batch", type=int, default=25, help="Max buffered items per send")

    import_parser = subparsers.add_parser("import-file", help="Import telemetry file to ingest payload")
    import_parser.add_argument("--input", required=True, help="Path to telemetry CSV/JSON file")
    import_parser.add_argument("--mapping", required=True, help="Path to telemetry_tag_map.yaml")
    import_parser.add_argument("--tenant-id", required=True, help="Tenant id for ingest payload")
    import_parser.add_argument(
        "--buffer",
        help="Optional buffer file path to append payloads (jsonl).",
        default=""
    )
    opcua_parser = subparsers.add_parser("opcua-poll", help="Poll OPC UA endpoint and build ingest payload")
    opcua_parser.add_argument("--endpoint", required=True, help="OPC UA endpoint URL")
    opcua_parser.add_argument("--mapping", required=True, help="Path to telemetry_tag_map.yaml")
    opcua_parser.add_argument("--tenant-id", required=True, help="Tenant id for ingest payload")
    opcua_parser.add_argument(
        "--buffer",
        help="Optional buffer file path to append payloads (jsonl).",
        default=""
    )

    args = parser.parse_args(argv)

    if args.command == "validate-config":
        return _handle_validate(Path(args.config))
    if args.command == "run":
        return _handle_run(Path(args.config), args.max_batch)
    if args.command == "import-file":
        return _handle_import(Path(args.input), Path(args.mapping), args.tenant_id, args.buffer)
    if args.command == "opcua-poll":
        return _handle_opcua(args.endpoint, Path(args.mapping), args.tenant_id, args.buffer)

    parser.print_help()
    return 1


def _handle_validate(config_path: Path) -> int:
    try:
        config, auth_source = load_config(config_path)
    except Exception as exc:  # pragma: no cover - simple CLI wrapper
        print(f"Config invalid: {exc}")
        return 2
    print(
        "Config valid: "
        f"tenant_id={config.tenant_id}, "
        f"gateway_url={config.gateway_url}, "
        f"poll_interval_seconds={config.poll_interval_seconds}, "
        f"buffer_path={config.buffer_path}, "
        f"auth_source={auth_source}"
    )
    return 0


def _handle_run(config_path: Path, max_batch: int) -> int:
    try:
        config, auth_source = load_config(config_path)
    except Exception as exc:  # pragma: no cover - simple CLI wrapper
        print(f"Failed to start: {exc}")
        return 2

    observability = init_observability(config)
    token_provider = build_token_provider(config)
    client = TelemetryIngestClient(
        config.gateway_url,
        token_provider=token_provider,
        mtls_cert_path=config.mtls_cert_path,
        mtls_key_path=config.mtls_key_path,
        revoked_serials_path=config.mtls_revoked_serials_path,
        ca_bundle_path=config.mtls_ca_bundle_path,
        crl_url=config.mtls_crl_url,
        crl_cache_path=config.mtls_crl_cache_path,
        crl_refresh_seconds=config.mtls_crl_refresh_seconds,
        pinned_fingerprints=config.mtls_pinned_fingerprints,
        verify_hostname=config.mtls_verify_hostname,
        timeout_seconds=config.ingest_timeout_seconds,
        observability=observability,
    )
    buffer = JsonlBuffer(
        config.buffer_path,
        max_items=config.buffer_max_items,
        max_bytes=config.buffer_max_bytes,
        drop_policy=config.buffer_drop_policy,
    )
    survival_client = SurvivalSignalClient(
        _resolve_survival_endpoint(config),
        token_provider=token_provider,
        mtls_cert_path=config.mtls_cert_path,
        mtls_key_path=config.mtls_key_path,
        revoked_serials_path=config.mtls_revoked_serials_path,
        ca_bundle_path=config.mtls_ca_bundle_path,
        crl_url=config.mtls_crl_url,
        crl_cache_path=config.mtls_crl_cache_path,
        crl_refresh_seconds=config.mtls_crl_refresh_seconds,
        pinned_fingerprints=config.mtls_pinned_fingerprints,
        verify_hostname=config.mtls_verify_hostname,
        timeout_seconds=config.ingest_timeout_seconds,
    )
    survival_emitter = SurvivalSignalEmitter(survival_client, config, observability)
    watcher = _build_cert_watcher(config)
    reload_flag = _setup_reload_signal(config)
    last_reload_check = 0.0
    metrics_collector = _build_metrics_collector(config)
    metrics_exporter = _build_metrics_exporter(config, observability, token_provider)
    last_metrics_emit = 0.0

    print(
        "Edge agent starting: "
        f"tenant_id={config.tenant_id}, "
        f"gateway_url={config.gateway_url}, "
        f"poll_interval_seconds={config.poll_interval_seconds}, "
        f"buffer_path={config.buffer_path}, "
        f"auth_source={auth_source}"
    )
    while True:
        _flush_buffer(client, buffer, max_batch)
        last_metrics_emit = _maybe_emit_metrics(
            metrics_collector,
            metrics_exporter,
            config,
            last_metrics_emit,
        )
        stats = buffer.stats()
        observability.record_buffer(stats.pending_count, stats.size_bytes)
        snapshot = compute_degradation(buffer, client.breaker, config)
        observability.record_degradation(
            snapshot.severity, snapshot.resource_severity, snapshot.network_severity
        )
        survival_emitter.emit_if_needed(snapshot)
        last_reload_check = _maybe_reload_certs(
            watcher, reload_flag, last_reload_check, config.mtls_reload_interval_seconds
        )
        time.sleep(config.poll_interval_seconds)
    return 0


def _handle_import(input_path: Path, mapping_path: Path, tenant_id: str, buffer_path: str) -> int:
    try:
        payload = import_telemetry_file(input_path, mapping_path, tenant_id)
    except Exception as exc:  # pragma: no cover - simple CLI wrapper
        print(f"Import failed: {exc}")
        return 2
    if buffer_path:
        buffer = JsonlBuffer(buffer_path)
        idempotency_key = payload.metadata.get("idempotency_key", f"{tenant_id}:{input_path.name}")
        if buffer.enqueue(BufferItem(item_id=idempotency_key, payload=payload.to_dict())):
            print(f"Buffered payload to {buffer_path}")
        else:
            print("Buffer full. Dropped newest payload per drop policy.")
    else:
        print(json.dumps(payload.to_dict(), indent=2))
    return 0


def _handle_opcua(endpoint: str, mapping_path: Path, tenant_id: str, buffer_path: str) -> int:
    try:
        tag_map = TelemetryTagMap.load(mapping_path)
    except Exception as exc:  # pragma: no cover - simple CLI wrapper
        print(f"Mapping invalid: {exc}")
        return 2
    collector = OpcUaCollector(endpoint, tag_map)
    try:
        sample = asyncio.run(collector.collect_once())
    except Exception as exc:  # pragma: no cover - simple CLI wrapper
        print(f"OPC UA poll failed: {exc}")
        return 2
    payload = build_payload_from_opcua(tenant_id, sample, tag_map)
    if buffer_path:
        buffer = JsonlBuffer(buffer_path)
        if buffer.enqueue(BufferItem(item_id=f"{tenant_id}:{sample.timestamp}", payload=payload.to_dict())):
            print(f"Buffered payload to {buffer_path}")
        else:
            print("Buffer full. Dropped newest payload per drop policy.")
    else:
        print(json.dumps(payload.to_dict(), indent=2))
    return 0


def _flush_buffer(client: TelemetryIngestClient, buffer: JsonlBuffer, max_batch: int) -> None:
    while True:
        pending = buffer.pending(limit=max_batch)
        if not pending:
            return
        sent = 0
        for item in pending:
            result = client.ingest(item.payload)
            if result.ok:
                sent += 1
                continue
            print(f"Ingest failed: {result.error} (status={result.status_code})")
            if result.error == "circuit_open":
                break
            time.sleep(client.backoff.next_delay(sent))
            break
        if sent:
            buffer.ack(sent)
        if sent < len(pending):
            return


class _ReloadFlag:
    def __init__(self) -> None:
        self.requested = False


def _build_cert_watcher(config) -> CertificateWatcher | None:
    if not config.mtls_reload_interval_seconds and not config.mtls_reload_on_sighup:
        return None
    return CertificateWatcher(
        [
            config.mtls_cert_path,
            config.mtls_key_path,
            config.mtls_ca_bundle_path,
            config.mtls_revoked_serials_path,
            config.mtls_crl_cache_path,
        ]
    )


def _setup_reload_signal(config) -> _ReloadFlag | None:
    if not config.mtls_reload_on_sighup:
        return None
    flag = _ReloadFlag()
    if hasattr(signal, "SIGHUP"):
        signal.signal(signal.SIGHUP, lambda *_: _set_reload(flag))
    return flag


def _set_reload(flag: _ReloadFlag) -> None:
    flag.requested = True


def _maybe_reload_certs(
    watcher: CertificateWatcher | None,
    flag: _ReloadFlag | None,
    last_check: float,
    interval_seconds: int | None,
) -> float:
    now = time.time()
    should_check = False
    if interval_seconds and (now - last_check) >= interval_seconds:
        should_check = True
    if flag and flag.requested:
        should_check = True
        flag.requested = False
    if not watcher or not should_check:
        return last_check
    result = watcher.check()
    if result.changed:
        emit_audit_event("cert_reload", {"changed_paths": result.changed_paths})
    return now


def _resolve_survival_endpoint(config) -> str:
    if config.survival_signal_url:
        return config.survival_signal_url
    return f"{config.gateway_url.rstrip('/')}/api/survival/signal"


def _build_metrics_collector(config):
    if not config.metrics_enabled:
        return None
    if config.metrics_collector == "none":
        return None
    if config.metrics_collector == "simulated":
        return SimulatedMetricsCollector(node_id=config.metrics_node_id or "edge-agent")
    if config.metrics_collector == "system":
        return SystemMetricsCollector(node_id=config.metrics_node_id or "edge-agent")
    raise ValueError(f"Unknown metrics collector: {config.metrics_collector}")


def _build_metrics_exporter(config, observability, token_provider):
    if not config.metrics_enabled:
        return None
    exporter_config = MetricsExporterConfig(
        endpoints=config.metrics_endpoints,
        node_id=config.metrics_node_id or "edge-agent",
        queue_path=config.metrics_queue_path,
        queue_max_items=config.metrics_queue_max_items,
        queue_max_bytes=config.metrics_queue_max_bytes,
        queue_drop_policy=config.metrics_queue_drop_policy,
        timeout_seconds=config.metrics_timeout_seconds,
        max_batch=config.metrics_max_batch,
        backoff_base_seconds=config.metrics_backoff_base_seconds,
        backoff_max_seconds=config.metrics_backoff_max_seconds,
        failure_threshold=config.metrics_failure_threshold,
        recovery_seconds=config.metrics_recovery_seconds,
    )
    return MetricsExporter(
        exporter_config,
        token_provider=token_provider,
        mtls_cert_path=config.mtls_cert_path,
        mtls_key_path=config.mtls_key_path,
        revoked_serials_path=config.mtls_revoked_serials_path,
        ca_bundle_path=config.mtls_ca_bundle_path,
        crl_url=config.mtls_crl_url,
        crl_cache_path=config.mtls_crl_cache_path,
        crl_refresh_seconds=config.mtls_crl_refresh_seconds,
        pinned_fingerprints=config.mtls_pinned_fingerprints,
        verify_hostname=config.mtls_verify_hostname,
        observability=observability,
    )


def _maybe_emit_metrics(
    collector,
    exporter,
    config,
    last_emit: float,
) -> float:
    if not collector or not exporter:
        return last_emit
    now = time.time()
    if (now - last_emit) < config.metrics_flush_interval_seconds:
        return last_emit
    observed_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    interfaces = collector.collect(observed_utc)
    if not exporter.enqueue(interfaces, observed_utc):
        print("Metrics queue full. Dropped newest metrics payload.")
    exporter.flush()
    return now


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
