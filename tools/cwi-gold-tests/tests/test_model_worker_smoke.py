import os

import pytest
import requests


pytestmark = pytest.mark.smoke


def _model_base_url():
    url = os.environ.get("CWI_MODEL_WORKER_URL", "").strip()
    if not url:
        pytest.skip("CWI_MODEL_WORKER_URL not set")
    return url.rstrip("/")


def _health_base_url():
    url = os.environ.get("CWI_MODEL_WORKER_HEALTH_URL", "").strip()
    if url:
        return url.rstrip("/")

    port = os.environ.get("MODEL_HEALTH_PORT", "").strip()
    if not port:
        pytest.skip("CWI_MODEL_WORKER_HEALTH_URL or MODEL_HEALTH_PORT not set")

    host = os.environ.get("CWI_MODEL_WORKER_HEALTH_HOST", "http://localhost").strip()
    return f"{host.rstrip('/')}:{port}"


def _model_name():
    model = os.environ.get("CWI_MODEL_WORKER_MODEL")
    if not model:
        model = os.environ.get("MODEL_NAME")
    if not model:
        pytest.skip("CWI_MODEL_WORKER_MODEL or MODEL_NAME not set")
    return model


def test_model_worker_health_and_metrics():
    base_url = _health_base_url()

    health = requests.get(f"{base_url}/health", timeout=5)
    assert health.status_code == 200

    metrics = requests.get(f"{base_url}/metrics", timeout=5)
    assert metrics.status_code == 200
    assert "cwi_model_worker_up" in metrics.text


def test_model_worker_basic_generate():
    base_url = _model_base_url()
    model = _model_name()

    payload = {"model": model, "prompt": "Say hello.", "stream": False}
    response = requests.post(f"{base_url}/api/generate", json=payload, timeout=30)
    assert response.status_code == 200
    data = response.json()
    assert "response" in data
