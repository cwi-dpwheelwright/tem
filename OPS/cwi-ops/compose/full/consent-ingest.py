from __future__ import annotations

import json
import logging
import os
import shutil
import time
from pathlib import Path
from typing import Any, Iterable, List

from cwi_etl.consent_ingest import ConsentIngestItem, ConsentIngestJob
from cwi_etl.consent_store import ConsentStore, ConsentStoreConfig


def main() -> int:
    logging.basicConfig(level=os.environ.get("CWI_CONSENT_INGEST_LOG_LEVEL", "INFO"))
    logger = logging.getLogger("consent_ingest_runner")

    input_dir = Path(os.environ.get("CWI_CONSENT_INGEST_INPUT_DIR", "/consent-input"))
    archive_dir = Path(os.environ.get("CWI_CONSENT_INGEST_ARCHIVE_DIR", "/consent-archive"))
    error_dir = Path(os.environ.get("CWI_CONSENT_INGEST_ERROR_DIR", "/consent-error"))
    source_type = os.environ.get("CWI_CONSENT_INGEST_SOURCE_TYPE", "operator_memory_ingest")
    source_id = os.environ.get("CWI_CONSENT_INGEST_SOURCE_ID", "operator-memory")
    file_glob = os.environ.get("CWI_CONSENT_INGEST_FILE_GLOB", "*.json*")
    interval_seconds = int(os.environ.get("CWI_CONSENT_INGEST_INTERVAL_SECONDS", "300"))
    run_once = _flag_enabled("CWI_CONSENT_INGEST_RUN_ONCE")

    input_dir.mkdir(parents=True, exist_ok=True)
    archive_dir.mkdir(parents=True, exist_ok=True)
    error_dir.mkdir(parents=True, exist_ok=True)

    store = ConsentStore(ConsentStoreConfig.from_env())
    job = ConsentIngestJob(store, logger=logger)

    while True:
        files = sorted(input_dir.glob(file_glob))
        if not files:
            logger.info("consent_ingest_runner no_files interval_seconds=%s", interval_seconds)
        for path in files:
            try:
                items = _load_items(path, source_type, source_id)
                if not items:
                    logger.warning("consent_ingest_runner empty_file file=%s", path)
                    _move_file(path, archive_dir)
                    continue
                metrics = job.run(items)
                logger.info(
                    "consent_ingest_runner file=%s received=%s stored=%s failed=%s",
                    path.name,
                    metrics.received,
                    metrics.stored,
                    metrics.failed,
                )
                target_dir = error_dir if metrics.failed > 0 else archive_dir
                _move_file(path, target_dir)
            except Exception as exc:
                logger.exception("consent_ingest_runner failed file=%s error=%s", path.name, exc)
                _move_file(path, error_dir)

        if run_once:
            break
        time.sleep(interval_seconds)

    return 0


def _load_items(path: Path, default_source_type: str, default_source_id: str) -> List[ConsentIngestItem]:
    records: Iterable[Any]
    raw = path.read_text(encoding="utf-8")
    if path.suffix.lower() == ".jsonl":
        records = [json.loads(line) for line in raw.splitlines() if line.strip()]
    else:
        payload = json.loads(raw)
        if isinstance(payload, dict) and isinstance(payload.get("items"), list):
            records = payload["items"]
        elif isinstance(payload, list):
            records = payload
        else:
            records = [payload]

    items: List[ConsentIngestItem] = []
    for index, record in enumerate(records):
        if not isinstance(record, dict):
            raise ValueError(f"invalid consent record at index {index}")
        payload = record.get("payload") if "payload" in record else record
        if not isinstance(payload, dict):
            raise ValueError(f"invalid consent payload at index {index}")
        doc_id = record.get("doc_id") or f"{path.stem}-{index}"
        items.append(
            ConsentIngestItem(
                doc_id=str(doc_id),
                payload=payload,
                source_type=record.get("source_type", default_source_type),
                source_id=record.get("source_id", default_source_id),
                created_utc=record.get("created_utc"),
            )
        )
    return items


def _move_file(path: Path, target_dir: Path) -> None:
    target = target_dir / path.name
    if target.exists():
        target = target_dir / f"{path.stem}-{int(time.time())}{path.suffix}"
    shutil.move(str(path), str(target))


def _flag_enabled(name: str) -> bool:
    value = os.environ.get(name, "").strip().lower()
    return value in {"1", "true", "yes", "on"}


if __name__ == "__main__":
    raise SystemExit(main())
