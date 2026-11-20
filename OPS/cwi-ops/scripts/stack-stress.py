import argparse
import asyncio
import json
import os
import subprocess
import sys
import time
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path


try:
    import httpx  # type: ignore
except Exception as exc:  # pragma: no cover
    raise SystemExit("httpx is required. Install with: python -m pip install httpx") from exc


@dataclass
class Metrics:
    count: int = 0
    success_count: int = 0
    error_count: int = 0
    timeout_count: int = 0
    latencies_ms: list[float] = None
    status_counts: dict[int, int] = None

    def __post_init__(self) -> None:
        if self.latencies_ms is None:
            self.latencies_ms = []
        if self.status_counts is None:
            self.status_counts = defaultdict(int)

    def add(self, latency_ms: float, status: int, success: bool, timeout: bool) -> None:
        self.count += 1
        self.latencies_ms.append(latency_ms)
        self.status_counts[status] += 1
        if success:
            self.success_count += 1
        else:
            self.error_count += 1
        if timeout:
            self.timeout_count += 1


def _now_ts() -> str:
    return time.strftime("%Y%m%d-%H%M%S", time.gmtime())


def _now_iso() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def _default_ask_payload(pack_name: str) -> dict:
    question = os.environ.get("CWI_STRESS_QUESTION") or "Please explain the laws of physics."
    return {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": question,
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 1},
    }


def _default_action_payload() -> dict:
    return {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "observe",
        "action": {"type": "risk_demo", "payload": {"value": "stress-test"}},
    }


def _load_payload(value: str | None, fallback: dict) -> dict:
    if not value:
        return fallback
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        pass
    if os.path.exists(value):
        with open(value, "r", encoding="utf-8") as handle:
            return json.loads(handle.read())
    raise ValueError("Payload override is not valid JSON or a readable file path.")


def _parse_accept_status(value: str | None) -> set[int] | None:
    if not value:
        return None
    codes: set[int] = set()
    for part in value.split(","):
        part = part.strip()
        if not part:
            continue
        if part.isdigit():
            codes.add(int(part))
    return codes or None


def _percentile(values: list[float], percent: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    rank = max(0, min(len(ordered) - 1, int(round((percent / 100.0) * (len(ordered) - 1)))))
    return ordered[rank]


def _summarize(metrics: Metrics, duration_seconds: float) -> dict:
    latencies = metrics.latencies_ms
    avg = sum(latencies) / len(latencies) if latencies else None
    status_counts = {str(code): count for code, count in metrics.status_counts.items()}
    return {
        "count": metrics.count,
        "success_count": metrics.success_count,
        "error_count": metrics.error_count,
        "timeout_count": metrics.timeout_count,
        "error_rate": (metrics.error_count / metrics.count) if metrics.count else 0.0,
        "p50_ms": _percentile(latencies, 50),
        "p95_ms": _percentile(latencies, 95),
        "p99_ms": _percentile(latencies, 99),
        "avg_ms": avg,
        "min_ms": min(latencies) if latencies else None,
        "max_ms": max(latencies) if latencies else None,
        "rps": (metrics.count / duration_seconds) if duration_seconds > 0 else 0.0,
        "status_counts": status_counts,
    }


def _apply_thresholds(summary: dict, thresholds: dict) -> list[str]:
    violations: list[str] = []
    if thresholds["max_error_rate"] is not None and summary["error_rate"] > thresholds["max_error_rate"]:
        violations.append(
            f"error_rate {summary['error_rate']:.3f} > {thresholds['max_error_rate']:.3f}"
        )
    if summary["p95_ms"] is not None and thresholds["max_p95_ms"] is not None:
        if summary["p95_ms"] > thresholds["max_p95_ms"]:
            violations.append(f"p95_ms {summary['p95_ms']:.1f} > {thresholds['max_p95_ms']:.1f}")
    if summary["p99_ms"] is not None and thresholds["max_p99_ms"] is not None:
        if summary["p99_ms"] > thresholds["max_p99_ms"]:
            violations.append(f"p99_ms {summary['p99_ms']:.1f} > {thresholds['max_p99_ms']:.1f}")
    if summary["avg_ms"] is not None and thresholds["max_avg_ms"] is not None:
        if summary["avg_ms"] > thresholds["max_avg_ms"]:
            violations.append(f"avg_ms {summary['avg_ms']:.1f} > {thresholds['max_avg_ms']:.1f}")
    return violations


def _extract_error(response: httpx.Response) -> str | None:
    try:
        data = response.json()
        message = data.get("message") or data.get("error")
        if isinstance(message, str) and message.strip():
            return message.strip()
    except Exception:
        pass
    text = response.text.strip()
    return text[:200] if text else None


def _open_output_dir(path: Path) -> None:
    try:
        if os.name == "nt":
            os.startfile(path)  # type: ignore[attr-defined]
            return
        if sys.platform == "darwin":
            subprocess.run(["open", str(path)], check=False)
            return
        subprocess.run(["xdg-open", str(path)], check=False)
    except Exception:
        return


async def _log_writer(queue: asyncio.Queue, path: Path) -> None:
    with path.open("a", encoding="utf-8") as handle:
        while True:
            item = await queue.get()
            if item is None:
                break
            handle.write(json.dumps(item, separators=(",", ":")) + "\n")


async def _worker(
    client: httpx.AsyncClient,
    endpoints: list[tuple[str, str, dict]],
    headers: dict,
    end_time: float,
    accept_statuses: set[int] | None,
    metrics: dict[str, Metrics],
    overall: Metrics,
    log_mode: str,
    log_queue: asyncio.Queue | None,
    step_index: int,
) -> None:
    while time.monotonic() < end_time:
        for label, url, payload in endpoints:
            start = time.perf_counter()
            status = 0
            timeout = False
            error = None
            request_id = None
            try:
                response = await client.post(url, json=payload, headers=headers)
                status = response.status_code
                request_id = response.headers.get("x-request-id") or response.headers.get("x-correlation-id")
                if accept_statuses is None:
                    success = 200 <= status < 300
                else:
                    success = status in accept_statuses
                if not success:
                    error = _extract_error(response)
            except (httpx.TimeoutException, asyncio.TimeoutError):
                timeout = True
                success = False
                error = "timeout"
            except httpx.RequestError as exc:
                success = False
                error = type(exc).__name__

            latency_ms = (time.perf_counter() - start) * 1000.0
            metrics[label].add(latency_ms, status, success, timeout)
            overall.add(latency_ms, status, success, timeout)

            if log_queue and log_mode != "none":
                if log_mode == "all" or not success:
                    await log_queue.put({
                        "ts": _now_iso(),
                        "step": step_index,
                        "endpoint": label,
                        "status": status,
                        "latency_ms": round(latency_ms, 2),
                        "request_id": request_id,
                        "success": success,
                        "error": error,
                    })


async def _run_step(
    step_index: int,
    base_url: str,
    duration_seconds: int,
    concurrency: int,
    endpoints: list[tuple[str, str, dict]],
    headers: dict,
    timeout_seconds: float,
    accept_statuses: set[int] | None,
    log_mode: str,
    log_path: Path | None,
    thresholds: dict,
) -> dict:
    metrics = {label: Metrics() for label, _, _ in endpoints}
    overall = Metrics()
    end_time = time.monotonic() + duration_seconds

    log_queue = None
    writer_task = None
    if log_path and log_mode != "none":
        log_queue = asyncio.Queue(maxsize=10000)
        writer_task = asyncio.create_task(_log_writer(log_queue, log_path))

    timeout = httpx.Timeout(timeout_seconds)
    async with httpx.AsyncClient(timeout=timeout) as client:
        tasks = [
            _worker(
                client,
                endpoints,
                headers,
                end_time,
                accept_statuses,
                metrics,
                overall,
                log_mode,
                log_queue,
                step_index,
            )
            for _ in range(concurrency)
        ]
        await asyncio.gather(*tasks)

    if log_queue and writer_task:
        await log_queue.put(None)
        await writer_task

    summaries = {label: _summarize(item, duration_seconds) for label, item in metrics.items()}
    summaries["overall"] = _summarize(overall, duration_seconds)
    violations = _apply_thresholds(summaries["overall"], thresholds)

    return {
        "step": step_index,
        "concurrency": concurrency,
        "duration_seconds": duration_seconds,
        "summaries": summaries,
        "violations": violations,
    }


def _require_auth() -> bool:
    raw = os.environ.get("AUTH_REQUIRED")
    if raw is None or not str(raw).strip():
        return True
    return str(raw).lower() in ("1", "true", "yes")


def _candidate_identity_urls(base_url: str) -> list[str]:
    urls: list[str] = []
    port = os.environ.get("IDENTITY_HTTP_PORT")
    if port:
        urls.append(f"http://localhost:{port}")
    for name in ("CWI_IDENTITY_BASE_URL", "IDENTITY_BASE_URL", "IDENTITY_URL"):
        value = os.environ.get(name)
        if value:
            urls.append(value)
    if base_url:
        urls.append(base_url)
    seen = set()
    ordered: list[str] = []
    for url in urls:
        normalized = url.rstrip("/")
        if normalized and normalized not in seen:
            ordered.append(normalized)
            seen.add(normalized)
    return ordered


async def _fetch_auth_token(client: httpx.AsyncClient, base_url: str) -> tuple[str, str]:
    tenant_id = os.environ.get("IDENTITY_BOOTSTRAP_TENANT_ID") or os.environ.get("CWI_TENANT_ID") or "tenant-1"
    email = os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_EMAIL")
    username = os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_USERNAME")
    password = os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD")

    if not password:
        raise SystemExit("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD is required to fetch auth token.")
    if not email and not username:
        raise SystemExit("IDENTITY_BOOTSTRAP_ADMIN_EMAIL or IDENTITY_BOOTSTRAP_ADMIN_USERNAME is required.")

    payload = {"tenant_id": tenant_id, "password": password}
    if email:
        payload["email"] = email
    else:
        payload["username"] = username

    errors: list[str] = []
    for identity_base in _candidate_identity_urls(base_url):
        url = f"{identity_base}/auth/login"
        try:
            resp = await client.post(url, json=payload)
        except httpx.RequestError as exc:
            errors.append(f"{url} -> {type(exc).__name__}")
            continue

        if resp.status_code != 200:
            snippet = resp.text.strip().replace("\n", " ")[:200]
            errors.append(f"{url} -> {resp.status_code} {snippet}")
            continue

        token = (resp.json() or {}).get("access_token")
        if not token:
            errors.append(f"{url} -> missing access_token")
            continue
        return token, identity_base

    error_text = "; ".join(errors) if errors else "no identity endpoints attempted"
    raise SystemExit(f"Failed to fetch auth token. Attempts: {error_text}")


async def _run(args) -> dict:
    base_url = args.base_url.rstrip("/")
    pack_name = args.pack_name
    auth_token = args.auth_token or os.environ.get("CWI_AUTH_TOKEN")
    require_auth = _require_auth()

    auth_summary = {"required": require_auth, "token_source": None, "errors": []}
    if auth_token:
        auth_summary["token_source"] = "provided"
    if require_auth and not auth_token:
        async with httpx.AsyncClient(timeout=10.0) as auth_client:
            try:
                auth_token, source = await _fetch_auth_token(auth_client, base_url)
                auth_summary["token_source"] = source
            except SystemExit as exc:
                auth_summary["errors"].append(str(exc))
                raise

    headers = {}
    if auth_token:
        headers["Authorization"] = f"Bearer {auth_token}"

    ask_payload = _load_payload(
        os.environ.get("CWI_STRESS_ASK_PAYLOAD_JSON"),
        _default_ask_payload(pack_name),
    )
    action_payload = _load_payload(
        os.environ.get("CWI_STRESS_ACTION_PAYLOAD_JSON"),
        _default_action_payload(),
    )

    endpoints: list[tuple[str, str, dict]] = []
    if args.include_ask:
        endpoints.append(("ask", f"{base_url}/api/ask", ask_payload))
    if args.include_action:
        endpoints.append(("action", f"{base_url}/api/action", action_payload))

    if not endpoints:
        raise SystemExit("At least one endpoint must be enabled.")

    if args.include_action and require_auth and not auth_token:
        raise SystemExit("CWI_AUTH_TOKEN is required when action endpoint is enabled.")

    accept_statuses = _parse_accept_status(args.accept_status)
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    timestamp = _now_ts()
    report_path = output_dir / f"stack-stress-{timestamp}.json"
    log_path = output_dir / f"stack-stress-{timestamp}.jsonl" if args.log_mode != "none" else None

    thresholds = {
        "max_error_rate": args.max_error_rate,
        "max_p95_ms": args.max_p95_ms,
        "max_p99_ms": args.max_p99_ms,
        "max_avg_ms": args.max_avg_ms,
    }

    steps: list[dict] = []
    stop_reason = None
    step_index = 1

    for concurrency in range(args.start_concurrency, args.max_concurrency + 1, args.step_concurrency):
        step_log_path = None
        if log_path:
            step_log_path = log_path
        result = await _run_step(
            step_index,
            base_url,
            args.step_duration_seconds,
            concurrency,
            endpoints,
            headers,
            args.timeout_seconds,
            accept_statuses,
            args.log_mode,
            step_log_path,
            thresholds,
        )
        steps.append(result)
        if result["violations"]:
            stop_reason = {
                "step": step_index,
                "concurrency": concurrency,
                "violations": result["violations"],
            }
            if args.stop_on_threshold:
                break
        if args.cooldown_seconds > 0 and concurrency < args.max_concurrency:
            await asyncio.sleep(args.cooldown_seconds)
        step_index += 1

    payload = {
        "base_url": base_url,
        "pack_name": pack_name,
        "auth": auth_summary,
        "include_ask": args.include_ask,
        "include_action": args.include_action,
        "start_concurrency": args.start_concurrency,
        "step_concurrency": args.step_concurrency,
        "max_concurrency": args.max_concurrency,
        "step_duration_seconds": args.step_duration_seconds,
        "cooldown_seconds": args.cooldown_seconds,
        "timeout_seconds": args.timeout_seconds,
        "accept_status": args.accept_status,
        "log_mode": args.log_mode,
        "thresholds": thresholds,
        "stop_reason": stop_reason,
        "steps": steps,
    }

    report_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(report_path)
    if log_path:
        print(log_path)
    _open_output_dir(output_dir)
    return payload


def main() -> int:
    parser = argparse.ArgumentParser(description="Stack stress runner (gateway -> model)")
    parser.add_argument("--base-url", default=os.environ.get("CWI_BASE_URL", "").strip())
    parser.add_argument("--pack-name", default=os.environ.get("CWI_PACK_NAME", "enterprise"))
    parser.add_argument("--auth-token", default=os.environ.get("CWI_AUTH_TOKEN", "").strip())
    parser.add_argument("--no-ask", dest="include_ask", action="store_false")
    parser.add_argument("--no-action", dest="include_action", action="store_false")
    parser.add_argument("--start-concurrency", type=int, default=int(os.environ.get("CWI_STRESS_START_CONCURRENCY", "4")))
    parser.add_argument("--step-concurrency", type=int, default=int(os.environ.get("CWI_STRESS_STEP_CONCURRENCY", "4")))
    parser.add_argument("--max-concurrency", type=int, default=int(os.environ.get("CWI_STRESS_MAX_CONCURRENCY", "64")))
    parser.add_argument("--step-duration-seconds", type=int, default=int(os.environ.get("CWI_STRESS_STEP_DURATION_SECONDS", "60")))
    parser.add_argument("--cooldown-seconds", type=int, default=int(os.environ.get("CWI_STRESS_COOLDOWN_SECONDS", "10")))
    parser.add_argument("--timeout-seconds", type=float, default=float(os.environ.get("CWI_STRESS_TIMEOUT_SECONDS", "30")))
    parser.add_argument("--accept-status", default=os.environ.get("CWI_STRESS_ACCEPT_STATUS"))
    parser.add_argument("--log-mode", choices=["all", "failures", "none"], default=os.environ.get("CWI_STRESS_LOG_MODE", "all"))
    parser.add_argument("--output-dir", default=os.environ.get("CWI_STRESS_OUTPUT_DIR", "artifacts/stack-stress"))
    parser.add_argument("--max-error-rate", type=float, default=float(os.environ.get("CWI_STRESS_MAX_ERROR_RATE", "0.05")))
    parser.add_argument("--max-p95-ms", type=float, default=float(os.environ.get("CWI_STRESS_MAX_P95_MS", "8000")))
    parser.add_argument("--max-p99-ms", type=float, default=float(os.environ.get("CWI_STRESS_MAX_P99_MS", "12000")))
    parser.add_argument("--max-avg-ms", type=float, default=float(os.environ.get("CWI_STRESS_MAX_AVG_MS", "5000")))
    parser.add_argument("--no-stop-on-threshold", dest="stop_on_threshold", action="store_false")
    parser.set_defaults(include_ask=True, include_action=True, stop_on_threshold=True)
    args = parser.parse_args()

    if not args.base_url:
        raise SystemExit("base-url is required (CWI_BASE_URL)")

    asyncio.run(_run(args))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
