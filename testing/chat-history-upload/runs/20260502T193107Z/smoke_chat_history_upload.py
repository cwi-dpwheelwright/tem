#!/usr/bin/env python3
import base64
import hashlib
import json
import os
import random
import string
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path

CHUNK_CHARS = 500_000
QUESTIONS_AFTER = [
    "what do you know about me?",
    "what projects am I working on?",
    "who are the key people in my network?",
    "what open loops do I have?",
    "show sources for what you know about me",
]
MARKERS = [
    "Avery Testwell",
    "BLUELANTERN-7429",
    "Project Quartz Lantern",
    "OPEN-LOOP-GAMMA-17",
    "OPEN-LOOP-NEBULA-88",
    "LATE-CHUNK-MARIGOLD-204",
    "DECISION-EMBER-31",
    "HOME-ORCHID-58",
    "GOAL-AURORA-19",
    "Demitrius",
    "Chad",
    "Jasmin",
]


def now_iso():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def http_json(method, url, payload=None, token=None, headers=None, timeout=240):
    data = None
    req_headers = {"Accept": "application/json", "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36", "Origin": "https://cwigpt.com", "Referer": "https://cwigpt.com/"}
    if payload is not None:
        data = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        req_headers["Content-Type"] = "application/json"
    if token:
        req_headers["Authorization"] = f"Bearer {token}"
    if headers:
        req_headers.update(headers)
    req = urllib.request.Request(url, data=data, headers=req_headers, method=method)
    started = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = resp.read().decode("utf-8-sig", errors="replace")
            return {
                "ok": 200 <= resp.status < 300,
                "status": resp.status,
                "duration_ms": int((time.time() - started) * 1000),
                "json": json.loads(body) if body else None,
                "text": body,
            }
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8-sig", errors="replace")
        parsed = None
        try:
            parsed = json.loads(body) if body else None
        except Exception:
            pass
        return {
            "ok": False,
            "status": exc.code,
            "duration_ms": int((time.time() - started) * 1000),
            "json": parsed,
            "text": body,
        }
    except Exception as exc:
        return {
            "ok": False,
            "status": None,
            "duration_ms": int((time.time() - started) * 1000),
            "error": type(exc).__name__,
            "message": str(exc),
            "json": None,
            "text": "",
        }


def login(identity_base, tenant_id, username, password):
    resp = http_json("POST", f"{identity_base}/auth/login", {
        "tenant_id": tenant_id,
        "username": username,
        "password": password,
    }, timeout=60)
    if not resp["ok"] or not (resp.get("json") or {}).get("access_token"):
        raise RuntimeError(f"login failed for {username}: status={resp.get('status')} body={resp.get('text')[:300]}")
    return resp["json"]["access_token"]


def create_password():
    alphabet = string.ascii_letters + string.digits
    return "Sm0ke!" + "".join(random.choice(alphabet) for _ in range(22))


def invite_and_activate(identity_base, admin_token, tenant_id, username, email, display_name, password):
    invite = http_json("POST", f"{identity_base}/admin/users/invite", {
        "tenant_id": tenant_id,
        "username": username,
        "email": email,
        "display_name": display_name,
    }, token=admin_token, timeout=60)
    if not invite["ok"]:
        raise RuntimeError(f"invite failed for {username}: status={invite.get('status')} body={invite.get('text')[:300]}")

    users = http_json("GET", f"{identity_base}/admin/users?{urllib.parse.urlencode({'tenant_id': tenant_id})}", token=admin_token, timeout=60)
    if not users["ok"]:
        raise RuntimeError(f"user list failed: status={users.get('status')} body={users.get('text')[:300]}")
    user = next((u for u in users["json"] if u.get("username") == username), None)
    if not user:
        raise RuntimeError(f"invited user {username} was not returned by admin user list")
    user_id = user["user_id"]

    pw = http_json("POST", f"{identity_base}/admin/users/{urllib.parse.quote(user_id)}/password", {
        "tenant_id": tenant_id,
        "password": password,
    }, token=admin_token, timeout=60)
    if not pw["ok"]:
        raise RuntimeError(f"password set failed for {username}: status={pw.get('status')} body={pw.get('text')[:300]}")
    return user_id


def remove_user(identity_base, admin_token, tenant_id, user_id):
    return http_json("POST", f"{identity_base}/admin/users/{urllib.parse.quote(user_id)}/remove", {
        "tenant_id": tenant_id,
        "reason": "chat-history-upload smoke test cleanup",
    }, token=admin_token, timeout=60)


def normalize_text(value):
    return "\n".join(line.rstrip() for line in str(value).replace("\r\n", "\n").replace("\r", "\n").split("\n")).strip()


def extract_text(value, depth=0):
    if value is None or depth > 6:
        return ""
    if isinstance(value, str):
        return normalize_text(value)
    if isinstance(value, list):
        parts = [extract_text(item, depth + 1) for item in value]
        return normalize_text("\n".join([p for p in parts if p]))
    if isinstance(value, dict):
        for key in ("text", "value", "body", "caption"):
            if isinstance(value.get(key), str) and value.get(key).strip():
                return normalize_text(value[key])
        for key in ("parts", "content", "message", "messages"):
            text = extract_text(value.get(key), depth + 1)
            if text:
                return text
    return ""


def normalize_role(role):
    role = (role or "").lower()
    if role == "assistant":
        return "Assistant"
    if role == "system":
        return "System"
    if role == "tool":
        return "Tool"
    if role == "developer":
        return "Developer"
    return "User"


def collect_messages(parsed):
    raw = []
    if isinstance(parsed, list):
        raw.extend(parsed)
    elif isinstance(parsed, dict):
        if isinstance(parsed.get("messages"), list):
            raw.extend(parsed["messages"])
        if isinstance(parsed.get("items"), list):
            raw.extend(parsed["items"])
        if isinstance(parsed.get("mapping"), dict):
            raw.extend(parsed["mapping"].values())
    messages = []
    for index, candidate in enumerate(raw):
        if not isinstance(candidate, dict):
            continue
        nested = candidate.get("message") if isinstance(candidate.get("message"), dict) else candidate
        author = nested.get("author") if isinstance(nested.get("author"), dict) else candidate.get("author") if isinstance(candidate.get("author"), dict) else {}
        role = normalize_role(nested.get("role") or author.get("role"))
        text = extract_text(nested.get("content") or candidate.get("content") or nested.get("parts") or candidate.get("parts") or nested.get("text") or candidate.get("text"))
        if not text:
            continue
        create_time = nested.get("create_time") or candidate.get("create_time") or nested.get("timestamp") or candidate.get("timestamp")
        messages.append({"index": index, "time": create_time if isinstance(create_time, (int, float)) else 10**18, "role": role, "text": text})
    messages.sort(key=lambda m: (m["time"], m["index"]))
    return messages


def split_segments(value, max_chars):
    remaining = normalize_text(value)
    while remaining:
        if len(remaining) <= max_chars:
            yield remaining
            return
        floor = int(max_chars * 0.6)
        split_at = max_chars
        for sep in ["\n\n", "\n", ". ", " "]:
            pos = remaining.rfind(sep, 0, max_chars + 1)
            if pos >= floor:
                split_at = pos + len(sep)
                break
        segment = remaining[:split_at].strip()
        if segment:
            yield segment
        remaining = remaining[split_at:].strip()


def sanitize_segment(value):
    import re
    out = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return out or "upload"


def build_payloads(zip_path, out_dir, tenant_id, user_id, upload_batch_id):
    extract_dir = out_dir / "extracted"
    extract_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path) as zf:
        names = [name for name in zf.namelist() if name.lower().endswith((".json", ".txt", ".md", ".jsonl")) and not name.endswith("/")]
        if not names:
            raise RuntimeError("zip contained no importable text/json files")
        name = names[0]
        raw = zf.read(name).decode("utf-8-sig", errors="replace")
    parsed = json.loads(raw)
    messages = collect_messages(parsed)
    content = "\n\n".join(f"{m['role']}: {m['text']}" for m in messages)
    segments = list(split_segments(content, CHUNK_CHARS))
    payload_dir = out_dir / "payloads"
    payload_dir.mkdir(parents=True, exist_ok=True)
    source_file_name = f"{Path(zip_path).name}/{name}"
    file_size = len(raw.encode("utf-8"))
    created_utc = now_iso()
    payload_summaries = []
    payloads = []
    for idx, segment in enumerate(segments):
        doc_id = "-".join([
            "web-upload",
            sanitize_segment(upload_batch_id),
            "file-001",
            sanitize_segment(source_file_name),
            f"part-{idx + 1:05d}",
        ])
        metadata = {
            "original_file_name": source_file_name,
            "original_size_bytes": file_size,
            "uploaded_at_utc": created_utc,
            "uploaded_via": "cwi-gpt-web-ui",
            "upload_batch_id": upload_batch_id,
            "uploaded_by": user_id,
            "import_kind": "chat_history",
            "normalized_for_ingest": True,
            "normalization_mode": "structured_transcript",
            "chunk_index": idx + 1,
            "source_file_index": 1,
            "chunk_character_count": len(segment),
            "total_chunks": len(segments),
            "message_count": len(messages),
            "original_modified_utc": created_utc,
        }
        payload = {
            "version": "v1",
            "tenant_id": tenant_id,
            "user_id": user_id,
            "idempotency_key": f"{upload_batch_id}:{doc_id}",
            "tags": ["gpt-web-ui", "chat-history-import", "chat_history", "structured_transcript"],
            "document": {
                "doc_id": doc_id,
                "title": f"{source_file_name} (Part {idx + 1} of {len(segments)})",
                "mime_type": "application/json",
                "content": segment,
                "content_encoding": "plain",
                "source": "cwi-gpt-web-ui",
                "created_utc": created_utc,
                "metadata": metadata,
            },
        }
        payload_path = payload_dir / f"part-{idx + 1:05d}.json"
        payload_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        payloads.append(payload)
        payload_summaries.append({
            "doc_id": doc_id,
            "title": payload["document"]["title"],
            "chars": len(segment),
            "sha256": hashlib.sha256(segment.encode("utf-8")).hexdigest(),
            "contains_late_marker": "LATE-CHUNK-MARIGOLD-204" in segment,
            "contains_nebula_open_loop": "OPEN-LOOP-NEBULA-88" in segment,
        })
    return {
        "source_zip_sha256": hashlib.sha256(Path(zip_path).read_bytes()).hexdigest(),
        "source_entry": source_file_name,
        "message_count": len(messages),
        "total_chunks": len(segments),
        "payload_summaries": payload_summaries,
        "payloads": payloads,
    }


def ask(public_base, token, tenant_id, user_id, question):
    return http_json("POST", f"{public_base}/api/ask", {
        "tenant_id": tenant_id,
        "user_id": user_id,
        "question": question,
        "pack": {"name": "personal", "anchor": "latest"},
        "context": {"top_k": 5, "timezone": "America/Chicago"},
    }, token=token, headers={"x-request-id": f"smoke-{int(time.time()*1000)}"}, timeout=240)


def marker_hits(text):
    text = text or ""
    return [m for m in MARKERS if m.lower() in text.lower()]


def response_digest(resp):
    payload = resp.get("json") or {}
    answer = payload.get("answer") or payload.get("answer_markdown") or resp.get("text") or ""
    trace = payload.get("trace") or {}
    return {
        "ok": resp.get("ok"),
        "status": resp.get("status"),
        "duration_ms": resp.get("duration_ms"),
        "answer": answer,
        "marker_hits": marker_hits(answer),
        "trace": {
            "request_id": trace.get("request_id"),
            "trace_id": trace.get("trace_id"),
            "route": trace.get("route"),
            "intent_type": trace.get("intent_type"),
            "intent_mode": trace.get("intent_mode"),
            "memory_used": trace.get("memory_used"),
            "graph_used": trace.get("graph_used"),
            "confidence": trace.get("confidence"),
            "sources": trace.get("sources"),
            "document_hits": trace.get("document_hits"),
            "graph_hits": trace.get("graph_hits"),
        },
        "citations": payload.get("citations"),
        "error": resp.get("error"),
        "message": resp.get("message"),
    }


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: smoke_chat_history_upload.py <zip_path> <out_dir>")
    zip_path = Path(sys.argv[1])
    out_dir = Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)

    public_base = os.environ.get("PUBLIC_BASE_URL", "https://cwigpt.com").rstrip("/")
    identity_base = os.environ.get("IDENTITY_BASE_URL", "http://127.0.0.1:8085").rstrip("/")
    tenant_id = os.environ.get("SMOKE_TENANT_ID") or os.environ.get("IDENTITY_DEFAULT_TENANT") or "tenant-1"
    super_tenant = os.environ["IDENTITY_SUPERADMIN_TENANT_ID"]
    super_username = os.environ["IDENTITY_SUPERADMIN_USERNAME"]
    super_password = os.environ["IDENTITY_SUPERADMIN_PASSWORD"]
    run_id = out_dir.name.lower().replace("-", "")
    username_a = f"memory-smoke-a-{run_id}"[:60]
    username_b = f"memory-smoke-b-{run_id}"[:60]
    email_a = f"{username_a}@example.invalid"
    email_b = f"{username_b}@example.invalid"
    password_a = create_password()
    password_b = create_password()

    result = {
        "run_id": out_dir.name,
        "started_utc": now_iso(),
        "public_base_url": public_base,
        "identity_base_url": identity_base,
        "tenant_id": tenant_id,
        "endpoint_under_test": f"{public_base}/api/ingest/document",
        "ask_endpoint": f"{public_base}/api/ask",
        "users": {},
        "upload": {},
        "pre_import": {},
        "post_import": {},
        "isolation": {},
        "cleanup": {},
        "pass_fail": {},
    }

    admin_token = login(identity_base, super_tenant, super_username, super_password)
    user_a_id = invite_and_activate(identity_base, admin_token, tenant_id, username_a, email_a, "Memory Smoke User A", password_a)
    user_b_id = invite_and_activate(identity_base, admin_token, tenant_id, username_b, email_b, "Memory Smoke User B", password_b)
    token_a = login(identity_base, tenant_id, username_a, password_a)
    token_b = login(identity_base, tenant_id, username_b, password_b)
    result["users"] = {
        "importing_user": {"username": username_a, "user_id": user_a_id},
        "control_user": {"username": username_b, "user_id": user_b_id},
    }

    pre_a = ask(public_base, token_a, tenant_id, user_a_id, "what do you know about me?")
    result["pre_import"]["importing_user_profile"] = response_digest(pre_a)

    upload_batch_id = f"chat-history-smoke-{out_dir.name}"
    built = build_payloads(zip_path, out_dir, tenant_id, user_a_id, upload_batch_id)
    upload_responses = []
    for idx, payload in enumerate(built["payloads"]):
        req_id = f"{upload_batch_id}-{idx+1:05d}"
        resp = http_json("POST", f"{public_base}/api/ingest/document", payload, token=token_a, headers={"x-request-id": req_id}, timeout=240)
        upload_responses.append({
            "chunk_index": idx + 1,
            "request_id": req_id,
            "ok": resp.get("ok"),
            "status": resp.get("status"),
            "duration_ms": resp.get("duration_ms"),
            "response": resp.get("json") or {"error": resp.get("error"), "message": resp.get("message"), "text": resp.get("text")[:300]},
        })
        if not resp.get("ok"):
            break
    result["upload"] = {
        "source_zip_sha256": built["source_zip_sha256"],
        "source_entry": built["source_entry"],
        "message_count": built["message_count"],
        "total_chunks": built["total_chunks"],
        "payload_summaries": built["payload_summaries"],
        "responses": upload_responses,
    }

    # Give synchronous activation a short settle window for database commits and any additive work.
    time.sleep(3)

    for question in QUESTIONS_AFTER:
        result["post_import"][question] = response_digest(ask(public_base, token_a, tenant_id, user_a_id, question))

    result["isolation"]["control_user_profile"] = response_digest(ask(public_base, token_b, tenant_id, user_b_id, "what do you know about me?"))
    result["isolation"]["control_user_projects"] = response_digest(ask(public_base, token_b, tenant_id, user_b_id, "what projects am I working on?"))

    upload_all_ok = bool(upload_responses) and all(item.get("ok") for item in upload_responses) and len(upload_responses) == built["total_chunks"]
    after_answers = result["post_import"]
    importing_has_memory = any(item.get("marker_hits") for item in after_answers.values())
    importing_memory_trace = any((item.get("trace") or {}).get("memory_used") is True for item in after_answers.values())
    control_hits = []
    for item in result["isolation"].values():
        control_hits.extend(item.get("marker_hits") or [])
    control_isolated = len(control_hits) == 0
    result["pass_fail"] = {
        "upload_all_chunks_accepted": upload_all_ok,
        "importing_user_answers_changed_with_memory_markers": importing_has_memory,
        "importing_user_trace_memory_used": importing_memory_trace,
        "control_user_did_not_receive_imported_markers": control_isolated,
        "overall_pass": upload_all_ok and importing_has_memory and control_isolated,
        "control_user_marker_hits": sorted(set(control_hits)),
    }

    # Cleanup identity users after behavioral proof. Uploaded memories remain scoped to removed user IDs and should be inaccessible.
    result["cleanup"]["remove_importing_user"] = remove_user(identity_base, admin_token, tenant_id, user_a_id)
    result["cleanup"]["remove_control_user"] = remove_user(identity_base, admin_token, tenant_id, user_b_id)
    for cleanup_item in result["cleanup"].values():
        cleanup_item.pop("text", None)
        cleanup_item.pop("json", None)

    result["completed_utc"] = now_iso()
    (out_dir / "smoke-results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")

    lines = []
    pf = result["pass_fail"]
    lines.append(f"# Chat History Upload Smoke Test - {out_dir.name}")
    lines.append("")
    lines.append(f"Result: {'PASS' if pf['overall_pass'] else 'FAIL'}")
    lines.append("")
    lines.append("## Scope")
    lines.append(f"- Endpoint under test: `{result['endpoint_under_test']}`")
    lines.append("- Upload path matches the UI button behavior: ZIP entry expansion -> structured transcript chunks -> JSON `POST /api/ingest/document` requests.")
    lines.append(f"- Source ZIP SHA-256: `{built['source_zip_sha256']}`")
    lines.append(f"- Message count: {built['message_count']}")
    lines.append(f"- Generated chunks: {built['total_chunks']}")
    lines.append("")
    lines.append("## Pass / Fail")
    for key, value in pf.items():
        lines.append(f"- {key}: `{value}`")
    lines.append("")
    lines.append("## Upload Responses")
    for item in upload_responses:
        response = item.get("response") or {}
        lines.append(f"- Chunk {item['chunk_index']}: status `{item.get('status')}`, ok `{item.get('ok')}`, doc `{response.get('doc_id')}`, trace `{response.get('trace_id')}`")
    lines.append("")
    lines.append("## Importing User Verification")
    for question, item in result["post_import"].items():
        trace = item.get("trace") or {}
        preview = (item.get("answer") or "").replace("\n", " ")[:500]
        lines.append(f"### {question}")
        lines.append(f"- status: `{item.get('status')}`, memory_used: `{trace.get('memory_used')}`, route: `{trace.get('route')}`, confidence: `{trace.get('confidence')}`")
        lines.append(f"- marker_hits: `{item.get('marker_hits')}`")
        lines.append(f"- answer preview: {preview}")
        lines.append("")
    lines.append("## Control User Isolation")
    for question, item in result["isolation"].items():
        trace = item.get("trace") or {}
        preview = (item.get("answer") or "").replace("\n", " ")[:500]
        lines.append(f"### {question}")
        lines.append(f"- status: `{item.get('status')}`, memory_used: `{trace.get('memory_used')}`, marker_hits: `{item.get('marker_hits')}`")
        lines.append(f"- answer preview: {preview}")
        lines.append("")
    if not pf["overall_pass"]:
        lines.append("## Root Cause Notes")
        if not upload_all_ok:
            lines.append("- One or more `/api/ingest/document` requests failed or not all generated chunks were accepted. Inspect `smoke-results.json` upload responses.")
        if upload_all_ok and not importing_has_memory:
            lines.append("- Upload accepted but post-import answers did not contain expected synthetic memory markers. Likely break is runtime memory activation/retrieval after source ingest, not the upload endpoint.")
        if importing_has_memory and not importing_memory_trace:
            lines.append("- Answers contained imported facts, but trace did not mark `memory_used=true`; trace instrumentation is incomplete.")
        if not control_isolated:
            lines.append("- Control user saw imported markers. This is a tenant/user isolation failure in memory retrieval.")
    lines.append("")
    lines.append("## Evidence Files")
    lines.append("- `smoke-results.json`: full redacted API result set with answers, traces, citations, upload responses, and pass/fail flags.")
    lines.append("- `payloads/`: exact JSON ingest payloads sent to `/api/ingest/document` with synthetic test content only.")
    (out_dir / "report.md").write_text("\n".join(lines), encoding="utf-8")

    print(json.dumps({"overall_pass": pf["overall_pass"], "run_dir": str(out_dir), "chunks": built["total_chunks"]}))


if __name__ == "__main__":
    main()


