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
class Result:
    latency_ms: float
    status_code: int
    eval_count: int | None
    eval_duration_ns: int | None


def _now_ts() -> str:
    return time.strftime("%Y%m%d-%H%M%S", time.gmtime())


async def _worker(
    client: httpx.AsyncClient,
    url: str,
    payload: dict,
    end_time: float,
    results: list[Result],
    timeout_seconds: float,
) -> None:
    while time.monotonic() < end_time:
        start = time.perf_counter()
        status = 0
        eval_count = None
        eval_duration = None
        try:
            response = await client.post(url, json=payload, timeout=timeout_seconds)
            status = response.status_code
            if status == 200:
                data = response.json()
                eval_count = data.get("eval_count")
                eval_duration = data.get("eval_duration")
        except httpx.RequestError:
            status = 0
        latency_ms = (time.perf_counter() - start) * 1000.0
        results.append(Result(latency_ms=latency_ms, status_code=status, eval_count=eval_count, eval_duration_ns=eval_duration))


def _summarize(results: list[Result]) -> dict:
    latencies = [r.latency_ms for r in results]
    statuses = [r.status_code for r in results]
    success_count = sum(1 for s in statuses if 200 <= s < 300)
    success_rate = success_count / len(statuses) if statuses else 0.0

    token_rates = []
    for r in results:
        if r.eval_count is None or r.eval_duration_ns in (None, 0):
            continue
        token_rates.append(r.eval_count / (r.eval_duration_ns / 1_000_000_000))

    summary = {
        "count": len(latencies),
        "success_rate": success_rate,
        "p50_ms": statistics.quantiles(latencies, n=2)[0] if latencies else 0.0,
        "p95_ms": statistics.quantiles(latencies, n=20)[18] if len(latencies) >= 20 else (max(latencies) if latencies else 0.0),
        "p99_ms": statistics.quantiles(latencies, n=100)[98] if len(latencies) >= 100 else (max(latencies) if latencies else 0.0),
        "max_ms": max(latencies) if latencies else 0.0,
    }

    if token_rates:
        summary["tokens_per_second_avg"] = sum(token_rates) / len(token_rates)
        summary["tokens_per_second_p95"] = statistics.quantiles(token_rates, n=20)[18] if len(token_rates) >= 20 else max(token_rates)

    return summary


async def run_capacity(
    base_url: str,
    model: str,
    prompt: str,
    duration_seconds: int,
    concurrency: int,
    timeout_seconds: float,
) -> dict:
    payload = {"model": model, "prompt": prompt, "stream": False}
    url = f"{base_url}/api/generate"
    results: list[Result] = []
    end_time = time.monotonic() + duration_seconds

    async with httpx.AsyncClient() as client:
        tasks = [
            _worker(client, url, payload, end_time, results, timeout_seconds)
            for _ in range(concurrency)
        ]
        await asyncio.gather(*tasks)

    return {
        "base_url": base_url,
        "model": model,
        "duration_seconds": duration_seconds,
        "concurrency": concurrency,
        "summary": _summarize(results),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Model worker capacity probe")
    parser.add_argument("--base-url", default=os.environ.get("CWI_MODEL_WORKER_URL", "").strip())
    parser.add_argument("--model", default=os.environ.get("CWI_MODEL_WORKER_MODEL") or os.environ.get("MODEL_NAME", ""))
    parser.add_argument("--prompt", default=os.environ.get("CWI_MODEL_WORKER_PROMPT", "Summarize: system readiness."))
    parser.add_argument("--duration-seconds", type=int, default=int(os.environ.get("CWI_CAPACITY_DURATION_SECONDS", "30")))
    parser.add_argument("--concurrency", type=int, default=int(os.environ.get("CWI_CAPACITY_CONCURRENCY", "4")))
    parser.add_argument("--timeout-seconds", type=float, default=float(os.environ.get("CWI_CAPACITY_TIMEOUT_SECONDS", "60")))
    parser.add_argument("--output-dir", default=os.environ.get("CWI_CAPACITY_OUTPUT_DIR", "artifacts/model-worker-capacity"))
    args = parser.parse_args()

    if not args.base_url:
        raise SystemExit("base-url is required (CWI_MODEL_WORKER_URL)")
    if not args.model:
        raise SystemExit("model is required (CWI_MODEL_WORKER_MODEL or MODEL_NAME)")

    payload = asyncio.run(
        run_capacity(
            base_url=args.base_url.rstrip("/"),
            model=args.model,
            prompt=args.prompt,
            duration_seconds=args.duration_seconds,
            concurrency=args.concurrency,
            timeout_seconds=args.timeout_seconds,
        )
    )

    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = output_dir / f"model-worker-capacity-{_now_ts()}.json"
    report_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(report_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
