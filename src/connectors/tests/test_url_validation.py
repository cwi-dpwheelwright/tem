import pytest

from cwi_connectors.url_validation import validate_external_url, validate_http_url


def test_validate_http_url_allows_http_by_default():
    assert validate_http_url("http://example.com", "url") == "http://example.com"


def test_validate_http_url_rejects_http_when_disallowed():
    with pytest.raises(ValueError):
        validate_http_url("http://example.com", "url", allow_http=False)


def test_validate_external_url_blocks_insecure_and_private(monkeypatch):
    monkeypatch.delenv("CWI_CONNECTORS_ALLOW_INSECURE_HTTP", raising=False)
    monkeypatch.delenv("CWI_CONNECTORS_ALLOW_PRIVATE_URLS", raising=False)
    with pytest.raises(ValueError):
        validate_external_url("http://example.com", "url")
    with pytest.raises(ValueError):
        validate_external_url("https://localhost", "url")


def test_validate_external_url_allows_overrides(monkeypatch):
    monkeypatch.setenv("CWI_CONNECTORS_ALLOW_INSECURE_HTTP", "true")
    monkeypatch.setenv("CWI_CONNECTORS_ALLOW_PRIVATE_URLS", "true")
    assert validate_external_url("http://localhost", "url") == "http://localhost"
