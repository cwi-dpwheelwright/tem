from __future__ import annotations

import asyncio
import json
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Protocol

import jsonschema


@dataclass(frozen=True)
class OCRRequest:
    doc_id: str
    content_bytes: bytes
    content_type: str
    page_count: int = 1
    source_path: str | None = None


@dataclass(frozen=True)
class OCRResult:
    doc_id: str
    text: str
    metadata: Dict[str, Any]


class OCRAdapter(Protocol):
    def extract(self, request: OCRRequest) -> OCRResult:
        ...


@dataclass(frozen=True)
class OCRConfig:
    provider: str
    enabled: bool
    timeout_seconds: float
    max_concurrency: int
    max_bytes: int
    max_pages: int
    simulated_text: str | None = None


@dataclass(frozen=True)
class OCROutcome:
    request: OCRRequest
    ok: bool
    result: OCRResult | None
    error: str | None
    duration_seconds: float


class OCRRunner:
    def __init__(self, config: OCRConfig, adapter: OCRAdapter) -> None:
        self.config = config
        self.adapter = adapter
        self._max_concurrency = config.max_concurrency
        self.semaphore: asyncio.Semaphore | None = None

    async def run(self, requests: Iterable[OCRRequest]) -> List[OCROutcome]:
        if self.semaphore is None:
            self.semaphore = asyncio.Semaphore(self._max_concurrency)
        tasks = [self._run_one(request) for request in requests]
        return await asyncio.gather(*tasks)

    async def _run_one(self, request: OCRRequest) -> OCROutcome:
        self._validate_request(request)
        if self.semaphore is None:
            self.semaphore = asyncio.Semaphore(self._max_concurrency)
        async with self.semaphore:
            start = time.time()
            try:
                result = await asyncio.wait_for(
                    asyncio.to_thread(self.adapter.extract, request),
                    timeout=self.config.timeout_seconds,
                )
                return OCROutcome(request, True, result, None, time.time() - start)
            except asyncio.TimeoutError:
                return OCROutcome(request, False, None, "timeout", time.time() - start)
            except Exception as exc:
                return OCROutcome(request, False, None, str(exc), time.time() - start)

    def _validate_request(self, request: OCRRequest) -> None:
        if len(request.content_bytes) > self.config.max_bytes:
            raise ValueError("OCR request exceeds max_bytes")
        if request.page_count > self.config.max_pages:
            raise ValueError("OCR request exceeds max_pages")


class SimulatedOCRAdapter:
    def __init__(self, text: str | None = None) -> None:
        self.text = text or "Simulated OCR output"

    def extract(self, request: OCRRequest) -> OCRResult:
        return OCRResult(
            doc_id=request.doc_id,
            text=self.text,
            metadata={
                "provider": "simulated",
                "content_type": request.content_type,
                "page_count": request.page_count,
            },
        )


def load_ocr_config(payload: Dict[str, Any]) -> OCRConfig:
    schema = json.loads(_find_schema().read_text(encoding="utf-8"))
    validator = jsonschema.Draft202012Validator(schema, format_checker=jsonschema.FormatChecker())
    errors = sorted(validator.iter_errors(payload), key=lambda e: e.path)
    if errors:
        message = "; ".join(error.message for error in errors)
        raise ValueError(f"OCR config invalid: {message}")
    return OCRConfig(
        provider=str(payload.get("provider")),
        enabled=bool(payload.get("enabled", True)),
        timeout_seconds=float(payload.get("timeout_seconds", 5)),
        max_concurrency=int(payload.get("max_concurrency", 4)),
        max_bytes=int(payload.get("max_bytes", 5_000_000)),
        max_pages=int(payload.get("max_pages", 25)),
        simulated_text=payload.get("simulated_text"),
    )


def build_ocr_adapter(config: OCRConfig) -> Optional[OCRAdapter]:
    if not config.enabled or config.provider == "disabled":
        return None
    if config.provider == "simulated":
        return SimulatedOCRAdapter(config.simulated_text)
    raise ValueError(f"Unsupported OCR provider: {config.provider}")


def _find_schema() -> Path:
    current = Path(__file__).resolve()
    for _ in range(8):
        candidate = current / "contracts" / "json-schema" / "ocr_config.json"
        if candidate.exists():
            return candidate
        current = current.parent
    raise FileNotFoundError("Unable to locate contracts/json-schema/ocr_config.json")
