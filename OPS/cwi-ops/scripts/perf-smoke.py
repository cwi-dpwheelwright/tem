import argparse
import asyncio
import json
import os
import statistics
import time
from dataclasses import dataclass
from pathlib import Path


try:
    import httpx  # type: ignore
except Exception as exc:  # pragma: no cover
    raise SystemExit("httpx is required. Install with: python -m pip install httpx") from exc


@dataclass
class Timing:
    endpoint: str
    latency_ms: float
    status_code: int


def _now_ts() -> str:
    return time.strftime("%Y%m%d-%H%M%S", time.gmtime())


def _default_ask_payload(pack_name: str) -> dict:
    return {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 1},
    }


def _default_action_payload() -> dict:
    return {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "observe",
        "action": {"type": "risk_demo", "payload": {"value": "perf-smoke"}},
    }


async def _worker(
    client: httpx.AsyncClient,
    base_url: str,
    duration_seconds: int,
    pack_name: str,
    headers: dict,
    results: list[Timing],
) -> None:
    ask_url = f"{base_url}/api/ask"
    action_url = f"{base_url}/api/action"
    end_time = time.monotonic() + duration_seconds
    ask_payload = _default_ask_payload(pack_name)
    action_payload = _default_action_payload()

    while time.monotonic() < end_time:
        for endpoint, url, payload in (
            ("ask", ask_url, ask_payload),
            ("action", action_url, action_payload),
        ):
            start = time.perf_counter()
            try:
                response = await client.post(url, json=payload, headers=headers, timeout=10.0)
                status = response.status_code
            except httpx.RequestError:
                status = 0
            latency_ms = (time.perf_counter() - start) * 1000.0
            results.append(Timing(endpoint=endpoint, latency_ms=latency_ms, status_code=status))


def _summarize(results: list[Timing]) -> dict:
    summary: dict[str, dict[str, float | int]] = {}
    for endpoint in ("ask", "action"):
        timings = [t.latency_ms for t in results if t.endpoint == endpoint]
        statuses = [t.status_code for t in results if t.endpoint == endpoint]
        if not timings:
            summary[endpoint] = {"count": 0}
            continue
        summary[endpoint] = {
            "count": len(timings),
            "p50_ms": statistics.quantiles(timings, n=2)[0],
            "p95_ms": statistics.quantiles(timings, n=20)[18],
            "p99_ms": statistics.quantiles(timings, n=100)[98],
            "max_ms": max(timings),
            "success_rate": sum(1 for s in statuses if 200 <= s < 300) / len(statuses),
        }
    return summary


async def run_perf(
    base_url: str,
    duration_seconds: int,
    concurrency: int,
    pack_name: str,
    auth_token: str | None,
) -> dict:
    headers = {}
    if auth_token:
        headers["Authorization"] = f"Bearer {auth_token}"

    results: list[Timing] = []
    async with httpx.AsyncClient() as client:
        tasks = [
            _worker(client, base_url, duration_seconds, pack_name, headers, results)
            for _ in range(concurrency)
        ]
        await asyncio.gather(*tasks)

    return {
        "base_url": base_url,
        "duration_seconds": duration_seconds,
        "concurrency": concurrency,
        "pack_name": pack_name,
        "summary": _summarize(results),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Performance smoke runner")
    parser.add_argument("--base-url", default=os.environ.get("PERF_BASE_URL", "").strip())
    parser.add_argument("--duration-seconds", type=int, default=int(os.environ.get("PERF_DURATION_SECONDS", "30")))
    parser.add_argument("--concurrency", type=int, default=int(os.environ.get("PERF_CONCURRENCY", "4")))
    parser.add_argument("--pack-name", default=os.environ.get("CWI_PACK_NAME", "enterprise"))
    parser.add_argument("--auth-token", default=os.environ.get("CWI_AUTH_TOKEN"))
    parser.add_argument("--output-dir", default="artifacts/perf-smoke")
    args = parser.parse_args()

    if not args.base_url:
        raise SystemExit("base-url is required (PERF_BASE_URL)")

    payload = asyncio.run(
        run_perf(
            base_url=args.base_url.rstrip("/"),
            duration_seconds=args.duration_seconds,
            concurrency=args.concurrency,
            pack_name=args.pack_name,
            auth_token=args.auth_token,
        )
    )

    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = output_dir / f"perf-smoke-{_now_ts()}.json"
    report_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(report_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
