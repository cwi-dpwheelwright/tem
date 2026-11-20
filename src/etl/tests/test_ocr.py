import asyncio

import pytest

from cwi_etl.ocr import (
    OCRConfig,
    OCRRequest,
    OCRRunner,
    SimulatedOCRAdapter,
    build_ocr_adapter,
    load_ocr_config,
)


def test_load_ocr_config_requires_provider():
    with pytest.raises(ValueError):
        load_ocr_config({})


def test_build_ocr_adapter_simulated():
    config = OCRConfig(
        provider="simulated",
        enabled=True,
        timeout_seconds=1,
        max_concurrency=1,
        max_bytes=100,
        max_pages=2,
        simulated_text="hello",
    )
    adapter = build_ocr_adapter(config)
    assert adapter is not None


def test_build_ocr_adapter_disabled():
    config = OCRConfig(
        provider="disabled",
        enabled=False,
        timeout_seconds=1,
        max_concurrency=1,
        max_bytes=100,
        max_pages=2,
    )
    assert build_ocr_adapter(config) is None


def test_ocr_runner_enforces_bounds():
    config = OCRConfig(
        provider="simulated",
        enabled=True,
        timeout_seconds=1,
        max_concurrency=1,
        max_bytes=4,
        max_pages=1,
    )
    runner = OCRRunner(config, SimulatedOCRAdapter())
    request = OCRRequest(doc_id="doc-1", content_bytes=b"12345", content_type="image/png", page_count=1)
    with pytest.raises(ValueError):
        asyncio.run(runner.run([request]))


def test_ocr_runner_timeout():
    config = OCRConfig(
        provider="simulated",
        enabled=True,
        timeout_seconds=0.01,
        max_concurrency=1,
        max_bytes=100,
        max_pages=1,
    )

    class SlowAdapter(SimulatedOCRAdapter):
        def extract(self, request: OCRRequest):
            import time

            time.sleep(0.05)
            return super().extract(request)

    runner = OCRRunner(config, SlowAdapter())
    request = OCRRequest(doc_id="doc-1", content_bytes=b"123", content_type="image/png", page_count=1)
    outcome = (asyncio.run(runner.run([request])))[0]
    assert outcome.ok is False
    assert outcome.error == "timeout"
