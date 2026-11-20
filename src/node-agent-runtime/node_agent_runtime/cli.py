"""Command-line interface for node agent runtime."""

from __future__ import annotations

import argparse
import json
import sys
import uuid
from pathlib import Path

from .activation import (
    check_and_store_nonce,
    load_activation_remote_status,
    is_activation_valid,
    load_activation_state,
    load_handshake,
    record_activation,
    sync_activation_status,
    verify_handshake,
)
from .bundle import unseal_bundle
from .link_adapters import OsLinkMetricsAdapter
from .link_collection import collect_link_metrics
from .link_metrics import report_payload
from .link_transport import send_link_metrics_report
from .config import ConfigError, ensure_state_dir, load_config
from .control_channel import run_control_session
from .degradation import (
    emit_degradation_event,
    event_payload,
    evaluate_degradation,
    sample_metrics,
    send_or_queue_degradation_event,
)
from .execution_guard import evaluate_execution
from .enrollment import build_enrollment_payload, submit_enrollment
from .health import health_payload, serve_health
from .identity import ensure_identity
from .kill_switch import (
    load_kill_switch_state,
    poll_kill_switch_state,
    resolve_state_path,
    sync_kill_switch_state,
)
from .metrics_ingestion import ingest_metrics_envelope
from .qr_service import serve_qr
from .reroute import evaluate_reroute_hint, hint_payload, send_reroute_hint
from .tamper import (
    apply_tamper_policy,
    decide_tamper_policy,
    emit_tamper_event,
    event_payload as tamper_event_payload,
    evaluate_tamper,
)
from .status_reporter import build_status_report, record_status, send_status


def main() -> None:
    parser = argparse.ArgumentParser(prog="cwi-node-agent")
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate_parser = subparsers.add_parser("validate-config", help="Validate config")
    validate_parser.add_argument("--config", required=True)

    identity_parser = subparsers.add_parser("generate-identity", help="Generate identity keys")
    identity_parser.add_argument("--config", required=True)

    enroll_parser = subparsers.add_parser("enroll", help="Enroll with coordinator")
    enroll_parser.add_argument("--config", required=True)
    enroll_parser.add_argument("--dry-run", action="store_true")
    enroll_parser.add_argument("--output", type=Path)
    enroll_parser.add_argument("--timeout", type=int)

    health_parser = subparsers.add_parser("health", help="Evaluate health/readiness")
    health_parser.add_argument("--config", required=True)

    serve_parser = subparsers.add_parser("serve-health", help="Serve health endpoints")
    serve_parser.add_argument("--config", required=True)
    serve_parser.add_argument("--host", default="127.0.0.1")
    serve_parser.add_argument("--port", type=int, default=8089)

    qr_parser = subparsers.add_parser("serve-qr", help="Serve the home-node QR linking endpoint")
    qr_parser.add_argument("--config", required=True)
    qr_parser.add_argument("--host", default="127.0.0.1")
    qr_parser.add_argument("--port", type=int, default=8088)

    emit_parser = subparsers.add_parser("emit-degradation", help="Emit degradation event")
    emit_parser.add_argument("--config", required=True)
    emit_parser.add_argument("--output", type=Path)

    tamper_parser = subparsers.add_parser("emit-tamper", help="Emit tamper event")
    tamper_parser.add_argument("--config", required=True)
    tamper_parser.add_argument("--output", type=Path)

    unseal_parser = subparsers.add_parser("unseal-bundle", help="Decrypt and unseal bundle")
    unseal_parser.add_argument("--config", required=True)
    unseal_parser.add_argument("--bundle", type=Path)
    unseal_parser.add_argument("--output-dir", type=Path)

    metrics_parser = subparsers.add_parser("collect-link-metrics", help="Collect link metrics report")
    metrics_parser.add_argument("--config", required=True)
    metrics_parser.add_argument("--output", type=Path)

    reroute_parser = subparsers.add_parser("emit-reroute-hint", help="Emit reroute hint")
    reroute_parser.add_argument("--config", required=True)
    reroute_parser.add_argument("--output", type=Path)

    status_parser = subparsers.add_parser("emit-status", help="Emit status report")
    status_parser.add_argument("--config", required=True)
    status_parser.add_argument("--output", type=Path)

    control_parser = subparsers.add_parser(
        "run-control-session",
        help="Run the persistent home-node control session",
    )
    control_parser.add_argument("--config", required=True)

    ingest_parser = subparsers.add_parser(
        "ingest-link-metrics",
        help="Validate external link-metrics envelope",
    )
    ingest_parser.add_argument("--config", required=True)
    ingest_parser.add_argument("--input", type=Path, required=True)
    ingest_parser.add_argument("--output", type=Path)

    activate_parser = subparsers.add_parser("activate", help="Verify and store activation handshake")
    activate_parser.add_argument("--config", required=True)
    activate_parser.add_argument("--handshake", type=Path)
    activate_parser.add_argument("--output", type=Path)

    status_parser = subparsers.add_parser("activation-status", help="Check activation status")
    status_parser.add_argument("--config", required=True)

    sync_activation_parser = subparsers.add_parser(
        "sync-activation-status",
        help="Fetch activation status from coordinator",
    )
    sync_activation_parser.add_argument("--config", required=True)
    sync_activation_parser.add_argument("--output", type=Path)

    ks_sync_parser = subparsers.add_parser("sync-kill-switch", help="Fetch kill-switch status")
    ks_sync_parser.add_argument("--config", required=True)
    ks_sync_parser.add_argument("--output", type=Path)
    ks_sync_parser.add_argument("--interval", type=int)
    ks_sync_parser.add_argument("--max-iterations", type=int)

    ks_status_parser = subparsers.add_parser("kill-switch-status", help="Show kill-switch status")
    ks_status_parser.add_argument("--config", required=True)

    exec_parser = subparsers.add_parser("check-execution", help="Check activation/kill-switch gating")
    exec_parser.add_argument("--config", required=True)

    args = parser.parse_args()

    try:
        config = load_config(args.config)

        if args.command == "validate-config":
            print("config ok")
            return

        if args.command == "health":
            print(json.dumps(health_payload(config), indent=2, sort_keys=True))
            return

        if args.command == "serve-health":
            serve_health(config, args.host, args.port)
            return

        if args.command == "serve-qr":
            serve_qr(config, args.host, args.port)
            return

        if args.command == "emit-degradation":
            ensure_state_dir(config)
            metrics = sample_metrics(config.storage.state_dir)
            snapshot = evaluate_degradation(metrics, config.degradation)
            event = emit_degradation_event(
                metrics,
                snapshot,
                node_id=config.identity.node_id,
                state_dir=config.storage.state_dir,
            )
            payload = event_payload(event)
            if config.degradation.transport_url:
                try:
                    result = send_or_queue_degradation_event(
                        event,
                        degradation=config.degradation,
                        resilience=config.resilience,
                        state_dir=config.storage.state_dir,
                    )
                    payload["transport"] = {
                        "url": config.degradation.transport_url,
                        **result,
                    }
                except Exception as exc:
                    payload["transport"] = {
                        "url": config.degradation.transport_url,
                        "sent": False,
                        "queued": False,
                        "error": str(exc),
                    }
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "emit-tamper":
            ensure_state_dir(config)
            assessment = evaluate_tamper(config.tamper)
            decision = decide_tamper_policy(assessment, config.tamper)
            event_id = uuid.uuid4().hex
            policy = apply_tamper_policy(
                decision,
                config=config,
                observed_utc=assessment.observed_utc,
                event_id=event_id,
            )
            event = emit_tamper_event(
                assessment,
                node_id=config.identity.node_id,
                state_dir=config.storage.state_dir,
                policy=policy,
                event_id=event_id,
            )
            payload = tamper_event_payload(event)
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "unseal-bundle":
            ensure_state_dir(config)
            unsealed = unseal_bundle(
                config.bundle,
                bundle_path=args.bundle,
                output_dir=args.output_dir,
            )
            payload = {
                "bundle_id": unsealed.bundle_id,
                "provider": unsealed.provider,
                "output_dir": str(unsealed.output_dir),
                "files": [str(path) for path in unsealed.files],
            }
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "collect-link-metrics":
            ensure_state_dir(config)
            report = collect_link_metrics(
                config.identity.node_id,
                [OsLinkMetricsAdapter()],
                state_dir=config.storage.state_dir,
            )
            payload = report_payload(report)
            if config.link_metrics.transport_url:
                try:
                    result = send_link_metrics_report(
                        report,
                        node_id=config.identity.node_id,
                        state_dir=config.storage.state_dir,
                        transport=config.link_metrics,
                        signing=config.metrics_signing,
                    )
                    payload["transport"] = {
                        "url": config.link_metrics.transport_url,
                        "sent": result.sent,
                        "envelope": result.envelope_used,
                    }
                except Exception as exc:
                    payload["transport"] = {
                        "url": config.link_metrics.transport_url,
                        "sent": False,
                        "error": str(exc),
                    }
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "emit-reroute-hint":
            ensure_state_dir(config)
            metrics = sample_metrics(config.storage.state_dir)
            snapshot = evaluate_degradation(metrics, config.degradation)
            hint = evaluate_reroute_hint(
                node_id=config.identity.node_id,
                severity=snapshot.severity,
                config=config.reroute,
                state_dir=config.storage.state_dir,
            )
            payload = {
                "severity": snapshot.severity,
                "hint": hint_payload(hint) if hint else None,
            }
            if hint and config.reroute.transport_url:
                try:
                    sent = send_reroute_hint(hint, config.reroute)
                    payload["transport"] = {"url": config.reroute.transport_url, "sent": sent}
                except Exception as exc:
                    payload["transport"] = {"url": config.reroute.transport_url, "sent": False, "error": str(exc)}
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "emit-status":
            ensure_state_dir(config)
            report = build_status_report(config)
            path = record_status(report, config.storage.state_dir)
            payload = {
                "node_id": report.node_id,
                "observed_utc": report.observed_utc,
                "payload": report.payload,
                "local_path": str(path) if path else None,
            }
            if config.status.status_url:
                try:
                    sent = send_status(report, config.status)
                    payload["transport"] = {"url": config.status.status_url, "sent": sent}
                except Exception as exc:
                    payload["transport"] = {"url": config.status.status_url, "sent": False, "error": str(exc)}
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "run-control-session":
            ensure_state_dir(config)
            import asyncio

            asyncio.run(run_control_session(config))
            return

        if args.command == "ingest-link-metrics":
            ensure_state_dir(config)
            raw = json.loads(args.input.read_text(encoding="utf-8"))
            payload = ingest_metrics_envelope(
                raw,
                signing=config.metrics_signing,
                policy=config.metrics_ingestion,
                state_dir=config.storage.state_dir,
            )
            result = {
                "node_id": raw.get("node_id"),
                "observed_utc": raw.get("observed_utc"),
                "payload": payload,
            }
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(result, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(result, indent=2, sort_keys=True))
            return

        if args.command == "activate":
            ensure_state_dir(config)
            handshake_path = args.handshake or config.activation.handshake_path
            if not handshake_path:
                raise ConfigError("Activation handshake path required")
            handshake = load_handshake(handshake_path)
            verify_handshake(handshake, config.activation, expected_device_id=config.identity.node_id)
            check_and_store_nonce(
                config.storage.state_dir,
                handshake.nonce,
                handshake.expires_utc,
                max_entries=config.activation.nonce_cache_max_entries,
            )
            state = record_activation(config.storage.state_dir, handshake)
            payload = {
                "device_id": state.device_id,
                "bundle_id": state.bundle_id,
                "activated_utc": state.activated_utc,
                "expires_utc": state.expires_utc,
            }
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "activation-status":
            state = load_activation_state(config.storage.state_dir)
            valid = is_activation_valid(state, expected_device_id=config.identity.node_id)
            remote_status = load_activation_remote_status(config.storage.state_dir)
            payload = {
                "active": valid,
                "state": state.__dict__ if state else None,
                "remote_status": remote_status.__dict__ if remote_status else None,
            }
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "sync-activation-status":
            ensure_state_dir(config)
            status, status_path, cleared = sync_activation_status(
                config.activation,
                coordinator_url=config.coordinator_url,
                node_id=config.identity.node_id,
                state_dir=config.storage.state_dir,
            )
            payload = {
                "status": status.__dict__,
                "cleared_activation": cleared,
                "remote_status_path": str(status_path) if status_path else None,
            }
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "sync-kill-switch":
            if args.interval:
                poll_kill_switch_state(
                    config,
                    interval_seconds=args.interval,
                    max_iterations=args.max_iterations,
                )
                print(json.dumps({"status": "ok", "mode": "poll"}, indent=2))
                return

            state, state_path = sync_kill_switch_state(config)
            payload = {
                "enabled": state.enabled,
                "irreversible": state.irreversible,
                "updated_utc": state.updated_utc,
                "source": state.source,
                "state_path": str(state_path) if state_path else None,
            }
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "kill-switch-status":
            state_path = resolve_state_path(config)
            state = load_kill_switch_state(state_path)
            payload = {
                "state": state.__dict__ if state else None,
                "state_path": str(state_path) if state_path else None,
            }
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        if args.command == "check-execution":
            decision = evaluate_execution(config)
            payload = {
                "allowed": decision.allowed,
                "reasons": decision.reasons,
                "activation_valid": decision.activation_valid,
                "kill_switch_enabled": decision.kill_switch_enabled,
                "details": decision.details,
            }
            print(json.dumps(payload, indent=2, sort_keys=True))
            return

        ensure_state_dir(config)
        identity = ensure_identity(config.identity)

        if args.command == "generate-identity":
            print(f"node_id={identity.node_id}")
            print(f"private_key={identity.private_key_path}")
            print(f"public_key={identity.public_key_path}")
            return

        if args.command == "enroll":
            payload = build_enrollment_payload(config, identity)
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
                print(f"wrote enrollment request to {args.output}")

            if args.dry_run:
                print(json.dumps(payload, indent=2, sort_keys=True))
                return

            timeout = args.timeout or config.enrollment.request_timeout_seconds
            status, body = submit_enrollment(config, identity, timeout_seconds=timeout)
            print(f"enrollment status={status}")
            if body:
                print(body)
            return

    except (ConfigError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
