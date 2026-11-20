import argparse
import asyncio
import json
import os
import sys
import time
import xml.etree.ElementTree as ET

import httpx


DEFAULT_ASK_PAYLOAD = {
    "tenant_id": "tenant-1",
    "user_id": "user-1",
    "question": "What is the system status?",
    "pack": {"name": os.environ.get("CWI_PACK_NAME", "enterprise")},
    "context": {"mode": "observe", "top_k": 3},
}

DEFAULT_ACTION_PAYLOAD = {
    "tenant_id": "tenant-1",
    "user_id": "user-1",
    "policy_mode": "approve",
    "action": {"type": "risk_demo", "payload": {"value": "test"}},
}


def _load_payload(env_key, default_payload):
    value = os.environ.get(env_key)
    if not value:
        return default_payload
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        pass
    if os.path.exists(value):
        with open(value, "r", encoding="utf-8") as handle:
            return json.loads(handle.read())
    raise ValueError(f"{env_key} is not valid JSON or a readable file path")


def _parse_status_codes(value):
    if not value:
        return {200}
    codes = set()
    for part in value.split(","):
        part = part.strip()
        if part.isdigit():
            codes.add(int(part))
    return codes or {200}


def _percentile(values, percent):
    if not values:
        return None
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    rank = max(0, min(len(ordered) - 1, int(round((percent / 100.0) * (len(ordered) - 1)))))
    return ordered[rank]


def _summarize(latencies_ms, total_count, error_count, timeout_count):
    success_count = total_count - error_count
    p50 = _percentile(latencies_ms, 50)
    p95 = _percentile(latencies_ms, 95)
    avg = sum(latencies_ms) / len(latencies_ms) if latencies_ms else None
    return {
        "count": total_count,
        "success_count": success_count,
        "error_count": error_count,
        "timeout_count": timeout_count,
        "error_rate": (error_count / total_count) if total_count else 0.0,
        "p50_ms": p50,
        "p95_ms": p95,
        "avg_ms": avg,
        "min_ms": min(latencies_ms) if latencies_ms else None,
        "max_ms": max(latencies_ms) if latencies_ms else None,
    }


async def _run_batch(client, url, headers, payload, count, concurrency, timeout_seconds, accepted_statuses):
    semaphore = asyncio.Semaphore(concurrency)
    latencies = []
    errors = 0
    timeouts = 0

    async def _one():
        nonlocal errors, timeouts
        async with semaphore:
            start = time.perf_counter()
            try:
                resp = await client.post(url, json=payload, headers=headers)
                elapsed = (time.perf_counter() - start) * 1000.0
                if resp.status_code not in accepted_statuses:
                    errors += 1
                latencies.append(elapsed)
            except (httpx.TimeoutException, asyncio.TimeoutError):
                elapsed = (time.perf_counter() - start) * 1000.0
                timeouts += 1
                errors += 1
                latencies.append(elapsed)
            except httpx.RequestError:
                elapsed = (time.perf_counter() - start) * 1000.0
                errors += 1
                latencies.append(elapsed)

    tasks = [_one() for _ in range(count)]
    await asyncio.gather(*tasks)
    return _summarize(latencies, count, errors, timeouts), latencies


def _collect_thresholds(args):
    def parse_float(value):
        if value is None:
            return None
        try:
            return float(value)
        except ValueError:
            return None

    return {
        "max_error_rate": parse_float(args.max_error_rate),
        "max_p95_ms": parse_float(args.max_p95_ms),
        "max_p50_ms": parse_float(args.max_p50_ms),
        "max_avg_ms": parse_float(args.max_avg_ms),
    }


def _apply_thresholds(label, summary, thresholds):
    violations = []
    if thresholds["max_error_rate"] is not None and summary["error_rate"] > thresholds["max_error_rate"]:
        violations.append(
            f"{label}.error_rate {summary['error_rate']:.3f} > {thresholds['max_error_rate']:.3f}"
        )
    if summary["p95_ms"] is not None and thresholds["max_p95_ms"] is not None:
        if summary["p95_ms"] > thresholds["max_p95_ms"]:
            violations.append(f"{label}.p95_ms {summary['p95_ms']:.1f} > {thresholds['max_p95_ms']:.1f}")
    if summary["p50_ms"] is not None and thresholds["max_p50_ms"] is not None:
        if summary["p50_ms"] > thresholds["max_p50_ms"]:
            violations.append(f"{label}.p50_ms {summary['p50_ms']:.1f} > {thresholds['max_p50_ms']:.1f}")
    if summary["avg_ms"] is not None and thresholds["max_avg_ms"] is not None:
        if summary["avg_ms"] > thresholds["max_avg_ms"]:
            violations.append(f"{label}.avg_ms {summary['avg_ms']:.1f} > {thresholds['max_avg_ms']:.1f}")
    return violations


def _write_json(path, payload):
    if not path:
        return
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)


def _write_junit(path, summaries, violations, duration_seconds):
    if not path:
        return

    tests = len(summaries)
    failures = len([item for item in violations if item])

    testsuite = ET.Element("testsuite", {
        "name": "cwi-gold-tests-performance",
        "tests": str(tests),
        "failures": str(failures),
        "errors": "0",
        "time": f"{duration_seconds:.3f}",
    })

    for label, summary in summaries.items():
        case = ET.SubElement(testsuite, "testcase", {
            "classname": "perf",
            "name": label,
            "time": f"{(summary.get('avg_ms') or 0.0) / 1000.0:.3f}",
        })
        failure_messages = violations.get(label, [])
        if failure_messages:
            failure = ET.SubElement(case, "failure", {"message": "thresholds_exceeded"})
            failure.text = "; ".join(failure_messages)

    tree = ET.ElementTree(testsuite)
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    tree.write(path, encoding="utf-8", xml_declaration=True)


async def _run_async(args):
    if not args.base_url:
        print("CWI_BASE_URL or --base-url is required.")
        return 2

    if args.action_count > 0 and not os.environ.get("CWI_AUTH_TOKEN"):
        print("CWI_AUTH_TOKEN is required when action_count > 0.")
        return 2

    ask_payload = _load_payload("CWI_PERF_ASK_PAYLOAD_JSON", DEFAULT_ASK_PAYLOAD)
    action_payload = _load_payload("CWI_PERF_ACTION_PAYLOAD_JSON", DEFAULT_ACTION_PAYLOAD)

    accepted_statuses = _parse_status_codes(
        os.environ.get("CWI_PERF_ACCEPT_STATUS", args.accept_status)
    )

    headers = {}
    token = os.environ.get("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    timeout = httpx.Timeout(args.timeout_seconds)

    start = time.perf_counter()
    async with httpx.AsyncClient(timeout=timeout) as client:
        ask_summary, ask_latencies = await _run_batch(
            client,
            f"{args.base_url}/api/ask",
            headers,
            ask_payload,
            args.ask_count,
            args.concurrency,
            args.timeout_seconds,
            accepted_statuses,
        )
        action_summary = None
        action_latencies = []
        if args.action_count > 0:
            action_summary, action_latencies = await _run_batch(
                client,
                f"{args.base_url}/api/action",
                headers,
                action_payload,
                args.action_count,
                args.concurrency,
                args.timeout_seconds,
                accepted_statuses,
            )

    duration = time.perf_counter() - start

    summaries = {"ask": ask_summary}
    if action_summary:
        summaries["action"] = action_summary

    overall_latencies = ask_latencies + action_latencies
    overall_count = ask_summary["count"] + (action_summary["count"] if action_summary else 0)
    overall_errors = ask_summary["error_count"] + (action_summary["error_count"] if action_summary else 0)
    overall_timeouts = ask_summary["timeout_count"] + (action_summary["timeout_count"] if action_summary else 0)

    overall_summary = _summarize(
        overall_latencies,
        overall_count,
        overall_errors,
        overall_timeouts,
    )
    summaries["overall"] = overall_summary

    thresholds = _collect_thresholds(args)
    violation_map = {
        label: _apply_thresholds(label, summary, thresholds) for label, summary in summaries.items()
    }
    violation_map = {k: v for k, v in violation_map.items() if v}

    result = {
        "base_url": args.base_url,
        "ask_count": args.ask_count,
        "action_count": args.action_count,
        "concurrency": args.concurrency,
        "timeout_seconds": args.timeout_seconds,
        "summaries": summaries,
        "thresholds": thresholds,
        "violations": violation_map,
        "duration_seconds": duration,
    }

    _write_json(args.json_summary, result)
    _write_junit(args.junit_xml, summaries, violation_map, duration)

    if violation_map:
        return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="CWI performance smoke runner")
    parser.add_argument("--base-url", default=os.environ.get("CWI_BASE_URL", ""))
    parser.add_argument("--ask-count", type=int, default=int(os.environ.get("CWI_PERF_ASK_COUNT", "25")))
    parser.add_argument("--action-count", type=int, default=int(os.environ.get("CWI_PERF_ACTION_COUNT", "10")))
    parser.add_argument("--concurrency", type=int, default=int(os.environ.get("CWI_PERF_CONCURRENCY", "10")))
    parser.add_argument("--timeout-seconds", type=float, default=float(os.environ.get("CWI_PERF_TIMEOUT_SECONDS", "20")))
    parser.add_argument("--accept-status", default=os.environ.get("CWI_PERF_ACCEPT_STATUS", "200"))
    parser.add_argument("--junit-xml", default=os.environ.get("CWI_JUNIT_XML", ""))
    parser.add_argument("--json-summary", default=os.environ.get("CWI_JSON_SUMMARY", ""))
    parser.add_argument("--max-error-rate", default=os.environ.get("CWI_PERF_MAX_ERROR_RATE", "0"))
    parser.add_argument("--max-p95-ms", default=os.environ.get("CWI_PERF_MAX_P95_MS"))
    parser.add_argument("--max-p50-ms", default=os.environ.get("CWI_PERF_MAX_P50_MS"))
    parser.add_argument("--max-avg-ms", default=os.environ.get("CWI_PERF_MAX_AVG_MS"))
    args = parser.parse_args()

    return asyncio.run(_run_async(args))


if __name__ == "__main__":
    raise SystemExit(main())
