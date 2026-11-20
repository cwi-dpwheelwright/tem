import json
import os
import uuid
from urllib.request import Request, urlopen

import pytest


pytestmark = pytest.mark.integration


def test_ingest_document_smoke() -> None:
    if os.environ.get("CWI_ETL_SMOKE_TEST") != "1":
        pytest.skip("CWI_ETL_SMOKE_TEST not set")

    base_url = os.environ.get("CWI_ETL_ORCHESTRATOR_URL")
    if not base_url:
        pytest.skip("CWI_ETL_ORCHESTRATOR_URL not set")

    token = os.environ.get("CWI_ETL_AUTH_TOKEN")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"

    payload = {
        "version": "v1",
        "tenant_id": os.environ.get("CWI_ETL_TENANT_ID", "tenant-1"),
        "idempotency_key": str(uuid.uuid4()),
        "tags": ["etl-smoke"],
        "document": {
            "doc_id": f"doc-{uuid.uuid4()}",
            "title": "Smoke Test",
            "mime_type": "text/plain",
            "content": "Smoke test content.",
            "content_encoding": "plain",
            "source": "etl-smoke",
            "metadata": {"source": "etl-smoke"},
        },
    }

    request = Request(
        f"{base_url.rstrip('/')}/api/ingest/document",
        data=json.dumps(payload).encode("utf-8"),
        headers=headers,
        method="POST",
    )

    with urlopen(request, timeout=15) as response:
        assert response.status in (200, 202)
