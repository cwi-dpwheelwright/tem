from __future__ import annotations

from urllib import error, request

from node_agent_runtime.config import (
    AgentConfig,
    ActivationConfig,
    DegradationConfig,
    EnrollmentConfig,
    EnrollmentTlsConfig,
    IdentityConfig,
    KillSwitchConfig,
    StorageConfig,
)
from node_agent_runtime.kill_switch import (
    KillSwitchState,
    _build_admin_url,
    fetch_kill_switch_state,
    load_kill_switch_state,
    poll_kill_switch_state,
    save_kill_switch_state,
    sync_kill_switch_state,
)


def _base_config(tmp_path, *, kill_switch: KillSwitchConfig) -> AgentConfig:
    return AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=tmp_path),
        degradation=DegradationConfig(),
        activation=ActivationConfig(),
        kill_switch=kill_switch,
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )


def test_build_admin_url_appends_tenant():
    url = _build_admin_url("https://host/api/admin/kill_switch", "tenant-1")
    assert "tenant_id=tenant-1" in url


def test_build_admin_url_preserves_query():
    url = _build_admin_url("https://host/api/admin/kill_switch?foo=bar", "tenant-1")
    assert "foo=bar" in url
    assert "tenant_id=tenant-1" in url


def test_fetch_kill_switch_state_sends_break_glass(monkeypatch):
    monkeypatch.setenv("BG_TOKEN", "token-123")
    config = KillSwitchConfig(
        admin_url="https://host/api/admin/kill_switch",
        tenant_id="tenant-1",
        break_glass_token_env="BG_TOKEN",
    )

    captured = {}

    class DummyResponse:
        def __enter__(self):
            return self

        def __exit__(self, exc_type, exc, tb):
            return False

        def read(self):
            return b'{"enabled": true, "irreversible": false, "updated_utc": "2026-02-01T00:00:00Z"}'

    def fake_urlopen(req, timeout=10):
        captured["headers"] = {k.lower(): v for k, v in req.header_items()}
        return DummyResponse()

    monkeypatch.setattr(request, "urlopen", fake_urlopen)

    state = fetch_kill_switch_state(config)
    assert captured["headers"]["x-break-glass-token"] == "token-123"
    assert state.enabled is True


def test_poll_kill_switch_state_writes_state(tmp_path):
    kill_config = KillSwitchConfig(state_path=tmp_path / "state.json")
    config = _base_config(tmp_path, kill_switch=kill_config)
    calls = {"count": 0}

    def fake_fetch(_config):
        calls["count"] += 1
        return KillSwitchState(
            enabled=True,
            irreversible=False,
            updated_utc="2026-02-01T00:00:00Z",
            source="remote",
        )

    poll_kill_switch_state(
        config,
        interval_seconds=1,
        max_iterations=2,
        sleep_fn=lambda _: None,
        fetch_fn=fake_fetch,
    )

    assert calls["count"] == 2
    assert (tmp_path / "state.json").exists()


def test_sync_kill_switch_state_keeps_irreversible(tmp_path):
    state_path = tmp_path / "state.json"
    config = _base_config(tmp_path, kill_switch=KillSwitchConfig(state_path=state_path))
    save_kill_switch_state(
        state_path,
        KillSwitchState(
            enabled=True,
            irreversible=True,
            updated_utc="2026-02-01T00:00:00Z",
            source="local",
        ),
    )

    def fake_fetch(_config):
        return KillSwitchState(
            enabled=False,
            irreversible=False,
            updated_utc="2026-02-01T00:00:10Z",
            source="remote",
        )

    state, _ = sync_kill_switch_state(config, fetch_fn=fake_fetch)
    assert state.enabled is True
    assert state.irreversible is True
    persisted = load_kill_switch_state(state_path)
    assert persisted is not None
    assert persisted.enabled is True
    assert persisted.irreversible is True


def test_fetch_kill_switch_state_retries(monkeypatch):
    config = KillSwitchConfig(
        admin_url="https://host/api/admin/kill_switch",
        tenant_id="tenant-1",
        retry_attempts=2,
        retry_backoff_seconds=0,
    )

    calls = {"count": 0}

    class DummyResponse:
        def __enter__(self):
            return self

        def __exit__(self, exc_type, exc, tb):
            return False

        def read(self):
            return b'{"enabled": true, "irreversible": false, "updated_utc": "2026-02-01T00:00:00Z"}'

    def fake_urlopen(req, timeout=10):
        calls["count"] += 1
        if calls["count"] == 1:
            raise error.URLError("temporary failure")
        return DummyResponse()

    monkeypatch.setattr(request, "urlopen", fake_urlopen)

    state = fetch_kill_switch_state(config)
    assert calls["count"] == 2
    assert state.enabled is True
