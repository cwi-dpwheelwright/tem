import json
import os
import uuid
import datetime

import pytest
import requests


pytestmark = [pytest.mark.integration]


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return None
    return {"Authorization": f"Bearer {token}"}


def _base_url(base_url):
    override = os.environ.get("CWI_PROVENANCE_BASE_URL") or os.environ.get("CWI_PROVENANCE_URL")
    if override:
        return override.rstrip("/")
    return base_url.rstrip("/")


def _load_payload():
    raw = os.environ.get("CWI_PROVENANCE_PAYLOAD_JSON")
    if not raw:
        return None
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        pass
    if os.path.exists(raw):
        with open(raw, "r", encoding="utf-8") as handle:
            return json.loads(handle.read())
    return None


def _default_payload():
    now = datetime.datetime.now(tz=datetime.timezone.utc).isoformat()
    tenant_id = os.environ.get("CWI_TENANT_ID", "tenant-1")
    connector_id = os.environ.get("CWI_PROVENANCE_CONNECTOR_ID", "gold-tests")
    source = os.environ.get("CWI_PROVENANCE_SOURCE", "gold-tests")
    return {
        "version": "v1",
        "envelope_id": f"gold-tests:{uuid.uuid4()}",
        "emitted_utc": now,
        "tenant_id": tenant_id,
        "connector_id": connector_id,
        "source": source,
        "summary": {
            "doc_ids": ["doc-gold-1"],
            "chunk_ids": ["chunk-gold-1"],
            "redaction_count": 0,
        },
    }


def test_provenance_ingest_endpoint_accepts_envelope(base_url):
    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = _load_payload() or _default_payload()
    base = _base_url(base_url)
    idempotency = payload.get("envelope_id") or f"gold-tests:{uuid.uuid4()}"
    headers = dict(headers)
    headers["Idempotency-Key"] = idempotency

    resp = requests.post(f"{base}/api/ingest/provenance", json=payload, headers=headers, timeout=20)
    if resp.status_code in (404, 405):
        pytest.skip("Provenance ingest endpoint not available")
    assert resp.status_code < 400
