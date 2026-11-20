#!/usr/bin/env python3
"""Run or initialize the controlled recursive ASK E2E suite.

The runner is intentionally stdlib-only so it can create evidence artifacts in
minimal CI shells. By default it performs a dry run and writes NOT_RUN tracking
records. Pass --execute to call the configured ASK and recursive-event URLs.
"""

from __future__ import annotations

import argparse
import copy
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_SUITE = SCRIPT_DIR / "controlled-ask-e2e-suite.v1.json"
DEFAULT_OUTPUT_ROOT = REPO_ROOT / "testing" / "runs" / "recursive-intelligence"
REQUIRED_TRACE_FIELDS = {"sources", "confidence", "policy_mode", "model", "pack_hash"}
STRUCTURAL_TRACE_CHECKS = {
    "learning_trace_governed_memory_update": "Learning ASK trace shows governed memory update",
    "post_learning_trace_governed_memory_recall": "Post-learning ASK trace shows governed memory recall",
    "post_learning_memory_source_grounding": "Post-learning answers are grounded in memory sources",
    "truth_engine_trace_signal": "Truth Engine trace fields are present",
    "response_framework_trace_signal": "Response framework overlay trace fields are present",
    "provenance_trace_consistency": "Provenance trace and response trace are consistent",
    "pack_trust_trace_signal": "Pack trust hash signals are present in response trace",
}
SUCCESS_CODES = {200, 201, 202}
AUTH_REFRESH_CODES = {401, 403}
REDACTED = "<redacted>"
BASE_SENSITIVE_KEYS = {
    "access_token",
    "refresh_token",
    "token",
    "password",
    "authorization",
    "tenant_id",
    "user_id",
    "sub",
    "email",
    "username",
}
RESPONSE_SENSITIVE_KEYS = BASE_SENSITIVE_KEYS | {
    "home_node_id",
    "node_id",
    "watcher_node_id",
    "owner_user_id",
    "home_node_owner_user_id",
    "tailscale_ip",
    "home_node_tailscale_ip",
    "home_node_membership_last_seen_utc",
    "path",
}
DEFAULT_DEFERRED_POLL_SECONDS = 120.0
DEFAULT_HTTP_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)


def utc_now() -> str:
    return iso_utc(dt.datetime.now(dt.timezone.utc))


def iso_utc(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def utc_display(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")


def local_display(value: dt.datetime) -> str:
    local = value.astimezone()
    offset = local.strftime("%z")
    formatted_offset = f"{offset[:3]}:{offset[3:]}" if offset else "+00:00"
    timezone_name = local.tzname() or "local"
    return f"{local.strftime('%Y-%m-%d %H:%M:%S')} {timezone_name} (UTC{formatted_offset})"


def run_sort_key(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).strftime("%Y-%m-%d_%H-%M-%S_UTC")


def run_id(value: dt.datetime | None = None) -> str:
    started = value or dt.datetime.now(dt.timezone.utc)
    stamp = run_sort_key(started)
    suffix = hashlib.sha256(stamp.encode("utf-8")).hexdigest()[:8]
    return f"{stamp}-{suffix}"


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def namespace_suite_entities(suite: dict, namespace: str) -> dict:
    normalized_namespace = re.sub(r"[^0-9]", "", namespace or "")
    if not normalized_namespace:
        return suite

    namespaced = copy.deepcopy(suite)
    for case in namespaced.get("cases", []):
        original = str(case.get("entity_token", ""))
        match = re.fullmatch(r"CWI-RL-(\d{3,})", original, flags=re.IGNORECASE)
        if not match:
            continue

        replacement = f"CWI-RL-{normalized_namespace}{match.group(1)}"
        case["entity_token"] = replacement
        replace_case_token(case, original, replacement)
    namespaced["entity_namespace"] = normalized_namespace
    return namespaced


def replace_case_token(value: object, original: str, replacement: str) -> object:
    if isinstance(value, dict):
        for key, item in list(value.items()):
            value[key] = replace_case_token(item, original, replacement)
        return value
    if isinstance(value, list):
        for index, item in enumerate(value):
            value[index] = replace_case_token(item, original, replacement)
        return value
    if isinstance(value, str):
        return value.replace(original, replacement)
    return value


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def write_request_json(path: Path, payload: object) -> None:
    write_json(path, redact_sensitive(payload, BASE_SENSITIVE_KEYS))


def write_response_json(path: Path, payload: object) -> None:
    write_json(path, sanitize_response_artifact(payload))


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def append_ndjson(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, sort_keys=True) + "\n")


def relative_artifact_path(path: Path, base: Path) -> str:
    try:
        return path.relative_to(base).as_posix()
    except ValueError:
        return path.as_posix()


def resolve_url(primary_env: str, fallback_env: str | None, suffix: str) -> str:
    primary = os.environ.get(primary_env, "").strip()
    if primary:
        return primary.rstrip("/")
    if fallback_env:
        fallback = os.environ.get(fallback_env, "").strip()
        if fallback:
            return fallback.rstrip("/") + suffix
    return ""


def resolve_identity_login_url() -> str:
    explicit = os.environ.get("CWI_IDENTITY_LOGIN_URL", "").strip()
    if explicit:
        return explicit.rstrip("/")
    for base_env in ("CWI_IDENTITY_BASE_URL", "IDENTITY_URL", "CWI_BASE_URL"):
        base = os.environ.get(base_env, "").strip()
        if base:
            return base.rstrip("/") + "/auth/login"
    return ""


def post_json(url: str, payload: dict, headers: dict[str, str], timeout: float) -> dict:
    body = json.dumps(payload).encode("utf-8")
    request_headers = {
        "User-Agent": os.environ.get("CWI_HTTP_USER_AGENT", DEFAULT_HTTP_USER_AGENT),
        "Content-Type": "application/json",
        "Accept": "application/json",
        **headers,
    }
    request = urllib.request.Request(
        url,
        data=body,
        headers=request_headers,
        method="POST",
    )
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
        return {
            "ok": False,
            "status_code": exc.code,
            "body": parse_json(raw),
            "raw": raw,
        }
    except urllib.error.URLError as exc:
        return {"ok": False, "status_code": 0, "body": None, "raw": str(exc.reason)}
    except TimeoutError as exc:
        return {"ok": False, "status_code": 0, "body": None, "raw": str(exc)}


def get_json(url: str, headers: dict[str, str], timeout: float) -> dict:
    request_headers = {
        "User-Agent": os.environ.get("CWI_HTTP_USER_AGENT", DEFAULT_HTTP_USER_AGENT),
        "Accept": "application/json",
        **headers,
    }
    request = urllib.request.Request(url, headers=request_headers, method="GET")
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
        return {
            "ok": False,
            "status_code": exc.code,
            "body": parse_json(raw),
            "raw": raw,
        }
    except urllib.error.URLError as exc:
        return {"ok": False, "status_code": 0, "body": None, "raw": str(exc.reason)}
    except TimeoutError as exc:
        return {"ok": False, "status_code": 0, "body": None, "raw": str(exc)}


def parse_json(raw: str) -> object:
    if not raw:
        return None
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return None


def redact_sensitive(value: object, sensitive_keys: set[str] | None = None) -> object:
    sensitive = sensitive_keys or BASE_SENSITIVE_KEYS
    if isinstance(value, dict):
        return {
            key: REDACTED if key.lower() in sensitive else redact_sensitive(item, sensitive)
            for key, item in value.items()
        }
    if isinstance(value, list):
        return [redact_sensitive(item, sensitive) for item in value]
    return value


def sanitize_response_artifact(value: object) -> object:
    sanitized = redact_sensitive(value, RESPONSE_SENSITIVE_KEYS)
    if isinstance(sanitized, dict) and "raw" in sanitized and "body" in sanitized:
        body = sanitized.get("body")
        if isinstance(body, (dict, list)):
            sanitized["raw"] = json.dumps(body, sort_keys=True)
    return sanitized


def identity_password(args: argparse.Namespace) -> str:
    candidates = [
        args.identity_password_env,
        "CWI_IDENTITY_PASSWORD",
        "IDENTITY_SUPERADMIN_PASSWORD",
    ]
    for name in dict.fromkeys(name for name in candidates if name):
        value = os.environ.get(name, "")
        if value:
            return value
    return ""


def identity_credentials_configured(args: argparse.Namespace) -> bool:
    return bool(
        args.identity_login_url
        and identity_password(args)
        and (args.identity_username or args.identity_email)
    )


def identity_login_payload(args: argparse.Namespace) -> dict:
    payload = {"password": identity_password(args)}
    if args.identity_tenant_id:
        payload["tenant_id"] = args.identity_tenant_id
    if args.identity_email:
        payload["email"] = args.identity_email
    if args.identity_username:
        payload["username"] = args.identity_username
    return payload


def authenticate(args: argparse.Namespace, run_dir: Path) -> tuple[dict[str, str], dict[str, object]]:
    headers: dict[str, str] = {}
    auth = {
        "mode": args.auth_mode,
        "status": "NOT_RUN",
        "detail": "No authentication was configured.",
        "identity_login_configured": bool(args.identity_login_url),
        "identity_principal_configured": bool(args.identity_username or args.identity_email),
        "identity_tenant_configured": bool(args.identity_tenant_id),
        "identity_password_env": args.identity_password_env,
        "access_token_received": False,
    }

    if args.auth_mode == "none":
        auth["detail"] = "Authentication disabled by --auth-mode none."
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth

    if args.auth_mode in ("auto", "token") and args.auth_token:
        headers["Authorization"] = f"Bearer {args.auth_token}"
        auth.update({
            "mode": "token",
            "status": "PASS",
            "detail": "Bearer token supplied from environment/argument and applied to ASK calls.",
            "access_token_received": True,
        })
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth

    if args.auth_mode == "token":
        auth.update({
            "status": "FAIL",
            "detail": "Token auth was requested, but CWI_AUTH_TOKEN/--auth-token was empty.",
        })
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth

    if not identity_credentials_configured(args):
        auth.update({
            "status": "FAIL" if args.auth_mode == "identity" else "NOT_RUN",
            "detail": (
                "Identity login was requested, but login URL, username/email, or password environment variable is missing."
                if args.auth_mode == "identity"
                else "Identity login was skipped because login URL, username/email, or password environment variable is missing."
            ),
        })
        write_json(run_dir / "auth-status.json", auth)
        return headers, auth

    response = post_json(
        args.identity_login_url,
        identity_login_payload(args),
        {},
        args.timeout_seconds,
    )
    token = ""
    body = response.get("body")
    if isinstance(body, dict):
        token_value = body.get("access_token")
        token = token_value if isinstance(token_value, str) else ""

    if token:
        headers["Authorization"] = f"Bearer {token}"
        auth.update({
            "mode": "identity",
            "status": "PASS",
            "detail": "Identity login succeeded and bearer token was applied to ASK calls.",
            "status_code": response["status_code"],
            "access_token_received": True,
            "waf_blocked": False,
        })
    else:
        auth.update({
            "mode": "identity",
            "status": "FAIL",
            "detail": "Identity login did not return an access_token.",
            "status_code": response["status_code"],
            "access_token_received": False,
            "waf_blocked": response_is_waf_blocked(response),
            "body": redact_sensitive(body),
        })
    write_json(run_dir / "auth-status.json", auth)
    return headers, auth


def refresh_identity_auth(
    args: argparse.Namespace,
    headers: dict[str, str],
    auth: dict[str, object],
    run_dir: Path,
) -> bool:
    if auth.get("mode") != "identity" or not identity_credentials_configured(args):
        return False

    response = post_json(
        args.identity_login_url,
        identity_login_payload(args),
        {},
        args.timeout_seconds,
    )
    refresh_count = int(auth.get("refresh_count") or 0) + 1
    auth["refresh_count"] = refresh_count
    auth["last_refresh_status_code"] = response["status_code"]

    token = ""
    body = response.get("body")
    if isinstance(body, dict):
        token_value = body.get("access_token")
        token = token_value if isinstance(token_value, str) else ""

    if token:
        headers["Authorization"] = f"Bearer {token}"
        auth.update({
            "status": "PASS",
            "detail": "Identity login succeeded and bearer token was applied to ASK calls. Token was refreshed during the run.",
            "access_token_received": True,
            "waf_blocked": False,
            "last_refresh_succeeded": True,
        })
        write_json(run_dir / "auth-status.json", auth)
        return True

    auth.update({
        "last_refresh_succeeded": False,
        "last_refresh_waf_blocked": response_is_waf_blocked(response),
        "last_refresh_body": redact_sensitive(body),
    })
    write_json(run_dir / "auth-status.json", auth)
    return False


def should_refresh_auth(response: dict) -> bool:
    return response.get("status_code") in AUTH_REFRESH_CODES and not response_is_waf_blocked(response)


def post_json_with_auth_retry(
    url: str,
    payload: dict,
    args: argparse.Namespace,
    headers: dict[str, str],
    auth: dict[str, object],
    run_dir: Path,
) -> dict:
    response = post_json(url, payload, headers, args.timeout_seconds)
    if should_refresh_auth(response) and refresh_identity_auth(args, headers, auth, run_dir):
        retry = post_json(url, payload, headers, args.timeout_seconds)
        retry["auth_refreshed"] = True
        retry["auth_retry_after_status_code"] = response.get("status_code")
        return retry
    return response


def get_json_with_auth_retry(
    url: str,
    args: argparse.Namespace,
    headers: dict[str, str],
    auth: dict[str, object],
    run_dir: Path,
) -> dict:
    response = get_json(url, headers, args.timeout_seconds)
    if should_refresh_auth(response) and refresh_identity_auth(args, headers, auth, run_dir):
        retry = get_json(url, headers, args.timeout_seconds)
        retry["auth_refreshed"] = True
        retry["auth_retry_after_status_code"] = response.get("status_code")
        return retry
    return response


def answer_text(response_body: object) -> str:
    if not isinstance(response_body, dict):
        return ""
    for key in ("answer", "answer_markdown", "response", "text"):
        value = response_body.get(key)
        if isinstance(value, str) and value.strip():
            return value
    return json.dumps(response_body, sort_keys=True)


def is_pending_home_node_response(response_body: object) -> bool:
    if not isinstance(response_body, dict):
        return False
    execution_state = str(response_body.get("execution_state") or "").lower()
    execution_target = str(response_body.get("execution_target") or "").lower()
    deferred_work_id = response_body.get("deferred_work_id")
    if not deferred_work_id and isinstance(response_body.get("trace"), dict):
        deferred_work_id = response_body["trace"].get("deferred_work_id")
    return (
        execution_state == "pending"
        and execution_target == "home_node"
        and isinstance(deferred_work_id, str)
        and bool(deferred_work_id.strip())
    )


def deferred_work_id(response_body: object) -> str:
    if not isinstance(response_body, dict):
        return ""
    value = response_body.get("deferred_work_id")
    if isinstance(value, str) and value.strip():
        return value.strip()
    trace = response_body.get("trace")
    if isinstance(trace, dict):
        value = trace.get("deferred_work_id")
        if isinstance(value, str) and value.strip():
            return value.strip()
    return ""


def request_id(response_body: object) -> str:
    if not isinstance(response_body, dict):
        return ""
    for container_name in ("provenance", "trace"):
        container = response_body.get(container_name)
        if isinstance(container, dict):
            value = container.get("request_id")
            if isinstance(value, str) and value.strip():
                return value.strip()
    value = response_body.get("request_id")
    return value.strip() if isinstance(value, str) else ""


def trace_payload(response_body: object) -> dict:
    if not isinstance(response_body, dict):
        return {}
    trace = response_body.get("trace")
    return trace if isinstance(trace, dict) else {}


def has_all_terms(text: str, terms: list[str]) -> bool:
    haystack = text.lower()
    return all(term.lower() in haystack for term in terms)


def has_any_term(text: str, terms: list[str]) -> bool:
    if not terms:
        return True
    haystack = text.lower()
    return any(term.lower() in haystack for term in terms)


def matching_forbidden_terms(text: str, terms: list[str]) -> list[str]:
    haystack = text.lower()
    return [term for term in terms if term.lower() in haystack]


def trace_has_required_fields(trace: dict) -> bool:
    if not REQUIRED_TRACE_FIELDS.issubset(trace):
        return False
    return (
        isinstance(trace.get("sources"), list)
        and isinstance(trace.get("confidence"), (int, float))
        and isinstance(trace.get("policy_mode"), str)
        and isinstance(trace.get("model"), str)
        and isinstance(trace.get("pack_hash"), str)
    )


def response_body(response: dict) -> object:
    return response.get("body")


def provenance_payload(response_body: object) -> dict:
    if not isinstance(response_body, dict):
        return {}
    provenance = response_body.get("provenance")
    return provenance if isinstance(provenance, dict) else {}


def lower_value(container: dict, key: str) -> str:
    value = container.get(key)
    return value.strip().lower() if isinstance(value, str) else ""


def bool_value(container: dict, key: str) -> bool:
    return container.get(key) is True


def list_value(container: dict, key: str) -> list:
    value = container.get(key)
    return value if isinstance(value, list) else []


def is_sha256_hex(value: object) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-fA-F]{64}", value.strip()) is not None


def governed_memory_update_trace(response: dict) -> bool:
    body = response_body(response)
    if not isinstance(body, dict):
        return False
    trace = trace_payload(body)
    return (
        lower_value(body, "execution_target") == "governed_memory"
        and lower_value(body, "intent_mode") == "memory_update"
        and lower_value(trace, "grounding_mode") == "memory_update"
        and lower_value(trace, "confidence_basis") == "memory_update_confirmed"
    )


def governed_memory_recall_trace(response: dict) -> bool:
    body = response_body(response)
    if not isinstance(body, dict):
        return False
    trace = trace_payload(body)
    return (
        lower_value(body, "execution_target") == "governed_memory"
        and lower_value(body, "intent_mode") == "memory_recall"
        and lower_value(trace, "grounding_mode") in {"source_grounded", "memory_policy_state"}
    )


def memory_source_grounding_trace(response: dict) -> bool:
    body = response_body(response)
    if not isinstance(body, dict):
        return False
    trace = trace_payload(body)
    sources = list_value(body, "sources") + list_value(trace, "sources")
    return bool_value(trace, "memory_used") and any(
        isinstance(source, str) and source.startswith("memory:")
        for source in sources
    )


def truth_engine_trace_signal(response: dict) -> bool:
    body = response_body(response)
    if not isinstance(body, dict):
        return False
    trace = trace_payload(body)
    return (
        isinstance(trace.get("truth_engine_version"), str)
        and isinstance(trace.get("truth_engine_mode"), str)
        and isinstance(trace.get("truth_state"), str)
        and isinstance(trace.get("truth_evidence_count"), (int, float))
    )


def response_framework_trace_signal(response: dict) -> bool:
    body = response_body(response)
    if not isinstance(body, dict):
        return False
    trace = trace_payload(body)
    return (
        bool_value(trace, "communication_runtime_applied")
        and isinstance(trace.get("memory_framework_version"), str)
        and isinstance(trace.get("comprehension_expression_version"), str)
        and isinstance(trace.get("presentation_framework_version"), str)
    )


def provenance_trace_consistency(response: dict) -> bool:
    body = response_body(response)
    if not isinstance(body, dict):
        return False
    trace = trace_payload(body)
    provenance = provenance_payload(body)
    trace_id = trace.get("trace_id")
    provenance_trace_id = provenance.get("trace_id")
    trace_pack_hash = trace.get("pack_hash")
    provenance_pack_hash = provenance.get("pack_hash")
    return (
        isinstance(trace_id, str)
        and trace_id.strip()
        and trace_id == provenance_trace_id
        and is_sha256_hex(trace_pack_hash)
        and trace_pack_hash == provenance_pack_hash
    )


def pack_trust_trace_signal(response: dict) -> bool:
    body = response_body(response)
    if not isinstance(body, dict):
        return False
    trace = trace_payload(body)
    baseline_packs = list_value(trace, "baseline_packs")
    domain_packs = list_value(trace, "domain_packs")
    return (
        is_sha256_hex(trace.get("pack_hash"))
        and bool(baseline_packs)
        and all(isinstance(pack, dict) and is_sha256_hex(pack.get("pack_hash")) for pack in baseline_packs)
        and bool(domain_packs)
    )


def response_is_waf_blocked(response: dict) -> bool:
    body = response.get("body")
    raw = str(response.get("raw") or "").lower()
    if isinstance(body, dict):
        if body.get("cloudflare_error") is True:
            return True
        if body.get("error_code") == 1010:
            return True
        if str(body.get("error_name") or "").lower() == "browser_signature_banned":
            return True
    return response.get("status_code") == 403 and "cloudflare" in raw


def ask_payload(case: dict, prompt: str, args: argparse.Namespace, conversation_id: str) -> dict:
    payload = {
        "question": prompt,
        "pack": {"name": args.pack_name},
        "context": {
            "mode": args.policy_mode,
            "top_k": args.top_k,
            "conversation_id": conversation_id,
            "recursive_case_id": case["case_id"],
        },
    }
    if args.tenant_id:
        payload["tenant_id"] = args.tenant_id
    if args.user_id:
        payload["user_id"] = args.user_id
    return payload


def event_payload(case: dict, event_text: str, event_index: int, args: argparse.Namespace, run: str) -> dict:
    created = utc_now()
    source_hash = hashlib.sha256(event_text.encode("utf-8")).hexdigest()
    event_id = f"{case['case_id'].lower()}-{event_index}-{run}"
    return {
        "schema_version": "recursive-event.v1",
        "event_id": event_id,
        "event_type": "controlled_fact_learning",
        "tenant_id": args.tenant_id,
        "user_scope": {"scope_type": "user", "user_id": args.user_id},
        "source": {
            "component": "controlled-ask-e2e",
            "source_type": "process_miner",
            "source_id": f"{case['case_id']}:{event_index}",
        },
        "confidence": 0.99,
        "trace_id": f"{run}:{case['case_id']}",
        "policy_status": "observe_only",
        "timestamp_utc": created,
        "temporal_state": "current",
        "payload": {
            "suite_id": args.suite_id,
            "case_id": case["case_id"],
            "category": case["category"],
            "entity_token": case["entity_token"],
            "controlled_fact": event_text,
        },
        "provenance": {
            "source_hash": source_hash,
            "created_utc": created,
            "actor": "controlled-ask-e2e-runner",
        },
    }


def resolve_deferred_url(args: argparse.Namespace, work_id: str, req_id: str) -> str:
    base = args.deferred_url.rstrip("/")
    query = urllib.parse.urlencode({"request_id": req_id})
    return f"{base}/{urllib.parse.quote(work_id, safe='')}?{query}"


def poll_deferred_response(
    response: dict,
    args: argparse.Namespace,
    headers: dict[str, str],
    auth: dict[str, object],
    run_dir: Path,
    case_dir: Path,
    artifact_prefix: str,
) -> tuple[dict, bool]:
    body = response.get("body")
    if not is_pending_home_node_response(body):
        return response, False
    if args.deferred_poll_seconds <= 0 or not args.deferred_url:
        return response, False

    work_id = deferred_work_id(body)
    req_id = request_id(body)
    if not work_id or not req_id:
        return response, False

    deadline = dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=args.deferred_poll_seconds)
    attempt = 0
    latest = response
    while dt.datetime.now(dt.timezone.utc) < deadline:
        attempt += 1
        if attempt > 1:
            time.sleep(max(0.1, args.deferred_poll_interval_seconds))
        deferred = get_json_with_auth_retry(resolve_deferred_url(args, work_id, req_id), args, headers, auth, run_dir)
        write_response_json(case_dir / f"{artifact_prefix}-deferred-{attempt}-response.json", deferred)
        if not deferred["ok"]:
            return response, True
        latest = deferred
        deferred_body = deferred.get("body")
        if isinstance(deferred_body, dict) and str(deferred_body.get("execution_state") or "").lower() == "completed":
            return deferred, True
        if isinstance(deferred_body, dict) and str(deferred_body.get("execution_state") or "").lower() in {"failed", "denied"}:
            return deferred, True
    return response, latest is not response


def status_from_checks(checks: list[dict]) -> str:
    if any(check["status"] == "FAIL" for check in checks):
        return "FAIL"
    if all(check["status"] == "NOT_RUN" for check in checks):
        return "NOT_RUN"
    return "PASS"


def make_check(name: str, status: str, detail: str) -> dict:
    return {"name": name, "status": status, "detail": detail}


def dry_run_case(case: dict, run: str) -> dict:
    checks = [
        make_check("baseline_ask", "NOT_RUN", "Dry run did not call ASK."),
        make_check("controlled_learning_events", "NOT_RUN", f"Planned {len(case['learning_events'])} event(s)."),
        make_check("post_learning_asks", "NOT_RUN", f"Planned {len(case['post_learning_prompts'])} ASK prompt(s)."),
        make_check("trace_contract", "NOT_RUN", "Dry run did not inspect trace fields."),
        make_check("answer_terms", "NOT_RUN", "Dry run did not evaluate response text."),
    ]
    checks.extend(
        make_check(name, "NOT_RUN", "Dry run did not collect structural trace evidence.")
        for name in STRUCTURAL_TRACE_CHECKS
    )
    return {
        "schema_version": "cwi.recursive_controlled_ask_result.v1",
        "run_id": run,
        "case_id": case["case_id"],
        "category": case["category"],
        "entity_token": case["entity_token"],
        "status": "NOT_RUN",
        "checks": checks,
        "evidence": {},
    }


def blocked_case(case: dict, run: str, check_name: str, detail: str) -> dict:
    checks = [
        make_check(check_name, "FAIL", detail),
        make_check("baseline_ask", "NOT_RUN", "Blocked before ASK execution."),
        make_check("controlled_learning_events", "NOT_RUN", "Blocked before controlled learning execution."),
        make_check("post_learning_asks", "NOT_RUN", "Blocked before post-learning ASK execution."),
        make_check("trace_contract", "NOT_RUN", "No ASK response was available for trace inspection."),
        make_check("answer_terms", "NOT_RUN", "No ASK response was available for answer evaluation."),
    ]
    checks.extend(
        make_check(name, "NOT_RUN", "Blocked before structural trace evidence could be collected.")
        for name in STRUCTURAL_TRACE_CHECKS
    )
    return {
        "schema_version": "cwi.recursive_controlled_ask_result.v1",
        "run_id": run,
        "case_id": case["case_id"],
        "category": case["category"],
        "entity_token": case["entity_token"],
        "status": "NOT_RUN",
        "checks": checks,
        "evidence": {},
    }


def execute_case(
    case: dict,
    run: str,
    run_dir: Path,
    args: argparse.Namespace,
    headers: dict[str, str],
    auth: dict[str, object],
) -> dict:
    case_dir = run_dir / "cases" / safe_name(case["case_id"])
    conversation_id = f"{run}:{case['case_id']}"
    checks: list[dict] = []
    evidence: dict[str, object] = {"response_paths": [], "event_paths": [], "deferred_response_paths": []}
    pending_home_node_responses = 0

    baseline_request = ask_payload(case, case["baseline_prompt"], args, conversation_id)
    baseline_response = post_json_with_auth_retry(args.ask_url, baseline_request, args, headers, auth, run_dir)
    connectivity_blocked = baseline_response["status_code"] == 0
    auth_blocked = baseline_response["status_code"] in (401, 403)
    waf_blocked = response_is_waf_blocked(baseline_response)
    write_request_json(case_dir / "baseline-request.json", baseline_request)
    write_response_json(case_dir / "baseline-response.json", baseline_response)
    evidence["baseline_response"] = str(case_dir / "baseline-response.json")
    baseline_effective_response, baseline_deferred_polled = poll_deferred_response(
        baseline_response,
        args,
        headers,
        auth,
        run_dir,
        case_dir,
        "baseline",
    )
    if baseline_deferred_polled:
        baseline_response = baseline_effective_response
        write_response_json(case_dir / "baseline-effective-response.json", baseline_response)
        evidence["deferred_response_paths"].append(str(case_dir / "baseline-effective-response.json"))
    if is_pending_home_node_response(baseline_response.get("body")):
        pending_home_node_responses += 1

    checks.append(make_check(
        "baseline_ask_status",
        "PASS" if baseline_response["ok"] else "FAIL",
        f"ASK returned {baseline_response['status_code']}.",
    ))
    baseline_answer = answer_text(baseline_response["body"])
    if has_all_terms(baseline_answer, case["expected_terms_all"]):
        checks.append(make_check(
            "baseline_not_prelearned",
            "FAIL",
            "Baseline already contained all expected learned terms.",
        ))
    else:
        checks.append(make_check(
            "baseline_not_prelearned",
            "PASS",
            "Baseline did not already prove the controlled fact.",
        ))

    baseline_trace = trace_payload(baseline_response["body"])
    checks.append(make_check(
        "baseline_trace_contract",
        "PASS" if trace_has_required_fields(baseline_trace) else "FAIL",
        "Required trace fields present." if trace_has_required_fields(baseline_trace) else "Required trace fields missing or wrong type.",
    ))

    event_statuses = []
    learning_responses = []
    for index, text in enumerate(case["learning_events"], start=1):
        if args.learning_mode == "recursive_event":
            event = event_payload(case, text, index, args, run)
            response = post_json_with_auth_retry(args.event_url, event, args, headers, auth, run_dir)
            event_path = case_dir / f"learning-event-{index}.json"
            response_path = case_dir / f"learning-event-{index}-response.json"
        else:
            event = ask_payload(case, text, args, conversation_id)
            response = post_json_with_auth_retry(args.ask_url, event, args, headers, auth, run_dir)
            event_path = case_dir / f"learning-ask-{index}-request.json"
            response_path = case_dir / f"learning-ask-{index}-response.json"

        connectivity_blocked = connectivity_blocked or response["status_code"] == 0
        auth_blocked = auth_blocked or response["status_code"] in (401, 403)
        waf_blocked = waf_blocked or response_is_waf_blocked(response)
        write_request_json(event_path, event)
        write_response_json(response_path, response)
        evidence["event_paths"].append(str(event_path))
        event_statuses.append(response["ok"])
        effective_response, deferred_polled = poll_deferred_response(
            response,
            args,
            headers,
            auth,
            run_dir,
            case_dir,
            f"learning-{index}",
        )
        if deferred_polled:
            write_response_json(case_dir / f"learning-{index}-effective-response.json", effective_response)
            evidence["deferred_response_paths"].append(str(case_dir / f"learning-{index}-effective-response.json"))
        if is_pending_home_node_response(effective_response.get("body")):
            pending_home_node_responses += 1
        learning_responses.append(effective_response)

    checks.append(make_check(
        "controlled_learning_events",
        "PASS" if event_statuses and all(event_statuses) else "FAIL",
        f"{sum(1 for ok in event_statuses if ok)} of {len(event_statuses)} learning event(s) accepted.",
    ))

    post_statuses = []
    post_trace_statuses = []
    post_term_statuses = []
    post_forbidden_hits: list[str] = []
    post_responses = []
    for index, prompt in enumerate(case["post_learning_prompts"], start=1):
        request_payload = ask_payload(case, prompt, args, conversation_id)
        response = post_json_with_auth_retry(args.ask_url, request_payload, args, headers, auth, run_dir)
        connectivity_blocked = connectivity_blocked or response["status_code"] == 0
        auth_blocked = auth_blocked or response["status_code"] in (401, 403)
        waf_blocked = waf_blocked or response_is_waf_blocked(response)
        request_path = case_dir / f"post-ask-{index}-request.json"
        response_path = case_dir / f"post-ask-{index}-response.json"
        write_request_json(request_path, request_payload)
        write_response_json(response_path, response)
        evidence["response_paths"].append(str(response_path))
        effective_response, deferred_polled = poll_deferred_response(
            response,
            args,
            headers,
            auth,
            run_dir,
            case_dir,
            f"post-ask-{index}",
        )
        if deferred_polled:
            write_response_json(case_dir / f"post-ask-{index}-effective-response.json", effective_response)
            evidence["deferred_response_paths"].append(str(case_dir / f"post-ask-{index}-effective-response.json"))
            response = effective_response
        if is_pending_home_node_response(response.get("body")):
            pending_home_node_responses += 1
        post_statuses.append(response["ok"])
        post_responses.append(response)

        body = response["body"]
        text = answer_text(body)
        trace = trace_payload(body)
        post_trace_statuses.append(trace_has_required_fields(trace))
        post_term_statuses.append(
            has_all_terms(text, case["expected_terms_all"])
            and has_any_term(text, case.get("expected_terms_any", []))
        )
        post_forbidden_hits.extend(matching_forbidden_terms(text, case.get("forbidden_terms", [])))

    checks.append(make_check(
        "home_node_deferred_completion",
        "PASS" if pending_home_node_responses == 0 else "FAIL",
        "No unresolved home-node pending acknowledgements remained after polling."
        if pending_home_node_responses == 0
        else f"{pending_home_node_responses} response(s) remained pending home-node acknowledgements instead of final answers.",
    ))
    checks.append(make_check(
        "post_learning_ask_status",
        "PASS" if post_statuses and all(post_statuses) else "FAIL",
        f"{sum(1 for ok in post_statuses if ok)} of {len(post_statuses)} post-learning ASK call(s) returned success.",
    ))
    checks.append(make_check(
        "post_learning_trace_contract",
        "PASS" if post_trace_statuses and all(post_trace_statuses) else "FAIL",
        "Required trace fields present in post-learning responses." if post_trace_statuses and all(post_trace_statuses) else "Required trace fields missing in at least one post-learning response.",
    ))
    checks.append(make_check(
        "post_learning_answer_terms",
        "PASS" if post_term_statuses and all(post_term_statuses) else "FAIL",
        "Expected learned terms appeared in all post-learning answers." if post_term_statuses and all(post_term_statuses) else "At least one post-learning answer missed expected terms.",
    ))
    checks.append(make_check(
        "forbidden_terms_absent",
        "PASS" if not post_forbidden_hits else "FAIL",
        "No forbidden terms found." if not post_forbidden_hits else f"Forbidden terms found: {', '.join(sorted(set(post_forbidden_hits)))}",
    ))
    if args.learning_mode == "ask":
        learning_memory_update_pass = bool(learning_responses) and all(
            governed_memory_update_trace(response)
            for response in learning_responses
        )
        checks.append(make_check(
            "learning_trace_governed_memory_update",
            "PASS" if learning_memory_update_pass else "FAIL",
            "Learning ASK responses trace governed memory update."
            if learning_memory_update_pass
            else "At least one learning ASK response did not trace a governed memory update.",
        ))
    else:
        checks.append(make_check(
            "learning_trace_governed_memory_update",
            "NOT_RUN",
            "Recursive-event learning mode bypasses ASK memory-update response traces.",
        ))

    memory_recall_pass = bool(post_responses) and all(
        governed_memory_recall_trace(response)
        for response in post_responses
    )
    checks.append(make_check(
        "post_learning_trace_governed_memory_recall",
        "PASS" if memory_recall_pass else "FAIL",
        "Post-learning ASK responses trace governed memory recall."
        if memory_recall_pass
        else "At least one post-learning ASK response did not trace governed memory recall.",
    ))

    memory_source_pass = bool(post_responses) and all(
        memory_source_grounding_trace(response)
        for response in post_responses
    )
    checks.append(make_check(
        "post_learning_memory_source_grounding",
        "PASS" if memory_source_pass else "FAIL",
        "Post-learning ASK responses are grounded in memory sources."
        if memory_source_pass
        else "At least one post-learning ASK response lacked memory source grounding.",
    ))

    truth_engine_pass = bool(post_responses) and all(
        truth_engine_trace_signal(response)
        for response in post_responses
    )
    checks.append(make_check(
        "truth_engine_trace_signal",
        "PASS" if truth_engine_pass else "FAIL",
        "Post-learning ASK responses include Truth Engine trace fields."
        if truth_engine_pass
        else "At least one post-learning ASK response lacked Truth Engine trace fields.",
    ))

    response_framework_pass = bool(post_responses) and all(
        response_framework_trace_signal(response)
        for response in post_responses
    )
    checks.append(make_check(
        "response_framework_trace_signal",
        "PASS" if response_framework_pass else "FAIL",
        "Post-learning ASK responses include response framework overlay trace fields."
        if response_framework_pass
        else "At least one post-learning ASK response lacked response framework overlay trace fields.",
    ))

    provenance_pass = bool(post_responses) and all(
        provenance_trace_consistency(response)
        for response in post_responses
    )
    checks.append(make_check(
        "provenance_trace_consistency",
        "PASS" if provenance_pass else "FAIL",
        "Post-learning ASK provenance trace IDs and pack hashes match the response trace."
        if provenance_pass
        else "At least one post-learning ASK provenance payload did not match the response trace.",
    ))

    pack_trust_pass = bool(post_responses) and all(
        pack_trust_trace_signal(response)
        for response in post_responses
    )
    checks.append(make_check(
        "pack_trust_trace_signal",
        "PASS" if pack_trust_pass else "FAIL",
        "Post-learning ASK traces include SHA-256 pack hash signals."
        if pack_trust_pass
        else "At least one post-learning ASK trace lacked pack trust hash signals.",
    ))
    checks.append(make_check(
        "environment_connectivity",
        "FAIL" if connectivity_blocked else "PASS",
        "At least one endpoint call failed before HTTP response." if connectivity_blocked else "All endpoint calls returned HTTP responses.",
    ))
    checks.append(make_check(
        "environment_auth",
        "FAIL" if auth_blocked and not waf_blocked else "PASS",
        "At least one endpoint call returned 401/403." if auth_blocked and not waf_blocked else "No application authorization block detected.",
    ))
    checks.append(make_check(
        "environment_waf",
        "FAIL" if waf_blocked else "PASS",
        "At least one endpoint call was blocked by Cloudflare/WAF." if waf_blocked else "No Cloudflare/WAF block detected.",
    ))

    return {
        "schema_version": "cwi.recursive_controlled_ask_result.v1",
        "run_id": run,
        "case_id": case["case_id"],
        "category": case["category"],
        "entity_token": case["entity_token"],
        "status": status_from_checks(checks),
        "checks": checks,
        "evidence": evidence,
    }


def safe_name(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9_.-]+", "-", value)


def structural_evidence_summary(results: list[dict]) -> dict:
    checks: dict[str, dict[str, object]] = {}
    for name, description in STRUCTURAL_TRACE_CHECKS.items():
        checks[name] = {
            "description": description,
            "PASS": 0,
            "FAIL": 0,
            "NOT_RUN": 0,
        }

    for result in results:
        for check in result.get("checks", []):
            name = check.get("name")
            if name not in checks:
                continue
            status = check.get("status", "NOT_RUN")
            if status not in {"PASS", "FAIL", "NOT_RUN"}:
                status = "NOT_RUN"
            checks[name][status] = int(checks[name][status]) + 1

    total = len(results)
    failed = sum(int(value["FAIL"]) for value in checks.values())
    not_run = sum(int(value["NOT_RUN"]) for value in checks.values())
    passed_checks = sum(1 for value in checks.values() if int(value["PASS"]) == total and total > 0)
    trace_derived_all_passed = total > 0 and all(
        int(value["PASS"]) == total
        for value in checks.values()
    )

    return {
        "schema_version": "cwi.recursive_structural_evidence_summary.v1",
        "evidence_level": "trace_derived",
        "trace_derived_all_passed": trace_derived_all_passed,
        "passed_check_count": passed_checks,
        "total_check_count": len(checks),
        "failed_observation_count": failed,
        "not_run_observation_count": not_run,
        "checks": checks,
        "independent_subsystem_verification": {
            "graph_transition": "UNKNOWN",
            "vector_refresh": "UNKNOWN",
            "critic_transition": "UNKNOWN",
            "ledger_transition": "UNKNOWN",
        },
        "limitation": (
            "Trace-derived evidence proves the response reported these subsystems participating. "
            "It does not independently inspect the backing graph, vector, Critic, or ledger stores."
        ),
    }


def summarize(
    results: list[dict],
    suite: dict,
    run: str,
    run_dir: Path,
    executed: bool,
    auth: dict[str, object],
    started_utc: dt.datetime,
) -> dict:
    counts = {"PASS": 0, "FAIL": 0, "NOT_RUN": 0}
    by_category: dict[str, dict[str, int]] = {}
    for result in results:
        status = result["status"]
        counts[status] = counts.get(status, 0) + 1
        category = result["category"]
        by_category.setdefault(category, {"PASS": 0, "FAIL": 0, "NOT_RUN": 0})
        by_category[category][status] = by_category[category].get(status, 0) + 1

    generated_utc = dt.datetime.now(dt.timezone.utc)
    summary = {
        "schema_version": "cwi.recursive_controlled_ask_summary.v1",
        "run_id": run,
        "run_sort_key": run_sort_key(started_utc),
        "run_started_utc": iso_utc(started_utc),
        "run_started_utc_display": utc_display(started_utc),
        "run_started_local_display": local_display(started_utc),
        "suite_id": suite["suite_id"],
        "generated_utc": iso_utc(generated_utc),
        "generated_utc_display": utc_display(generated_utc),
        "generated_local_display": local_display(generated_utc),
        "mode": "execute" if executed else "dry_run",
        "total_cases": len(results),
        "pass_count": counts.get("PASS", 0),
        "fail_count": counts.get("FAIL", 0),
        "not_run_count": counts.get("NOT_RUN", 0),
        "by_category": by_category,
        "structural_evidence": structural_evidence_summary(results),
        "results_path": str(run_dir / "results.ndjson"),
        "report_path": str(run_dir / "report.md"),
        "auth": auth,
    }
    summary.update(report_validity(summary))
    summary["recursive_intelligence_grade"] = grade_recursive_intelligence(summary, results)
    return summary


def report_validity(summary: dict) -> dict:
    if summary["mode"] != "execute":
        return {
            "valid_behavioral_report": False,
            "report_validity": "INVALID_NOT_EXECUTED",
            "report_validity_basis": "The suite was not executed; dry-run artifacts are plans, not behavioral reports.",
        }
    if summary["total_cases"] <= 0:
        return {
            "valid_behavioral_report": False,
            "report_validity": "INVALID_EMPTY_SUITE",
            "report_validity_basis": "The suite selected zero cases.",
        }
    if summary["not_run_count"] > 0:
        return {
            "valid_behavioral_report": False,
            "report_validity": "INVALID_NOT_RUN",
            "report_validity_basis": f"{summary['not_run_count']} of {summary['total_cases']} case(s) were NOT RUN. A behavioral report is valid only when NOT RUN is 0.",
        }
    return {
        "valid_behavioral_report": True,
        "report_validity": "VALID",
        "report_validity_basis": "Every selected controlled case executed. PASS/FAIL results can be used as behavioral evidence.",
    }


def latest_run_record(summary: dict, run_dir: Path, output_root: Path) -> dict:
    grade = summary["recursive_intelligence_grade"]
    return {
        "run_id": summary["run_id"],
        "run_started_local_display": summary.get("run_started_local_display", ""),
        "run_started_utc_display": summary.get("run_started_utc_display", ""),
        "generated_local_display": summary.get("generated_local_display", ""),
        "generated_utc_display": summary.get("generated_utc_display", ""),
        "mode": summary["mode"],
        "learning_mode": summary.get("learning_mode", "ask"),
        "auth_status": summary.get("auth", {}).get("status", "UNKNOWN"),
        "report_validity": summary.get("report_validity", "UNKNOWN"),
        "valid_behavioral_report": summary.get("valid_behavioral_report", False),
        "grade": grade["grade"],
        "working_assessment": grade["working_assessment"],
        "pass_count": summary["pass_count"],
        "fail_count": summary["fail_count"],
        "not_run_count": summary["not_run_count"],
        "run_dir": relative_artifact_path(run_dir, output_root),
        "report_path": relative_artifact_path(run_dir / "report.md", output_root),
        "summary_path": relative_artifact_path(run_dir / "summary.json", output_root),
    }


def update_latest_artifacts(output_root: Path, summary: dict, run_dir: Path) -> None:
    record = latest_run_record(summary, run_dir, output_root)
    write_json(output_root / "latest-run.json", record)
    write_text(output_root / "latest-run.md", render_latest_run(record))
    write_text(output_root / "runs-index.md", render_runs_index(output_root))


def render_latest_run(record: dict) -> str:
    return "\n".join([
        "# Latest Recursive Intelligence Run",
        "",
        f"- Run started: `{record['run_started_local_display']}`",
        f"- Run started UTC: `{record['run_started_utc_display']}`",
        f"- Run ID: `{record['run_id']}`",
        f"- Mode: `{record['mode']}`",
        f"- Learning mode: `{record['learning_mode']}`",
        f"- Auth status: `{record['auth_status']}`",
        f"- Report validity: `{record['report_validity']}`",
        f"- Grade: `{record['grade']}`",
        f"- Working assessment: `{record['working_assessment']}`",
        f"- PASS / FAIL / NOT RUN: `{record['pass_count']} / {record['fail_count']} / {record['not_run_count']}`",
        f"- Report: [{record['report_path']}]({record['report_path']})",
        "",
    ])


def render_runs_index(output_root: Path) -> str:
    records = []
    for summary_path in output_root.glob("*/summary.json"):
        try:
            summary = load_json(summary_path)
        except (OSError, json.JSONDecodeError):
            continue
        grade = summary.get("recursive_intelligence_grade", {})
        not_run = summary.get("not_run_count", 0)
        inferred_validity = summary.get("report_validity")
        if not inferred_validity:
            inferred_validity = "INVALID_NOT_RUN" if not_run else "UNKNOWN"
        records.append({
            "sort": summary.get("run_started_utc", summary.get("generated_utc", summary.get("run_id", ""))),
            "run_id": summary.get("run_id", summary_path.parent.name),
            "started_local": summary.get("run_started_local_display", summary.get("generated_local_display", "")),
            "started_utc": summary.get("run_started_utc_display", summary.get("generated_utc_display", summary.get("generated_utc", ""))),
            "mode": summary.get("mode", ""),
            "learning_mode": summary.get("learning_mode", "ask"),
            "auth_status": summary.get("auth", {}).get("status", "UNKNOWN"),
            "validity": inferred_validity,
            "grade": grade.get("grade", "UNKNOWN"),
            "assessment": grade.get("working_assessment", "UNKNOWN"),
            "pass": summary.get("pass_count", 0),
            "fail": summary.get("fail_count", 0),
            "not_run": not_run,
            "report": f"{summary_path.parent.name}/report.md",
        })
    records.sort(key=lambda item: item["sort"], reverse=True)

    lines = [
        "# Recursive Intelligence Runs",
        "",
        "Most recent runs are listed first.",
        "",
        "| Started Local | Started UTC | Run ID | Mode | Auth | Validity | Grade | PASS | FAIL | NOT RUN | Report |",
        "|---|---|---|---|---|---|---|---:|---:|---:|---|",
    ]
    for record in records[:50]:
        lines.append(
            f"| {record['started_local']} | {record['started_utc']} | `{record['run_id']}` | "
            f"{record['mode']} / {record['learning_mode']} | {record['auth_status']} | "
            f"{record['validity']} | {record['grade']} | {record['pass']} | {record['fail']} | {record['not_run']} | "
            f"[report]({record['report']}) |"
        )
    lines.append("")
    return "\n".join(lines)


def grade_recursive_intelligence(summary: dict, results: list[dict]) -> dict:
    total = summary["total_cases"]
    passed = summary["pass_count"]
    failed = summary["fail_count"]
    not_run = summary["not_run_count"]
    mode = summary["mode"]
    failed_checks = [
        check["name"]
        for result in results
        for check in result["checks"]
        if check["status"] == "FAIL"
    ]
    connectivity_blocked = any(name == "environment_connectivity" for name in failed_checks)
    auth_blocked = any(name == "environment_auth" for name in failed_checks)
    waf_blocked = any(name == "environment_waf" for name in failed_checks)
    home_node_deferred_unresolved = any(name == "home_node_deferred_completion" for name in failed_checks)
    identity_waf_blocked = bool(summary.get("auth", {}).get("waf_blocked"))
    identity_auth_failed = summary.get("auth", {}).get("status") == "FAIL"
    auth_failure_count = sum(
        1
        for result in results
        if any(check["name"] == "environment_auth" and check["status"] == "FAIL" for check in result["checks"])
    )
    home_node_deferred_failure_count = sum(
        1
        for result in results
        if any(check["name"] == "home_node_deferred_completion" and check["status"] == "FAIL" for check in result["checks"])
    )
    privacy_failures = sum(
        1
        for result in results
        if result["category"] == "privacy_boundary"
        for check in result["checks"]
        if check["status"] == "FAIL"
    )

    if mode != "execute":
        return {
            "grade": "INVALID",
            "score": None,
            "working_assessment": "INVALID_REPORT_NOT_EXECUTED",
            "evidence_confidence": "none",
            "basis": "The suite was not executed; this artifact is not a valid behavioral report.",
            "limitation": "No recursive-intelligence grade can be assigned from a dry run.",
        }
    if not_run > 0:
        return {
            "grade": "INVALID",
            "score": None,
            "working_assessment": "INVALID_REPORT_NOT_RUN",
            "evidence_confidence": "none",
            "basis": f"{not_run} of {total} case(s) were NOT RUN. A valid behavioral report requires NOT RUN to be 0.",
            "failed_checks": sorted(set(failed_checks)),
            "limitation": "This artifact is an execution attempt log, not evidence that recursive intelligence is working or failing.",
        }

    if identity_waf_blocked and passed == 0:
        return {
            "grade": "UNKNOWN",
            "score": None,
            "working_assessment": "UNKNOWN_WAF_BLOCKED",
            "evidence_confidence": "none",
            "basis": "Live execution was attempted through the normal ASK path, but production Cloudflare/WAF blocked identity authentication before the application handled the authenticated request.",
            "failed_checks": sorted(set(failed_checks)),
            "limitation": "This is perimeter-access evidence, not evidence that recursive intelligence is working or failing.",
        }
    if identity_auth_failed and passed == 0:
        return {
            "grade": "UNKNOWN",
            "score": None,
            "working_assessment": "UNKNOWN_AUTH_REQUIRED",
            "evidence_confidence": "none",
            "basis": "Live execution was requested, but identity authentication did not produce a bearer token, so the controlled ASK cases were not executed.",
            "failed_checks": sorted(set(failed_checks)),
            "limitation": "This is authentication/configuration evidence, not evidence that recursive intelligence is working or failing.",
        }
    if connectivity_blocked and passed == 0:
        endpoint_scope = "ASK endpoint calls"
        if summary.get("learning_mode") == "recursive_event":
            endpoint_scope = "ASK/recursive-event endpoint calls"
        return {
            "grade": "UNKNOWN",
            "score": None,
            "working_assessment": "UNKNOWN_ENDPOINT_UNAVAILABLE",
            "evidence_confidence": "none",
            "basis": f"Live execution was attempted, but one or more {endpoint_scope} failed before receiving an HTTP response.",
            "failed_checks": sorted(set(failed_checks)),
            "limitation": "This is environment connectivity evidence, not evidence that recursive intelligence is working or failing.",
        }
    if (waf_blocked or identity_waf_blocked) and passed == 0:
        return {
            "grade": "UNKNOWN",
            "score": None,
            "working_assessment": "UNKNOWN_WAF_BLOCKED",
            "evidence_confidence": "none",
            "basis": "Live execution was attempted through the normal ASK path, but production Cloudflare/WAF blocked the runner before the application handled the authenticated request.",
            "failed_checks": sorted(set(failed_checks)),
            "limitation": "This is perimeter-access evidence, not evidence that recursive intelligence is working or failing.",
        }
    if (auth_blocked or identity_auth_failed) and passed == 0 and auth_failure_count == total:
        return {
            "grade": "UNKNOWN",
            "score": None,
            "working_assessment": "UNKNOWN_AUTH_REQUIRED",
            "evidence_confidence": "none",
            "basis": "Live execution was attempted through the normal ASK path, but every selected case encountered 401/403 authorization responses.",
            "failed_checks": sorted(set(failed_checks)),
            "limitation": "This is authentication/configuration evidence, not evidence that recursive intelligence is working or failing.",
        }
    if home_node_deferred_unresolved and passed == 0 and home_node_deferred_failure_count == total:
        return {
            "grade": "UNKNOWN",
            "score": None,
            "working_assessment": "UNKNOWN_HOME_NODE_DEFERRED_UNRESOLVED",
            "evidence_confidence": "high",
            "basis": "Every controlled ASK case executed, but the normal ASK path returned unresolved home-node pending acknowledgements instead of final answers.",
            "failed_checks": sorted(set(failed_checks)),
            "limitation": "This proves a linked home-node completion-path gap for the tested account; it does not prove recursive intelligence is working or failing.",
        }

    pass_rate = passed / total if total else 0.0
    if privacy_failures:
        grade = "F"
        score = min(59, round(pass_rate * 100))
        assessment = "NOT_READY_PRIVACY_BLOCKER"
        confidence = "high"
        basis = "At least one privacy-boundary case failed; recursive learning cannot be treated as working safely."
    elif failed == 0 and not_run == 0:
        grade = "B+"
        score = 89
        assessment = "LIKELY_WORKING_FOR_CONTROLLED_ASK_E2E"
        confidence = "medium"
        if summary.get("structural_evidence", {}).get("trace_derived_all_passed"):
            basis = "All controlled pre/post ASK cases passed with answer checks and trace-derived structural evidence for governed memory, Truth Engine, response overlays, provenance consistency, and pack trust. Grade is capped because this runner still does not independently inspect every backing graph, vector, Critic, and ledger store transition."
        else:
            basis = "All controlled pre/post ASK cases passed with trace and answer checks. Grade is capped because this runner does not independently verify every graph, vector, Critic, Truth Engine, and ledger transition."
    elif pass_rate >= 0.95 and not_run == 0:
        grade = "B"
        score = round(pass_rate * 100)
        assessment = "MOSTLY_WORKING_WITH_REVIEW_NEEDED"
        confidence = "medium"
        basis = "At least 95% of controlled cases passed, but failures prevent a clean recursive-intelligence claim."
    elif pass_rate >= 0.80:
        grade = "C"
        score = round(pass_rate * 100)
        assessment = "PARTIALLY_WORKING"
        confidence = "medium"
        basis = "Most controlled cases passed, but failure volume is high enough to require remediation before claiming the loop works."
    elif pass_rate >= 0.50:
        grade = "D"
        score = round(pass_rate * 100)
        assessment = "WEAK_OR_INCONSISTENT"
        confidence = "medium"
        basis = "The controlled ASK loop showed inconsistent behavior."
    else:
        grade = "F"
        score = round(pass_rate * 100)
        assessment = "NOT_WORKING_FOR_CONTROLLED_ASK_E2E"
        confidence = "medium"
        basis = "Most controlled cases failed or did not produce the expected pre/post learning behavior."

    return {
        "grade": grade,
        "score": score,
        "working_assessment": assessment,
        "evidence_confidence": confidence,
        "basis": basis,
        "pass_rate": round(pass_rate, 4),
        "failed_checks": sorted(set(failed_checks)),
        "auth_failure_count": auth_failure_count,
        "home_node_deferred_failure_count": home_node_deferred_failure_count,
        "privacy_failure_count": privacy_failures,
        "limitation": "This grade reflects the controlled ASK E2E suite only; full production readiness still requires hard governance gates and structural recursive-loop evidence.",
    }


def render_report(summary: dict, results: list[dict], suite: dict) -> str:
    grade = summary["recursive_intelligence_grade"]
    score = "not scored" if grade["score"] is None else str(grade["score"])
    title = "# Recursive Controlled ASK E2E Report"
    if not summary.get("valid_behavioral_report", False):
        title = "# Recursive Controlled ASK E2E Attempt - INVALID REPORT"
    lines = [
        title,
        "",
        f"- Run ID: `{summary['run_id']}`",
        f"- Run started: `{summary.get('run_started_local_display', 'UNKNOWN')}`",
        f"- Run started UTC: `{summary.get('run_started_utc_display', 'UNKNOWN')}`",
        f"- Report generated: `{summary.get('generated_local_display', 'UNKNOWN')}`",
        f"- Report generated UTC: `{summary.get('generated_utc_display', summary.get('generated_utc', 'UNKNOWN'))}`",
        f"- Suite: `{summary['suite_id']}`",
        f"- Mode: `{summary['mode']}`",
        f"- Learning mode: `{summary.get('learning_mode', 'ask')}`",
        f"- Entity namespace: `{summary.get('entity_namespace') or 'none'}`",
        f"- Deferred poll seconds: `{summary.get('deferred_poll_seconds', 0)}`",
        f"- Auth status: `{summary.get('auth', {}).get('status', 'UNKNOWN')}`",
        f"- Report validity: `{summary.get('report_validity', 'UNKNOWN')}`",
        f"- Valid behavioral report: `{str(summary.get('valid_behavioral_report', False)).lower()}`",
        f"- Validity basis: {summary.get('report_validity_basis', 'UNKNOWN')}",
        f"- Total cases: `{summary['total_cases']}`",
        f"- PASS: `{summary['pass_count']}`",
        f"- FAIL: `{summary['fail_count']}`",
        f"- NOT RUN: `{summary['not_run_count']}`",
        f"- Recursive intelligence grade: `{grade['grade']}`",
        f"- Grade score: `{score}`",
        f"- Working assessment: `{grade['working_assessment']}`",
        f"- Evidence confidence: `{grade['evidence_confidence']}`",
        "",
        "## Report Validity",
        "",
        report_validity_text(summary),
        "",
        "## Execution Status",
        "",
        execution_status_text(summary),
        "",
        "## Grade",
        "",
        f"- Grade: `{grade['grade']}`",
        f"- Score: `{score}`",
        f"- Assessment: `{grade['working_assessment']}`",
        f"- Evidence confidence: `{grade['evidence_confidence']}`",
        f"- Basis: {grade['basis']}",
        f"- Limitation: {grade['limitation']}",
        "",
        "## Structural Evidence",
        "",
        structural_evidence_text(summary),
        "",
        "| Evidence Check | PASS | FAIL | NOT RUN |",
        "|---|---:|---:|---:|",
    ]
    structural = summary.get("structural_evidence", {})
    for name, record in structural.get("checks", {}).items():
        lines.append(
            f"| {record.get('description', name)} | {record.get('PASS', 0)} | {record.get('FAIL', 0)} | {record.get('NOT_RUN', 0)} |"
        )
    independent = structural.get("independent_subsystem_verification", {})
    lines.extend([
        "",
        "### Independent Subsystem Verification",
        "",
        "| Subsystem | Status |",
        "|---|---|",
    ])
    for subsystem, status in independent.items():
        lines.append(f"| {subsystem} | `{status}` |")
    lines.extend([
        "",
        "## Stakeholder Update",
        "",
        stakeholder_update(summary),
        "",
        "## Category Summary",
        "",
        "| Category | PASS | FAIL | NOT RUN |",
        "|---|---:|---:|---:|",
    ])
    for category, counts in sorted(summary["by_category"].items()):
        lines.append(
            f"| {category} | {counts.get('PASS', 0)} | {counts.get('FAIL', 0)} | {counts.get('NOT_RUN', 0)} |"
        )
    lines.extend([
        "",
        "## Case Results",
        "",
        "| Case | Category | Entity | Status | Failed Checks |",
        "|---|---|---|---:|---|",
    ])
    for result in results:
        failed = [check["name"] for check in result["checks"] if check["status"] == "FAIL"]
        failed_text = ", ".join(failed) if failed else ""
        lines.append(
            f"| {result['case_id']} | {result['category']} | {result['entity_token']} | {result['status']} | {failed_text} |"
        )
    lines.extend([
        "",
        "## Required Hard Gates",
        "",
    ])
    for gate in suite["default_assertions"]["hard_gates"]:
        lines.append(f"- `{gate}`")
    lines.append("")
    return "\n".join(lines)


def structural_evidence_text(summary: dict) -> str:
    structural = summary.get("structural_evidence", {})
    if summary.get("mode") != "execute":
        return (
            "No structural evidence was collected because this was not a live execution run."
        )
    if not summary.get("valid_behavioral_report", False):
        return (
            "Structural evidence is diagnostic only because this report is not valid behavioral evidence."
        )
    if structural.get("trace_derived_all_passed"):
        return (
            "**TRACE-DERIVED structural evidence passed.** The ASK responses reported governed memory update, governed memory recall, "
            "memory source grounding, Truth Engine fields, response-framework overlay fields, provenance/trace consistency, and pack-trust hash signals. "
            "This strengthens the behavioral result, but independent backing-store verification remains `UNKNOWN` until graph/vector/Critic/ledger stores are queried directly."
        )
    return (
        "**TRACE-DERIVED structural evidence is incomplete.** Review the failed or not-run structural checks before raising the recursive-intelligence grade."
    )


def execution_status_text(summary: dict) -> str:
    assessment = summary["recursive_intelligence_grade"]["working_assessment"]
    if summary["mode"] == "dry_run":
        return (
            "**No live tests were executed.** This report only proves the 50-case plan and "
            "artifact generation path. Run with `--execute` and a configured ASK endpoint "
            "to produce behavioral recursive-intelligence evidence."
        )
    if assessment == "UNKNOWN_ENDPOINT_UNAVAILABLE":
        return (
            "**Live execution was attempted, but endpoint connectivity failed.** The selected "
            "case attempts reached the runner but did not receive HTTP responses from the configured "
            "ASK/recursive-event endpoints. This does not prove recursive intelligence is working or failing."
        )
    if assessment == "UNKNOWN_AUTH_REQUIRED":
        return (
            "**Live execution was attempted, but authentication blocked the ASK calls.** "
            "Configure identity login environment variables or `CWI_AUTH_TOKEN` before treating this suite as behavioral evidence."
        )
    if assessment == "UNKNOWN_WAF_BLOCKED":
        return (
            "**Live execution was attempted, but production Cloudflare/WAF blocked this runner.** "
            "Allowlist the test runner or execute the suite from an approved browser/client path before treating this as behavioral evidence."
        )
    if assessment == "UNKNOWN_HOME_NODE_DEFERRED_UNRESOLVED":
        return (
            "**Live execution reached the normal ASK path, but the linked home-node work did not resolve to final answers.** "
            "The report is valid execution evidence, but it is evidence of a deferred-completion gap rather than recursive-intelligence quality."
        )
    if summary["not_run_count"]:
        return (
            f"Live execution was attempted, but `{summary['not_run_count']}` case(s) were not run. "
            "Treat the grade as incomplete until the skipped cases are explained or rerun."
        )
    return "Live execution completed for every selected controlled case."


def report_validity_text(summary: dict) -> str:
    if summary.get("valid_behavioral_report", False):
        return (
            "**VALID behavioral report.** Every selected case executed and `NOT RUN` is 0. "
            "PASS/FAIL counts can be used as recursive-intelligence test evidence."
        )
    return (
        f"**INVALID behavioral report.** {summary.get('report_validity_basis', 'Unknown validity failure.')} "
        "Use this artifact only as an execution-attempt diagnostic, not as a recursive-intelligence quality report."
    )


def stakeholder_update(summary: dict) -> str:
    grade = summary["recursive_intelligence_grade"]
    if summary["mode"] == "dry_run":
        return (
            "Prepared 50 controlled recursive ASK tests covering ownership, dependencies, open loops, "
            "relationship reinforcement, conflict handling, cache freshness, and privacy boundaries. "
            f"No live ASK calls were executed in this run. Current grade: {grade['grade']} ({grade['working_assessment']})."
        )
    if summary["not_run_count"] == summary["total_cases"]:
        return (
            f"Requested {summary['total_cases']} controlled recursive ASK tests, but no live ASK calls were executed. "
            f"Current grade: {grade['grade']} ({grade['working_assessment']})."
        )
    if summary["not_run_count"]:
        return (
            f"Requested {summary['total_cases']} controlled recursive ASK tests. "
            f"{summary['pass_count']} passed, {summary['fail_count']} failed, and {summary['not_run_count']} were not run. "
            f"Current grade: {grade['grade']} ({grade['working_assessment']})."
        )
    if summary["fail_count"]:
        return (
            f"Executed {summary['total_cases']} controlled recursive ASK tests. "
            f"{summary['pass_count']} passed and {summary['fail_count']} failed. "
            f"Current grade: {grade['grade']} ({grade['working_assessment']})."
        )
    return (
        f"Executed {summary['total_cases']} controlled recursive ASK tests with {summary['pass_count']} passing. "
        f"Current grade: {grade['grade']} ({grade['working_assessment']})."
    )


def validate_suite(suite: dict) -> None:
    cases = suite.get("cases", [])
    if len(cases) != 50:
        raise ValueError(f"Expected exactly 50 cases, found {len(cases)}.")
    case_ids = [case.get("case_id") for case in cases]
    entity_tokens = [case.get("entity_token") for case in cases]
    if len(set(case_ids)) != len(case_ids):
        raise ValueError("Case IDs must be unique.")
    if len(set(entity_tokens)) != len(entity_tokens):
        raise ValueError("Entity tokens must be unique.")
    for case in cases:
        missing = [
            key for key in (
                "case_id",
                "category",
                "entity_token",
                "learning_events",
                "baseline_prompt",
                "post_learning_prompts",
                "expected_terms_all",
                "forbidden_terms",
                "quality_focus",
            )
            if key not in case
        ]
        if missing:
            raise ValueError(f"{case.get('case_id', '<unknown>')} missing {missing}.")
        if not str(case["entity_token"]).startswith("CWI-RL-"):
            raise ValueError(f"{case['case_id']} uses a non-controlled entity token.")
        if not case["learning_events"]:
            raise ValueError(f"{case['case_id']} must include at least one learning event.")
        if not case["post_learning_prompts"]:
            raise ValueError(f"{case['case_id']} must include at least one post-learning prompt.")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Controlled recursive ASK E2E runner")
    parser.add_argument("--suite", default=str(DEFAULT_SUITE))
    parser.add_argument("--output-root", default=str(DEFAULT_OUTPUT_ROOT))
    parser.add_argument("--execute", action="store_true", help="Call live endpoints instead of writing a dry-run plan.")
    parser.add_argument(
        "--learning-mode",
        choices=["ask", "recursive_event"],
        default=os.environ.get("CWI_RECURSIVE_LEARNING_MODE", "ask"),
        help="Use normal ASK prompts for controlled learning by default; recursive_event uses the recursive event API.",
    )
    parser.add_argument("--case-limit", type=int, default=0, help="Optional limit for smoke execution.")
    parser.add_argument("--ask-url", default=resolve_url("CWI_RECURSIVE_ASK_URL", "CWI_BASE_URL", "/api/ask"))
    parser.add_argument("--event-url", default=resolve_url("CWI_RECURSIVE_EVENT_URL", "CWI_ORCHESTRATOR_BASE_URL", "/api/recursive/events"))
    parser.add_argument("--deferred-url", default=resolve_url("CWI_RECURSIVE_DEFERRED_URL", "CWI_BASE_URL", "/api/ask/deferred"))
    parser.add_argument(
        "--deferred-poll-seconds",
        type=float,
        default=float(os.environ.get("CWI_DEFERRED_POLL_SECONDS", str(DEFAULT_DEFERRED_POLL_SECONDS))),
        help="Seconds to poll deferred home-node ASK work after /api/ask returns pending. Use 0 to disable.",
    )
    parser.add_argument("--deferred-poll-interval-seconds", type=float, default=float(os.environ.get("CWI_DEFERRED_POLL_INTERVAL_SECONDS", "2")))
    parser.add_argument("--tenant-id", default=os.environ.get("CWI_TENANT_ID", ""))
    parser.add_argument("--user-id", default=os.environ.get("CWI_USER_ID", ""))
    parser.add_argument("--pack-name", default=os.environ.get("CWI_PACK_NAME", "personal"))
    parser.add_argument(
        "--entity-namespace",
        default=os.environ.get("CWI_RECURSIVE_ENTITY_NAMESPACE", ""),
        help=(
            "Optional numeric namespace inserted into CWI-RL entity tokens for repeatable production runs. "
            "When omitted in --execute mode, the UTC run timestamp is used so prior test memories do not contaminate baselines."
        ),
    )
    parser.add_argument("--policy-mode", default=os.environ.get("CWI_POLICY_MODE", "observe"))
    parser.add_argument("--top-k", type=int, default=int(os.environ.get("CWI_TOP_K", "8")))
    parser.add_argument("--timeout-seconds", type=float, default=float(os.environ.get("CWI_HTTP_TIMEOUT_SECONDS", "20")))
    parser.add_argument("--auth-token", default=os.environ.get("CWI_AUTH_TOKEN", ""))
    parser.add_argument(
        "--auth-mode",
        choices=["auto", "identity", "token", "none"],
        default=os.environ.get("CWI_AUTH_MODE", "auto"),
        help="Authentication source. auto uses CWI_AUTH_TOKEN first, then identity login when identity env vars are present.",
    )
    parser.add_argument("--identity-login-url", default=resolve_identity_login_url())
    parser.add_argument("--identity-tenant-id", default=os.environ.get("CWI_IDENTITY_TENANT_ID", os.environ.get("IDENTITY_SUPERADMIN_TENANT_ID", "")))
    parser.add_argument("--identity-username", default=os.environ.get("CWI_IDENTITY_USERNAME", os.environ.get("IDENTITY_SUPERADMIN_USERNAME", "")))
    parser.add_argument("--identity-email", default=os.environ.get("CWI_IDENTITY_EMAIL", ""))
    parser.add_argument(
        "--identity-password-env",
        default=os.environ.get("CWI_IDENTITY_PASSWORD_ENV", "CWI_IDENTITY_PASSWORD"),
        help="Name of the environment variable containing the identity password. The password value is never written to artifacts.",
    )
    args = parser.parse_args()
    args.suite_id = ""
    if not args.deferred_url and args.ask_url.rstrip("/").endswith("/api/ask"):
        args.deferred_url = args.ask_url.rstrip("/")[: -len("/api/ask")] + "/api/ask/deferred"
    return args


def main() -> int:
    args = parse_args()
    suite = load_json(Path(args.suite))
    validate_suite(suite)

    started_utc = dt.datetime.now(dt.timezone.utc)
    run = run_id(started_utc)
    if args.execute and not args.entity_namespace:
        args.entity_namespace = started_utc.strftime("%Y%m%d%H%M%S")
    if args.entity_namespace:
        suite = namespace_suite_entities(suite, args.entity_namespace)
        validate_suite(suite)
    args.suite_id = suite["suite_id"]
    run_dir = Path(args.output_root) / run
    results_path = run_dir / "results.ndjson"

    if args.execute and not args.ask_url:
        print("ASK URL is required for --execute.", file=sys.stderr)
        return 2
    if args.execute and args.learning_mode == "recursive_event" and not args.event_url:
        print("Recursive-event URL is required for --execute --learning-mode recursive_event.", file=sys.stderr)
        return 2

    headers, auth = authenticate(args, run_dir) if args.execute else ({}, {
        "mode": "none",
        "status": "NOT_RUN",
        "detail": "Dry run did not authenticate.",
        "access_token_received": False,
    })

    cases = suite["cases"]
    if args.case_limit > 0:
        cases = cases[: args.case_limit]

    results = []
    for case in cases:
        if args.execute and auth.get("status") == "FAIL":
            result = blocked_case(
                case,
                run,
                "authentication_setup",
                str(auth.get("detail", "Authentication failed before ASK execution.")),
            )
        elif args.execute:
            result = execute_case(case, run, run_dir, args, headers, auth)
        else:
            result = dry_run_case(case, run)
        results.append(result)
        append_ndjson(results_path, result)

    summary = summarize(results, suite, run, run_dir, executed=args.execute, auth=auth, started_utc=started_utc)
    summary["learning_mode"] = args.learning_mode
    summary["entity_namespace"] = args.entity_namespace
    summary["deferred_poll_seconds"] = args.deferred_poll_seconds
    summary["recursive_intelligence_grade"] = grade_recursive_intelligence(summary, results)
    write_json(run_dir / "summary.json", summary)
    write_text(run_dir / "report.md", render_report(summary, results, suite))
    update_latest_artifacts(Path(args.output_root), summary, run_dir)
    print(json.dumps(summary, indent=2, sort_keys=True))
    if args.execute and not summary.get("valid_behavioral_report", False):
        return 1
    return 1 if args.execute and summary["fail_count"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
