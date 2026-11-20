import os

import pytest
import requests


pytestmark = pytest.mark.smoke


def _base_url():
    url = os.environ.get("CWI_VECTOR_SHARD_URL") or os.environ.get("VECTOR_SHARD_1_URL")
    if not url:
        pytest.skip("CWI_VECTOR_SHARD_URL or VECTOR_SHARD_1_URL not set")
    return url.rstrip("/")


def test_vector_shard_health_and_collections():
    base_url = _base_url()

    health = requests.get(f"{base_url}/healthz", timeout=5)
    if health.status_code == 404:
        health = requests.get(f"{base_url}/health", timeout=5)
    assert health.status_code == 200

    collections = requests.get(f"{base_url}/collections", timeout=5)
    assert collections.status_code == 200

    collection = os.environ.get("VECTOR_COLLECTION")
    require_collection = os.environ.get("CWI_VECTOR_REQUIRE_COLLECTION", "").lower() in ("1", "true", "yes")
    if collection:
        response = requests.get(f"{base_url}/collections/{collection}", timeout=5)
        if require_collection:
            assert response.status_code == 200
        else:
            assert response.status_code in (200, 404)
