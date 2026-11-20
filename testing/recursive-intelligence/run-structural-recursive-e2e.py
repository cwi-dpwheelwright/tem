#!/usr/bin/env python3
"""Run structural recursive intelligence validation.

This runner is intentionally evidence-scoped. It can prove the currently
queryable structural chain, and it reports unavailable proof points as UNKNOWN
instead of upgrading behavioral ASK evidence into structural readiness.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import sys
import time
import urllib.error
import urllib.parse
import urllib.request


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_SUITE = SCRIPT_DIR / "structural-recursive-suite.v1.json"
DEFAULT_OUTPUT_ROOT = REPO_ROOT / "testing" / "runs" / "recursive-intelligence-structural"
SUCCESS_CODES = {200, 201, 202}
AUTH_REFRESH_CODES = {401, 403}
REDACTED = "<redacted>"
SENSITIVE_KEYS = {
    "access_token",
    "refresh_token",
    "token",
    "password",
    "authorization",
    "tenant_id",
    "user_id",
    "user_id_hash",
    "email",
    "username",
    "node_id",
}
REQUIRED_EVENT_FIELDS = {
    "schema_version",
    "event_id",
    "event_type",
    "tenant_id",
    "user_scope",
    "source",
    "confidence",
    "trace_id",
    "policy_status",
    "timestamp_utc",
    "temporal_state",
    "payload",
    "provenance",
}
VALID_EVENT_SOURCE_TYPES = {
    "ask_trace",
    "critic",
    "teacher",
    "process_miner",
    "graph",
    "vector",
    "truth_engine",
    "cache",
    "federated_summary",
    "external_opinion",
}
VALID_POLICY_STATUSES = {"approved", "denied", "observe_only", "pending_review", "quarantined"}
VALID_TEMPORAL_STATES = {"current", "historical", "stale", "conflicted", "unsupported", "estimated"}


def now_utc() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def iso_utc(value: dt.datetime | None = None) -> str:
    return (value or now_utc()).astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def utc_display(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")


def local_display(value: dt.datetime) -> str:
    local = value.astimezone()
    offset = local.strftime("%z")
    formatted_offset = f"{offset[:3]}:{offset[3:]}" if offset else "+00:00"
    return f"{local.strftime('%Y-%m-%d %H:%M:%S')} {local.tzname() or 'local'} (UTC{formatted_offset})"


def run_sort_key(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).strftime("%Y-%m-%d_%H-%M-%S_UTC")


def run_id(value: dt.datetime) -> str:
    stamp = run_sort_key(value)
    return f"{stamp}-{sha256(stamp)[:8]}"


def sha256(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def sha256_json(value: object) -> str:
    return sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), default=str))


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def redact(value: object) -> object:
    if isinstance(value, dict):
        result: dict[str, object] = {}
        for key, item in value.items():
            lowered = key.lower()
            if lowered in SENSITIVE_KEYS or any(part in lowered for part in ("password", "token", "secret")):
                result[key] = REDACTED
            else:
                result[key] = redact(item)
        return result
    if isinstance(value, list):
        return [redact(item) for item in value]
    return value


def parse_json(raw: str) -> object:
    if not raw:
        return None
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return None


def post_json(url: str, payload: dict, headers: dict[str, str], timeout: float) -> dict:
    body = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=body,
        headers={
            "User-Agent": "CWI-structural-recursive-validation/1.0",
            "Content-Type": "application/json",
            "Accept": "application/json",
            **headers,
        },
        method="POST",
    )
    return open_request(request, timeout)


def get_json(url: str, headers: dict[str, str], timeout: float) -> dict:
    request = urllib.request.Request(
        url,
        headers={
            "User-Agent": "CWI-structural-recursive-validation/1.0",
            "Accept": "application/json",
            **headers,
        },
        method="GET",
    )
    return open_request(request, timeout)


def open_request(request: urllib.request.Request, timeout: float) -> dict:
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            raw = response.read().decode("utf-8", errors="replace")
            return {
                "ok": response.status in SUCCESS_CODES,
                "status_code": response.status,
                "body": parse_json(raw),
                "raw": raw,
            }
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", errors="replace")
        return {"ok": False, "status_code": exc.code, "body": parse_json(raw), "raw": raw}
    except urllib.error.URLError as exc:
        return {"ok": False, "status_code": 0, "body": None, "raw": str(exc.reason)}
    except TimeoutError as exc:
        return {"ok": False, "status_code": 0, "body": None, "raw": str(exc)}


def request_with_auth_retry(
    method: str,
    url: str,
    payload: dict | None,
    args: argparse.Namespace,
    headers: dict[str, str],
    auth: dict[str, object],
    run_dir: Path,
) -> dict:
    response = (
        post_json(url, payload or {}, headers, args.timeout_seconds)
        if method == "POST"
        else get_json(url, headers, args.timeout_seconds)
    )
    if response["status_code"] in AUTH_REFRESH_CODES and refresh_identity_auth(args, headers, auth, run_dir):
        retry = (
            post_json(url, payload or {}, headers, args.timeout_seconds)
            if method == "POST"
            else get_json(url, headers, args.timeout_seconds)
        )
        retry["auth_refreshed"] = True
        retry["auth_retry_after_status_code"] = response["status_code"]
        return retry
    return response


def resolve_url(explicit: str, base_url: str, suffix: str) -> str:
    if explicit.strip():
        return explicit.strip().rstrip("/")
    if base_url.strip():
        return base_url.strip().rstrip("/") + suffix
    return ""


def resolve_identity_login_url() -> str:
    explicit = os.environ.get("CWI_IDENTITY_LOGIN_URL", "").strip()
    if explicit:
        return explicit.rstrip("/")
    for name in ("CWI_IDENTITY_BASE_URL", "IDENTITY_URL", "CWI_BASE_URL"):
        value = os.environ.get(name, "").strip()
        if value:
            return value.rstrip("/") + "/auth/login"
    return ""


def identity_password(args: argparse.Namespace) -> str:
    for name in dict.fromkeys([args.identity_password_env, "CWI_IDENTITY_PASSWORD", "IDENTITY_SUPERADMIN_PASSWORD"]):
        if name and os.environ.get(name):
            return os.environ[name]
    return ""


def identity_payload(args: argparse.Namespace) -> dict:
    payload = {"password": identity_password(args)}
    if args.identity_tenant_id:
        payload["tenant_id"] = args.identity_tenant_id
    if args.identity_email:
        payload["email"] = args.identity_email
    if args.identity_username:
        payload["username"] = args.identity_username
    return payload


def identity_configured(args: argparse.Namespace) -> bool:
    return bool(args.identity_login_url and identity_password(args) and (args.identity_username or args.identity_email))


def authenticate(args: argparse.Namespace, run_dir: Path) -> tuple[dict[str, str], dict[str, object]]:
    headers: dict[str, str] = {}
    auth: dict[str, object] = {
        "mode": args.auth_mode,
        "status": "NOT_RUN",
        "identity_login_configured": bool(args.identity_login_url),
        "identity_principal_configured": bool(args.identity_username or args.identity_email),
        "identity_tenant_configured": bool(args.identity_tenant_id),
        "identity_password_configured": bool(identity_password(args)),
        "identity_password_env": args.identity_password_env,
        "access_token_received": False,
    }
    if args.auth_mode == "none":
        auth["detail"] = "Authentication disabled by --auth-mode none."
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth
    if args.auth_mode in ("auto", "token") and args.auth_token:
        headers["Authorization"] = f"Bearer {args.auth_token}"
        auth.update({"mode": "token", "status": "PASS", "detail": "Bearer token supplied.", "access_token_received": True})
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth
    if args.auth_mode == "token":
        auth.update({"status": "FAIL", "detail": "Token auth was requested, but no token was supplied."})
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth
    if not identity_configured(args):
        missing = []
        if not args.identity_login_url:
            missing.append("login_url")
        if not (args.identity_username or args.identity_email):
            missing.append("principal")
        if not identity_password(args):
            missing.append("password_env")
        auth.update({
            "status": "FAIL" if args.auth_mode == "identity" else "NOT_RUN",
            "detail": "Identity login configuration missing: " + ", ".join(missing) + ".",
        })
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth

    response = post_json(args.identity_login_url, identity_payload(args), {}, args.timeout_seconds)
    token = ""
    body = response.get("body")
    if isinstance(body, dict) and isinstance(body.get("access_token"), str):
        token = body["access_token"]
    if token:
        headers["Authorization"] = f"Bearer {token}"
        auth.update({
            "mode": "identity",
            "status": "PASS",
            "detail": "Identity login succeeded and bearer token was applied.",
            "status_code": response["status_code"],
            "access_token_received": True,
        })
    else:
        auth.update({
            "mode": "identity",
            "status": "FAIL",
            "detail": "Identity login did not return an access_token.",
            "status_code": response["status_code"],
            "access_token_received": False,
            "body": redact(body),
        })
    write_json(run_dir / "auth-status.json", auth)
    return headers, auth


def refresh_identity_auth(
    args: argparse.Namespace,
    headers: dict[str, str],
    auth: dict[str, object],
    run_dir: Path,
) -> bool:
    if auth.get("mode") != "identity" or not identity_configured(args):
        return False
    response = post_json(args.identity_login_url, identity_payload(args), {}, args.timeout_seconds)
    auth["refresh_count"] = int(auth.get("refresh_count") or 0) + 1
    auth["last_refresh_status_code"] = response["status_code"]
    body = response.get("body")
    token = body.get("access_token") if isinstance(body, dict) else None
    if isinstance(token, str) and token:
        headers["Authorization"] = f"Bearer {token}"
        auth["status"] = "PASS"
        write_json(run_dir / "auth-status.json", auth)
        return True
    auth["status"] = "FAIL"
    auth["detail"] = "Identity refresh did not return an access_token."
    write_json(run_dir / "auth-status.json", auth)
    return False


def step(step_id: str, status: str, basis: str, evidence_refs: list[str] | None = None, limitation: str = "") -> dict:
    return {
        "step_id": step_id,
        "status": status,
        "basis": basis,
        "evidence_refs": evidence_refs or [],
        "limitation": limitation,
    }


def field(value: object, *names: str) -> object:
    if not isinstance(value, dict):
        return None
    candidates = []
    for name in names:
        candidates.append(name)
        if "_" in name:
            parts = name.split("_")
            candidates.append(parts[0] + "".join(part.title() for part in parts[1:]))
        else:
            candidates.append(name[:1].upper() + name[1:])
    for candidate in candidates:
        if candidate in value:
            return value[candidate]
    return None


def find_by_field(items: object, name: str, expected: str) -> dict | None:
    if not isinstance(items, list):
        return None
    for item in items:
        if isinstance(item, dict) and str(field(item, name) or "") == expected:
            return item
    return None


def validate_event_payload(payload: dict) -> tuple[bool, list[str]]:
    errors: list[str] = []
    missing = sorted(REQUIRED_EVENT_FIELDS - set(payload))
    if missing:
        errors.append("missing:" + ",".join(missing))
    if payload.get("schema_version") != "recursive-event.v1":
        errors.append("schema_version_invalid")
    source = payload.get("source")
    if not isinstance(source, dict) or source.get("source_type") not in VALID_EVENT_SOURCE_TYPES:
        errors.append("source_type_invalid")
    if payload.get("policy_status") not in VALID_POLICY_STATUSES:
        errors.append("policy_status_invalid")
    if payload.get("temporal_state") not in VALID_TEMPORAL_STATES:
        errors.append("temporal_state_invalid")
    provenance = payload.get("provenance")
    if not isinstance(provenance, dict) or not provenance.get("source_hash") or not provenance.get("created_utc"):
        errors.append("provenance_invalid")
    confidence = payload.get("confidence")
    if not isinstance(confidence, (int, float)) or confidence < 0 or confidence > 1:
        errors.append("confidence_invalid")
    return not errors, errors


def build_process_miner_summary(suite: dict, args: argparse.Namespace, run_dir: Path, trace_id: str) -> tuple[dict, str]:
    state_dir = run_dir / "process-miner-state"
    observations = suite["controlled_observations"]
    sys.path.insert(0, str(REPO_ROOT / "src" / "node-agent-runtime"))
    try:
        from node_agent_runtime.process_miner import (  # type: ignore
            LocalProcessMiner,
            ProcessMinerSummaryRequest,
        )

        miner = LocalProcessMiner(state_dir)
        result = miner.observe(
            observations,
            policy_status="approved",
            summary_request=ProcessMinerSummaryRequest(
                tenant_id=args.tenant_id,
                node_id=args.node_id,
                user_id_hash=args.user_id_hash,
                trace_id=trace_id,
                policy_status="approved",
                source_component="structural_validation_runner",
            ),
        )
        payload = result.to_dict()
        payload["implementation"] = "node_agent_runtime.process_miner.LocalProcessMiner"
        return payload, "actual_process_miner"
    except Exception as exc:  # pragma: no cover - fallback is for minimal shells.
        summary = {
            "schema_version": "local_process_miner_summary.v1",
            "tenant_id": args.tenant_id,
            "user_id_hash": args.user_id_hash,
            "node_id": args.node_id,
            "trace_id": trace_id,
            "policy_status": "approved",
            "source_component": "structural_validation_runner",
            "approved_for_export": True,
            "raw_content_exported": False,
            "observations": observations,
            "provenance": {
                "source_hash": sha256_json(observations),
                "source_id": "fallback_structural_summary",
                "generated_utc": iso_utc(),
            },
            "fallback_reason": f"{type(exc).__name__}: {exc}",
        }
        return {
            "status": "observed",
            "observation_count": len(observations),
            "summary": summary,
            "raw_content_exported": False,
            "memory_mutated": False,
            "graph_mutated": False,
            "implementation": "fallback_compatible_summary",
        }, "fallback_process_miner"


def process_summary(result: dict) -> dict:
    summary = result.get("summary")
    return summary if isinstance(summary, dict) else {}


def build_graph_event(
    suite: dict,
    args: argparse.Namespace,
    run: str,
    trace_id: str,
    summary: dict,
    conflict: bool = False,
    parent_event_id: str | None = None,
) -> dict:
    fact = suite["conflict_fact" if conflict else "graph_fact"]
    suffix = "conflict" if conflict else "primary"
    event_id = f"struct-{suffix}-{sha256(f'{run}|{suffix}')[:32]}"
    created = iso_utc()
    payload = {
        "suite_id": suite["suite_id"],
        "candidate_state": "conflict" if conflict else "add",
        "subject": fact["subject"],
        "relation": fact["relation"],
        "object": fact["object"],
        "fact_key": f"{fact['fact_key_prefix']}:{run}",
        "summary_ref": summary.get("provenance", {}).get("source_id", "process_miner_summary"),
    }
    if conflict and parent_event_id:
        payload["supersedes_event_id"] = parent_event_id
    return {
        "schema_version": "recursive-event.v1",
        "event_id": event_id,
        "event_type": "graph_fact.observed",
        "tenant_id": args.tenant_id,
        "user_scope": {
            "scope_type": "user",
            "user_id_hash": args.user_id_hash,
            "node_id": args.node_id,
        },
        "source": {
            "component": "structural_validation_runner",
            "source_type": "process_miner",
            "source_id": str(summary.get("provenance", {}).get("source_id") or f"process-miner-{run}"),
            "node_id": args.node_id,
        },
        "confidence": 0.82 if conflict else 0.91,
        "trace_id": trace_id,
        "policy_status": "approved",
        "timestamp_utc": created,
        "temporal_state": "conflicted" if conflict else "current",
        "payload": payload,
        "provenance": {
            "source_hash": summary.get("provenance", {}).get("source_hash") or sha256_json(summary),
            "created_utc": created,
            "actor": "structural-validation-runner",
            "parent_event_ids": [parent_event_id] if parent_event_id else [],
        },
    }


def build_quality_event(suite: dict, args: argparse.Namespace, run: str, trace_id: str) -> dict:
    created = iso_utc()
    payload = dict(suite["quality_observation"])
    return {
        "schema_version": "recursive-event.v1",
        "event_id": f"quality-{sha256(f'{run}|quality')[:32]}",
        "event_type": "quality_observation.created",
        "tenant_id": args.tenant_id,
        "user_scope": {
            "scope_type": "user",
            "user_id_hash": args.user_id_hash,
        },
        "source": {
            "component": "structural_validation_runner",
            "source_type": "critic",
            "source_id": trace_id,
        },
        "confidence": 0.78,
        "trace_id": trace_id,
        "policy_status": "observe_only",
        "timestamp_utc": created,
        "temporal_state": "current",
        "payload": payload,
        "provenance": {
            "source_hash": sha256_json(payload),
            "created_utc": created,
            "actor": "structural-validation-runner",
            "parent_event_ids": [],
        },
    }


def query_url(base: str, **params: object) -> str:
    filtered = {key: value for key, value in params.items() if value is not None and value != ""}
    return base + ("?" + urllib.parse.urlencode(filtered) if filtered else "")


def artifact_response(path: Path, response: dict) -> None:
    write_json(path, redact(response))


def execute(args: argparse.Namespace, suite: dict, run: str, run_dir: Path, headers: dict[str, str], auth: dict[str, object]) -> list[dict]:
    steps: list[dict] = []
    trace_id = f"structural-{run}"
    summary_result, process_miner_mode = build_process_miner_summary(suite, args, run_dir, trace_id)
    write_json(run_dir / "process-miner-result.json", redact(summary_result))
    summary = process_summary(summary_result)

    if process_miner_mode == "actual_process_miner":
        steps.append(step("process_miner_observation", "PASS", "Actual node-agent process miner produced sanitized observations and a summary.", ["process-miner-result.json"]))
    else:
        steps.append(step("process_miner_observation", "UNKNOWN", "Fallback compatible summary was produced, but the actual process miner import did not run.", ["process-miner-result.json"]))

    safe_summary = bool(summary.get("approved_for_export")) and summary.get("raw_content_exported") is False
    steps.append(step(
        "controlled_input_event",
        "PASS" if safe_summary else "FAIL",
        "Controlled observations are sanitized and approved for export." if safe_summary else "Process-miner summary was not approved for export or raw content export was not false.",
        ["process-miner-result.json"],
    ))

    graph_event = build_graph_event(suite, args, run, trace_id, summary)
    valid, errors = validate_event_payload(graph_event)
    write_json(run_dir / "recursive-event-primary-request.json", redact(graph_event))
    steps.append(step(
        "normalized_recursive_event",
        "PASS" if valid else "FAIL",
        "Recursive event payload satisfies local required-field and enum checks." if valid else f"Recursive event payload failed local checks: {', '.join(errors)}",
        ["recursive-event-primary-request.json"],
    ))

    ingest = request_with_auth_retry("POST", args.recursive_events_url, graph_event, args, headers, auth, run_dir)
    artifact_response(run_dir / "recursive-event-primary-response.json", ingest)
    accepted = ingest["ok"] and isinstance(ingest.get("body"), dict) and field(ingest["body"], "accepted") is True
    steps.append(step(
        "recursive_event_ingest",
        "PASS" if accepted else "FAIL",
        "Recursive event API accepted the controlled process-miner event." if accepted else f"Recursive event API did not accept the event; status {ingest['status_code']}.",
        ["recursive-event-primary-request.json", "recursive-event-primary-response.json"],
    ))

    queried_event = None
    if accepted:
        events_url = query_url(args.recursive_events_url, tenant_id=args.tenant_id, event_type="graph_fact.observed", page_size=200)
        events = request_with_auth_retry("GET", events_url, None, args, headers, auth, run_dir)
        artifact_response(run_dir / "recursive-events-query-response.json", events)
        queried_event = find_by_field(events.get("body"), "event_id", graph_event["event_id"])
        steps.append(step(
            "recursive_event_query",
            "PASS" if queried_event else "FAIL",
            "Accepted recursive event is queryable by the recursive event API." if queried_event else "Accepted recursive event was not found in query results.",
            ["recursive-events-query-response.json"],
        ))
    else:
        steps.append(step("recursive_event_query", "NOT_RUN", "Skipped because recursive event ingest failed."))

    candidate = None
    if accepted:
        candidate_url = query_url(
            args.graph_candidates_url,
            tenant_id=args.tenant_id,
            candidate_state="add",
            temporal_state="current",
            page_size=200,
        )
        candidates = request_with_auth_retry("GET", candidate_url, None, args, headers, auth, run_dir)
        artifact_response(run_dir / "graph-candidates-query-response.json", candidates)
        candidate = find_by_field(candidates.get("body"), "recursive_event_id", graph_event["event_id"])
        steps.append(step(
            "graph_candidate_created",
            "PASS" if candidate else "FAIL",
            "Graph update candidate was created for the process-miner recursive event." if candidate else "No graph update candidate was found for the process-miner recursive event.",
            ["graph-candidates-query-response.json"],
        ))
    else:
        steps.append(step("graph_candidate_created", "NOT_RUN", "Skipped because recursive event ingest failed."))

    conflict_event = build_graph_event(suite, args, run, trace_id, summary, conflict=True, parent_event_id=graph_event["event_id"])
    write_json(run_dir / "recursive-event-conflict-request.json", redact(conflict_event))
    conflict_ingest = request_with_auth_retry("POST", args.recursive_events_url, conflict_event, args, headers, auth, run_dir)
    artifact_response(run_dir / "recursive-event-conflict-response.json", conflict_ingest)
    conflict_candidate = None
    if conflict_ingest["ok"] and isinstance(conflict_ingest.get("body"), dict) and field(conflict_ingest["body"], "accepted") is True:
        conflict_url = query_url(
            args.graph_candidates_url,
            tenant_id=args.tenant_id,
            candidate_state="conflict",
            temporal_state="conflicted",
            page_size=200,
        )
        conflict_candidates = request_with_auth_retry("GET", conflict_url, None, args, headers, auth, run_dir)
        artifact_response(run_dir / "graph-conflict-candidates-query-response.json", conflict_candidates)
        conflict_candidate = find_by_field(conflict_candidates.get("body"), "recursive_event_id", conflict_event["event_id"])
    steps.append(step(
        "graph_conflict_candidate_created",
        "PASS" if conflict_candidate else "FAIL",
        "Conflict recursive event produced a conflicted graph update candidate." if conflict_candidate else "Conflict recursive event did not produce a queryable conflicted graph candidate.",
        ["recursive-event-conflict-request.json", "recursive-event-conflict-response.json", "graph-conflict-candidates-query-response.json"],
    ))

    vector_job = None
    if candidate:
        candidate_id = str(field(candidate, "candidate_id") or "")
        vector_url = query_url(args.vector_jobs_url, tenant_id=args.tenant_id, graph_candidate_id=candidate_id, page_size=50)
        vector_jobs = request_with_auth_retry("GET", vector_url, None, args, headers, auth, run_dir)
        artifact_response(run_dir / "graph-vector-refresh-jobs-query-response.json", vector_jobs)
        vector_job = find_by_field(vector_jobs.get("body"), "graph_candidate_id", candidate_id)
        steps.append(step(
            "graph_vector_refresh_job_created",
            "PASS" if vector_job else "FAIL",
            "Graph-to-vector refresh job was created for the graph candidate." if vector_job else "No vector refresh job was found for the graph candidate.",
            ["graph-vector-refresh-jobs-query-response.json"],
        ))
    else:
        steps.append(step("graph_vector_refresh_job_created", "NOT_RUN", "Skipped because graph candidate was not available."))

    refresh_status = str(field(vector_job, "refresh_status") or "").lower() if vector_job else ""
    vector_evidence_refs = ["graph-vector-refresh-jobs-query-response.json"]
    if vector_job and refresh_status in {"pending", "running"} and args.vector_process_url:
        process_payload = {"tenant_id": args.tenant_id, "page_size": 25}
        write_json(run_dir / "graph-vector-refresh-process-request.json", redact(process_payload))
        vector_process = request_with_auth_retry("POST", args.vector_process_url, process_payload, args, headers, auth, run_dir)
        artifact_response(run_dir / "graph-vector-refresh-process-response.json", vector_process)
        vector_evidence_refs.append("graph-vector-refresh-process-response.json")
        refreshed_jobs = request_with_auth_retry("GET", vector_url, None, args, headers, auth, run_dir)
        artifact_response(run_dir / "graph-vector-refresh-jobs-after-process-response.json", refreshed_jobs)
        vector_evidence_refs.append("graph-vector-refresh-jobs-after-process-response.json")
        vector_job = find_by_field(refreshed_jobs.get("body"), "graph_candidate_id", candidate_id)
        refresh_status = str(field(vector_job, "refresh_status") or "").lower() if vector_job else refresh_status

    if refresh_status == "completed":
        steps.append(step("vector_refresh_completed", "PASS", "Vector refresh job completed after graph candidate vector upsert.", vector_evidence_refs))
    elif refresh_status in {"pending", "running"}:
        steps.append(step(
            "vector_refresh_completed",
            "UNKNOWN",
            f"Vector refresh job exists but is {refresh_status}; actual vector index update is not yet proven.",
            vector_evidence_refs,
        ))
    elif refresh_status == "failed":
        steps.append(step("vector_refresh_completed", "FAIL", "Vector refresh job is failed.", vector_evidence_refs))
    else:
        steps.append(step("vector_refresh_completed", "NOT_RUN", "Skipped because no vector refresh job was available."))

    if args.ask_url:
        ask_payload = {
            "question": "Using governed graph evidence, which role owns dashboard telemetry contract in structural validation?",
            "pack": {"name": args.pack_name},
            "context": {
                "mode": args.policy_mode,
                "top_k": args.top_k,
                "conversation_id": f"structural-{run}",
                "recursive_structural_run_id": run,
            },
            "tenant_id": args.tenant_id,
        }
        ask = request_with_auth_retry("POST", args.ask_url, ask_payload, args, headers, auth, run_dir)
        write_json(run_dir / "ask-retrieval-probe-request.json", redact(ask_payload))
        artifact_response(run_dir / "ask-retrieval-probe-response.json", ask)
        body = ask.get("body") if isinstance(ask.get("body"), dict) else {}
        trace = field(body, "trace")
        trace = trace if isinstance(trace, dict) else {}
        graph_used = field(trace, "graph_used") is True or int(field(trace, "graph_hits") or 0) > 0
        truth_state = field(trace, "truth_state")
        steps.append(step(
            "ask_retrieval_improvement",
            "PASS" if graph_used else "UNKNOWN",
            "ASK trace used graph evidence after structural event ingestion." if graph_used else "ASK completed, but graph-backed retrieval improvement was not proven by the trace.",
            ["ask-retrieval-probe-request.json", "ask-retrieval-probe-response.json"],
        ))
        steps.append(step(
            "truth_engine_trace_signal",
            "PASS" if truth_state else "UNKNOWN",
            "ASK response trace includes Truth Engine state." if truth_state else "ASK response trace did not expose Truth Engine state for this probe.",
            ["ask-retrieval-probe-request.json", "ask-retrieval-probe-response.json"],
        ))
    else:
        steps.append(step("ask_retrieval_improvement", "NOT_RUN", "ASK URL was not configured for retrieval probe."))
        steps.append(step("truth_engine_trace_signal", "NOT_RUN", "ASK URL was not configured for Truth Engine trace probe."))

    external_payload = {
        "tenant_id": args.tenant_id,
        "trace_id": trace_id,
        "policy_status": "approved",
        "purpose": "response_quality_review",
        "prompt_summary": "controlled_structural_recursive_validation_probe",
        "context_summary": "process_miner_graph_vector_quality_chain",
        "governed_answer_hash": sha256("structural-validation-answer"),
        "evidence_hashes": [sha256_json(summary), sha256_json(graph_event)],
    }
    write_json(run_dir / "external-opinion-request.json", redact(external_payload))
    external = request_with_auth_retry("POST", args.external_opinion_url, external_payload, args, headers, auth, run_dir)
    artifact_response(run_dir / "external-opinion-response.json", external)
    external_body = external.get("body") if isinstance(external.get("body"), dict) else {}
    external_status = str(field(external_body, "external_submission_status") or "").lower()
    steps.append(step(
        "external_opinion_observed",
        "PASS" if external["ok"] else "FAIL",
        "External opinion path returned a governed advisory response." if external["ok"] else f"External opinion path failed with status {external['status_code']}.",
        ["external-opinion-request.json", "external-opinion-response.json"],
    ))
    steps.append(step(
        "runtime_critic_external_submission",
        "PASS" if external_status == "submitted" else ("FAIL" if external_status == "failed" else "UNKNOWN"),
        "External runtime critic/opinion submission completed." if external_status == "submitted" else f"External submission status is {external_status or 'unknown'}; scoring is not independently proven.",
        ["external-opinion-response.json"],
    ))

    quality_event = build_quality_event(suite, args, run, trace_id)
    write_json(run_dir / "recursive-event-quality-request.json", redact(quality_event))
    quality_ingest = request_with_auth_retry("POST", args.recursive_events_url, quality_event, args, headers, auth, run_dir)
    artifact_response(run_dir / "recursive-event-quality-response.json", quality_ingest)
    quality_accepted = quality_ingest["ok"] and isinstance(quality_ingest.get("body"), dict) and field(quality_ingest["body"], "accepted") is True
    steps.append(step(
        "quality_observation_ingest",
        "PASS" if quality_accepted else "FAIL",
        "Quality observation recursive event was accepted." if quality_accepted else "Quality observation recursive event was not accepted.",
        ["recursive-event-quality-request.json", "recursive-event-quality-response.json"],
    ))

    quality_graph = request_with_auth_retry(
        "GET",
        query_url(args.response_quality_graph_url, tenant_id=args.tenant_id, page_size=200),
        None,
        args,
        headers,
        auth,
        run_dir,
    )
    artifact_response(run_dir / "response-quality-graph-response.json", quality_graph)
    quality_body = quality_graph.get("body") if isinstance(quality_graph.get("body"), dict) else {}
    active_issues = field(quality_body, "active_issues") or []
    quality_issue = find_by_field(active_issues, "event_id", quality_event["event_id"])
    steps.append(step(
        "response_quality_graph_update",
        "PASS" if quality_issue else "FAIL",
        "Response Quality Graph exposes the controlled quality observation as an active issue." if quality_issue else "Response Quality Graph did not expose the controlled quality observation.",
        ["response-quality-graph-response.json"],
    ))

    federated_payload = {
        "tenant_id": args.tenant_id,
        "user_id_hash": args.user_id_hash,
        "node_id": args.node_id,
        "trace_id": trace_id,
        "policy_status": "approved",
        "source_component": "structural_validation_runner",
        "observations": [
            {
                "category": item.get("type", "routine"),
                "signal": item.get("signal", "structural_signal"),
                "outcome": item.get("outcome", "observed"),
                "count": item.get("count", 1),
                "confidence": item.get("confidence", 0.5),
                "tags": item.get("tags", []),
                "metadata": {"suite": "structural_recursive"},
            }
            for item in suite["controlled_observations"]
        ],
        "provenance": {
            "source_hash": sha256_json(summary),
            "source_id": "structural_validation_runner",
            "generated_utc": iso_utc(),
        },
    }
    write_json(run_dir / "federated-summary-request.json", redact(federated_payload))
    federated = request_with_auth_retry("POST", args.federated_summary_url, federated_payload, args, headers, auth, run_dir)
    artifact_response(run_dir / "federated-summary-response.json", federated)
    fed_body = federated.get("body") if isinstance(federated.get("body"), dict) else {}
    fed_groups = int(field(fed_body, "summary_group_count") or 0)
    steps.append(step(
        "federated_summary_observed",
        "PASS" if federated["ok"] and fed_groups > 0 else "FAIL",
        "Federated summary path accepted sanitized observations and produced summary groups." if federated["ok"] and fed_groups > 0 else "Federated summary path did not produce queryable summary groups.",
        ["federated-summary-request.json", "federated-summary-response.json"],
    ))

    cache_key = f"structural-validation:{run}"
    cache_policy_version = "structural-validation-v1"
    cache_pack_hash = sha256("structural-validation-pack")
    cache_payload = {
        "cache_key": cache_key,
        "tenant_id": args.tenant_id,
        "scope": "tenant",
        "user_id_hash": args.user_id_hash,
        "source_proof_json": json.dumps(
            {
                "suite_id": suite["suite_id"],
                "trace_id": trace_id,
                "source_hash": sha256_json(summary),
            },
            sort_keys=True,
            separators=(",", ":"),
        ),
        "trace_id": trace_id,
        "fresh_until_utc": iso_utc(now_utc() + dt.timedelta(days=1)),
        "policy_mode": args.policy_mode,
        "policy_version": cache_policy_version,
        "pack_hash": cache_pack_hash,
        "validation_status": "validated",
        "critic_status": "pass",
        "payload_hash": sha256_json(summary),
    }
    write_json(run_dir / "validated-cache-upsert-request.json", redact(cache_payload))
    cache_upsert = request_with_auth_retry("POST", args.validated_cache_url, cache_payload, args, headers, auth, run_dir)
    artifact_response(run_dir / "validated-cache-upsert-response.json", cache_upsert)
    cache_eval_url = query_url(
        args.validated_cache_url + "/evaluate",
        tenant_id=args.tenant_id,
        cache_key=cache_key,
        user_id_hash=args.user_id_hash,
        policy_mode=args.policy_mode,
        policy_version=cache_policy_version,
        pack_hash=cache_pack_hash,
        now_utc=iso_utc(),
    )
    cache_eval = request_with_auth_retry("GET", cache_eval_url, None, args, headers, auth, run_dir)
    artifact_response(run_dir / "validated-cache-evaluate-response.json", cache_eval)
    cache_eval_body = cache_eval.get("body") if isinstance(cache_eval.get("body"), dict) else {}
    cache_state = str(field(cache_eval_body, "state") or "").lower()
    steps.append(step(
        "validated_cache_lifecycle",
        "PASS" if cache_upsert["ok"] and cache_state == "eligible" else ("FAIL" if cache_upsert["ok"] and cache_eval["ok"] else "UNKNOWN"),
        "Validated cache record was upserted and evaluated as eligible." if cache_upsert["ok"] and cache_state == "eligible" else f"Validated cache lifecycle was not proven; evaluation state is {cache_state or 'unknown'}.",
        ["validated-cache-upsert-request.json", "validated-cache-upsert-response.json", "validated-cache-evaluate-response.json"],
    ))

    ledger_export = request_with_auth_retry(
        "GET",
        query_url(args.ledger_export_url, trace_id=trace_id, page=1, page_size=200),
        None,
        args,
        headers,
        auth,
        run_dir,
    )
    artifact_response(run_dir / "ledger-export-response.json", ledger_export)
    ledger_verify = request_with_auth_retry(
        "GET",
        query_url(args.ledger_verify_url, trace_id=trace_id),
        None,
        args,
        headers,
        auth,
        run_dir,
    )
    artifact_response(run_dir / "ledger-verify-response.json", ledger_verify)
    ledger_export_body = ledger_export.get("body") if isinstance(ledger_export.get("body"), dict) else {}
    ledger_verify_body = ledger_verify.get("body") if isinstance(ledger_verify.get("body"), dict) else {}
    ledger_total = int(field(ledger_export_body, "total") or 0)
    ledger_valid_value = field(ledger_verify_body, "is_valid")
    if ledger_export["ok"] and ledger_verify["ok"] and ledger_total > 0 and ledger_valid_value is True:
        ledger_status = "PASS"
        ledger_basis = "Ledger export returned trace-scoped events and hash-chain verification reported valid."
    elif ledger_export["ok"] and ledger_verify["ok"] and ledger_valid_value is False:
        ledger_status = "FAIL"
        ledger_basis = "Ledger verification endpoint reported an invalid hash chain."
    else:
        ledger_status = "UNKNOWN"
        ledger_basis = "Ledger endpoints responded, but trace-scoped append/hash-chain proof was incomplete."
    steps.append(step(
        "ledger_audit_chain",
        ledger_status,
        ledger_basis,
        ["ledger-export-response.json", "ledger-verify-response.json"],
    ))
    return steps


def dry_run_steps(suite: dict) -> list[dict]:
    return [
        step(step_id, "NOT_RUN", "Dry run only; no live structural endpoint was called.")
        for step_id in suite["required_steps"]
    ]


def summarize(
    suite: dict,
    steps: list[dict],
    args: argparse.Namespace,
    run: str,
    run_dir: Path,
    started: dt.datetime,
    auth: dict[str, object],
) -> dict:
    counts = {status: sum(1 for item in steps if item["status"] == status) for status in ("PASS", "FAIL", "UNKNOWN", "NOT_RUN")}
    grade = grade_summary(counts, steps, args.execute, auth)
    generated = now_utc()
    return {
        "schema_version": "cwi.structural_recursive_summary.v1",
        "suite_id": suite["suite_id"],
        "run_id": run,
        "mode": "execute" if args.execute else "dry_run",
        "run_started_utc": iso_utc(started),
        "run_started_utc_display": utc_display(started),
        "run_started_local_display": local_display(started),
        "generated_utc": iso_utc(generated),
        "generated_utc_display": utc_display(generated),
        "generated_local_display": local_display(generated),
        "tenant_configured": bool(args.tenant_id),
        "auth": auth,
        "counts": counts,
        "report_validity": grade["report_validity"],
        "report_validity_basis": grade["report_validity_basis"],
        "structural_grade": grade,
        "steps": steps,
        "report_path": str(run_dir / "report.md"),
    }


def grade_summary(counts: dict[str, int], steps: list[dict], executed: bool, auth: dict[str, object]) -> dict:
    if not executed:
        return {
            "grade": "INVALID",
            "score": None,
            "assessment": "INVALID_REPORT_NOT_EXECUTED",
            "evidence_confidence": "none",
            "report_validity": "INVALID_NOT_EXECUTED",
            "report_validity_basis": "Dry run artifacts are plans, not structural evidence.",
            "basis": "No live structural endpoints were called.",
        }
    if auth.get("status") == "FAIL":
        return {
            "grade": "INVALID",
            "score": None,
            "assessment": "INVALID_AUTH_FAILED",
            "evidence_confidence": "none",
            "report_validity": "INVALID_AUTH_FAILED",
            "report_validity_basis": "Authentication failed before structural validation could execute.",
            "basis": str(auth.get("detail", "Authentication failed.")),
        }
    if counts["FAIL"] > 0:
        return {
            "grade": "F",
            "score": 0,
            "assessment": "STRUCTURAL_CHAIN_FAILED",
            "evidence_confidence": "high",
            "report_validity": "VALID_STRUCTURAL_DIAGNOSTIC",
            "report_validity_basis": "Live structural validation executed and found failing required steps.",
            "basis": f"{counts['FAIL']} structural step(s) failed.",
        }
    critical = {
        "recursive_event_ingest",
        "recursive_event_query",
        "graph_candidate_created",
        "graph_vector_refresh_job_created",
        "quality_observation_ingest",
        "response_quality_graph_update",
        "federated_summary_observed",
    }
    critical_pass = all(
        any(item["step_id"] == step_id and item["status"] == "PASS" for item in steps)
        for step_id in critical
    )
    if counts["UNKNOWN"] == 0 and counts["NOT_RUN"] == 0 and critical_pass:
        return {
            "grade": "A-",
            "score": 92,
            "assessment": "STRUCTURAL_CHAIN_PROVEN_FOR_CONTROLLED_PILOT",
            "evidence_confidence": "high",
            "report_validity": "VALID_STRUCTURAL_EVIDENCE",
            "report_validity_basis": "All structural validation steps produced PASS evidence.",
            "basis": "All required structural steps passed.",
        }
    if critical_pass:
        return {
            "grade": "B-",
            "score": 82,
            "assessment": "CORE_STRUCTURAL_CHAIN_PRESENT_WITH_UNPROVEN_CLOSED_LOOP_STEPS",
            "evidence_confidence": "medium",
            "report_validity": "VALID_STRUCTURAL_DIAGNOSTIC",
            "report_validity_basis": "Live structural validation executed; PASS/UNKNOWN results identify proven steps and remaining gaps.",
            "basis": "Core event, graph candidate, vector job, quality graph, and federated summary paths passed; closed-loop proof still has UNKNOWN steps.",
        }
    return {
        "grade": "C",
        "score": 70,
        "assessment": "PARTIAL_STRUCTURAL_EVIDENCE",
        "evidence_confidence": "medium",
        "report_validity": "VALID_STRUCTURAL_DIAGNOSTIC",
        "report_validity_basis": "Live structural validation executed, but core chain proof is incomplete.",
        "basis": "No hard failures were recorded, but one or more core structural steps did not pass.",
    }


def render_report(summary: dict) -> str:
    grade = summary["structural_grade"]
    counts = summary["counts"]
    lines = [
        "# Structural Recursive Intelligence E2E Report",
        "",
        f"- Run ID: `{summary['run_id']}`",
        f"- Run started: `{summary['run_started_local_display']}`",
        f"- Run started UTC: `{summary['run_started_utc_display']}`",
        f"- Report generated: `{summary['generated_local_display']}`",
        f"- Report generated UTC: `{summary['generated_utc_display']}`",
        f"- Suite: `{summary['suite_id']}`",
        f"- Mode: `{summary['mode']}`",
        f"- Auth status: `{summary.get('auth', {}).get('status', 'UNKNOWN')}`",
        f"- Report validity: `{summary['report_validity']}`",
        f"- Validity basis: {summary['report_validity_basis']}",
        f"- PASS: `{counts['PASS']}`",
        f"- FAIL: `{counts['FAIL']}`",
        f"- UNKNOWN: `{counts['UNKNOWN']}`",
        f"- NOT RUN: `{counts['NOT_RUN']}`",
        f"- Structural grade: `{grade['grade']}`",
        f"- Grade score: `{grade['score'] if grade['score'] is not None else 'not scored'}`",
        f"- Working assessment: `{grade['assessment']}`",
        f"- Evidence confidence: `{grade['evidence_confidence']}`",
        "",
        "## Grade",
        "",
        f"- Basis: {grade['basis']}",
        "- Limitation: This report is structural evidence only for steps directly executed or queried by this runner. UNKNOWN means the step was not independently proven.",
        "",
        "## Step Results",
        "",
        "| Step | Status | Basis | Evidence |",
        "|---|---:|---|---|",
    ]
    for item in summary["steps"]:
        evidence = ", ".join(f"`{ref}`" for ref in item.get("evidence_refs", []))
        lines.append(f"| `{item['step_id']}` | `{item['status']}` | {item['basis']} | {evidence} |")
    lines.extend([
        "",
        "## Interpretation",
        "",
        "- `PASS` means the runner observed direct evidence for that step.",
        "- `FAIL` means the step executed and contradicted the expected structural behavior.",
        "- `UNKNOWN` means execution produced useful diagnostics, but not enough evidence to prove the step.",
        "- `NOT_RUN` means the step was skipped or this was a dry run.",
        "",
    ])
    return "\n".join(lines)


def update_latest(summary: dict, output_root: Path, run_dir: Path) -> None:
    latest = {
        "run_id": summary["run_id"],
        "mode": summary["mode"],
        "report_validity": summary["report_validity"],
        "grade": summary["structural_grade"]["grade"],
        "assessment": summary["structural_grade"]["assessment"],
        "counts": summary["counts"],
        "report_path": str(run_dir / "report.md"),
        "generated_utc": summary["generated_utc"],
    }
    write_json(output_root / "latest-structural-run.json", latest)
    write_text(
        output_root / "latest-structural-run.md",
        "\n".join([
            "# Latest Structural Recursive Intelligence Run",
            "",
            f"- Run ID: `{latest['run_id']}`",
            f"- Mode: `{latest['mode']}`",
            f"- Report validity: `{latest['report_validity']}`",
            f"- Grade: `{latest['grade']}`",
            f"- Assessment: `{latest['assessment']}`",
            f"- PASS / FAIL / UNKNOWN / NOT RUN: `{latest['counts']['PASS']} / {latest['counts']['FAIL']} / {latest['counts']['UNKNOWN']} / {latest['counts']['NOT_RUN']}`",
            f"- Report: `{latest['report_path']}`",
            "",
        ]),
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Structural recursive intelligence E2E runner")
    parser.add_argument("--suite", default=str(DEFAULT_SUITE))
    parser.add_argument("--output-root", default=str(DEFAULT_OUTPUT_ROOT))
    parser.add_argument("--execute", action="store_true", help="Call live structural endpoints. Dry run is the default.")
    parser.add_argument("--base-url", default=os.environ.get("CWI_BASE_URL", ""))
    parser.add_argument("--recursive-events-url", default=os.environ.get("CWI_RECURSIVE_EVENTS_URL", ""))
    parser.add_argument("--graph-candidates-url", default=os.environ.get("CWI_GRAPH_CANDIDATES_URL", ""))
    parser.add_argument("--vector-jobs-url", default=os.environ.get("CWI_GRAPH_VECTOR_REFRESH_JOBS_URL", ""))
    parser.add_argument("--vector-process-url", default=os.environ.get("CWI_GRAPH_VECTOR_REFRESH_PROCESS_URL", ""))
    parser.add_argument("--response-quality-graph-url", default=os.environ.get("CWI_RESPONSE_QUALITY_GRAPH_URL", ""))
    parser.add_argument("--external-opinion-url", default=os.environ.get("CWI_EXTERNAL_OPINION_URL", ""))
    parser.add_argument("--federated-summary-url", default=os.environ.get("CWI_FEDERATED_SUMMARY_URL", ""))
    parser.add_argument("--validated-cache-url", default=os.environ.get("CWI_VALIDATED_CACHE_URL", ""))
    parser.add_argument("--ledger-export-url", default=os.environ.get("CWI_LEDGER_EXPORT_URL", ""))
    parser.add_argument("--ledger-verify-url", default=os.environ.get("CWI_LEDGER_VERIFY_URL", ""))
    parser.add_argument("--ask-url", default=os.environ.get("CWI_RECURSIVE_ASK_URL", ""))
    parser.add_argument("--tenant-id", default=os.environ.get("CWI_STRUCTURAL_TENANT_ID") or os.environ.get("CWI_IDENTITY_TENANT_ID") or "admin")
    parser.add_argument("--node-id", default=os.environ.get("CWI_STRUCTURAL_NODE_ID", "structural-validation-node"))
    parser.add_argument("--user-id-hash", default=os.environ.get("CWI_STRUCTURAL_USER_ID_HASH", ""))
    parser.add_argument("--pack-name", default=os.environ.get("CWI_PACK_NAME", "personal"))
    parser.add_argument("--policy-mode", default=os.environ.get("CWI_POLICY_MODE", "observe"))
    parser.add_argument("--top-k", type=int, default=int(os.environ.get("CWI_TOP_K", "5")))
    parser.add_argument("--auth-token", default=os.environ.get("CWI_AUTH_TOKEN", ""))
    parser.add_argument("--auth-mode", choices=["auto", "identity", "token", "none"], default=os.environ.get("CWI_AUTH_MODE", "auto"))
    parser.add_argument("--identity-login-url", default=resolve_identity_login_url())
    parser.add_argument("--identity-username", default=os.environ.get("CWI_IDENTITY_USERNAME") or os.environ.get("IDENTITY_SUPERADMIN_USERNAME", ""))
    parser.add_argument("--identity-email", default=os.environ.get("CWI_IDENTITY_EMAIL", ""))
    parser.add_argument("--identity-tenant-id", default=os.environ.get("CWI_IDENTITY_TENANT_ID", ""))
    parser.add_argument("--identity-password-env", default=os.environ.get("CWI_IDENTITY_PASSWORD_ENV", "CWI_IDENTITY_PASSWORD"))
    parser.add_argument("--timeout-seconds", type=float, default=float(os.environ.get("CWI_HTTP_TIMEOUT_SECONDS", "30")))
    args = parser.parse_args()
    args.recursive_events_url = resolve_url(args.recursive_events_url, args.base_url, "/api/recursive/events")
    args.graph_candidates_url = resolve_url(args.graph_candidates_url, args.base_url, "/api/admin/graph-update-candidates")
    args.vector_jobs_url = resolve_url(args.vector_jobs_url, args.base_url, "/api/admin/graph-vector-refresh-jobs")
    args.vector_process_url = resolve_url(args.vector_process_url, args.base_url, "/api/admin/graph-vector-refresh-jobs/process-pending")
    args.response_quality_graph_url = resolve_url(args.response_quality_graph_url, args.base_url, "/api/admin/response-quality-graph")
    args.external_opinion_url = resolve_url(args.external_opinion_url, args.base_url, "/api/admin/external-opinion/evaluations")
    args.federated_summary_url = resolve_url(args.federated_summary_url, args.base_url, "/api/admin/federated-learning/summaries")
    args.validated_cache_url = resolve_url(args.validated_cache_url, args.base_url, "/api/admin/validated-cache")
    args.ledger_export_url = resolve_url(args.ledger_export_url, args.base_url, "/api/ledger/export")
    args.ledger_verify_url = resolve_url(args.ledger_verify_url, args.base_url, "/api/ledger/verify")
    args.ask_url = resolve_url(args.ask_url, args.base_url, "/api/ask")
    if not args.user_id_hash:
        args.user_id_hash = sha256("structural-validation-user")[:32]
    return args


def main() -> int:
    args = parse_args()
    suite = load_json(Path(args.suite))
    started = now_utc()
    run = run_id(started)
    output_root = Path(args.output_root)
    run_dir = output_root / run
    run_dir.mkdir(parents=True, exist_ok=True)
    write_json(run_dir / "suite.json", suite)

    required_urls = [
        args.recursive_events_url,
        args.graph_candidates_url,
        args.vector_jobs_url,
        args.vector_process_url,
        args.response_quality_graph_url,
        args.external_opinion_url,
        args.federated_summary_url,
        args.validated_cache_url,
        args.ledger_export_url,
        args.ledger_verify_url,
    ]
    if args.execute and any(not url for url in required_urls):
        print("Base URL or required structural endpoint URLs are missing.", file=sys.stderr)
        return 2

    if args.execute:
        headers, auth = authenticate(args, run_dir)
        if auth.get("status") == "PASS" or args.auth_mode == "none":
            steps = execute(args, suite, run, run_dir, headers, auth)
        else:
            steps = [step(step_id, "NOT_RUN", "Skipped because authentication failed.") for step_id in suite["required_steps"]]
    else:
        auth = {
            "mode": args.auth_mode,
            "status": "NOT_RUN",
            "detail": "Dry run did not authenticate.",
            "access_token_received": False,
        }
        write_json(run_dir / "auth-status.json", auth)
        steps = dry_run_steps(suite)

    summary = summarize(suite, steps, args, run, run_dir, started, auth)
    write_json(run_dir / "summary.json", summary)
    write_text(run_dir / "report.md", render_report(summary))
    update_latest(summary, output_root, run_dir)

    print(f"Structural recursive report: {run_dir / 'report.md'}")
    if args.execute and summary["counts"]["FAIL"] > 0:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
