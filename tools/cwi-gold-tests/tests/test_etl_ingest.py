import json
import os

import pytest
import requests


def _env(name: str):
    value = os.environ.get(name)
    return value if value else None


def _load_json_env(name: str):
    raw = _env(name)
    if not raw:
        return None
    return json.loads(raw)


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return {}
    return {"Authorization": f"Bearer {token}"}


@pytest.mark.integration
def test_etl_ingest_search_trace_links():
    ingest_url = _env("CWI_ETL_INGEST_URL")
    vector_url = _env("CWI_VECTOR_SEARCH_URL")
    ask_url = _env("CWI_ASK_URL")
    trace_url_template = _env("CWI_TRACE_LOOKUP_URL_TEMPLATE")

    if not ingest_url:
        pytest.skip("CWI_ETL_INGEST_URL not set")

    payload = _load_json_env("CWI_ETL_INGEST_PAYLOAD_JSON") or {
        "version": "v1",
        "tenant_id": os.environ.get("CWI_TENANT_ID", "tenant-1"),
        "idempotency_key": "etl-test-1",
        "document": {
            "doc_id": "doc-test-1",
            "title": "Test Document",
            "mime_type": "text/plain",
            "content": "Test document content for ingestion.",
            "content_encoding": "plain",
            "source": "gold-tests",
        },
    }

    resp = requests.post(ingest_url, json=payload, timeout=30)
    assert resp.status_code == 200
    ingest_response = resp.json()
    doc_id = ingest_response.get("doc_id") or payload["document"]["doc_id"]
    trace_id = ingest_response.get("trace_id")

    if ask_url:
        ask_payload = _load_json_env("CWI_ASK_PAYLOAD_JSON") or {
            "tenant_id": payload["tenant_id"],
            "user_id": "etl-test",
            "question": "Find the test document.",
            "policy_mode": "observe",
        }
        ask_resp = requests.post(ask_url, json=ask_payload, headers=_auth_headers(), timeout=30)
        assert ask_resp.status_code == 200
        ask_json = ask_resp.json()
        citations = json.dumps(ask_json.get("citations", []))
        sources = json.dumps(ask_json.get("trace", {}).get("sources", []))
        assert doc_id in citations or doc_id in sources
    else:
        if not vector_url:
            pytest.skip("CWI_VECTOR_SEARCH_URL not set")

        vector_payload = _load_json_env("CWI_VECTOR_SEARCH_PAYLOAD_JSON")
        if vector_payload is None:
            pytest.skip("CWI_VECTOR_SEARCH_PAYLOAD_JSON not set")

        vector_resp = requests.post(vector_url, json=vector_payload, timeout=30)
        assert vector_resp.status_code == 200
        vector_text = json.dumps(vector_resp.json())
        assert doc_id in vector_text

    if not trace_url_template or not trace_id:
        pytest.skip("CWI_TRACE_LOOKUP_URL_TEMPLATE or trace_id missing")

    trace_url = trace_url_template.format(trace_id=trace_id)
    trace_resp = requests.get(trace_url, timeout=30)
    assert trace_resp.status_code == 200
    trace_text = json.dumps(trace_resp.json())
    assert doc_id in trace_text
