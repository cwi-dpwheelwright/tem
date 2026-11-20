from __future__ import annotations

import base64
import json
from pathlib import Path
import hashlib

from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives import serialization

from node_agent_runtime.bundle import load_sealed_bundle
from node_agent_runtime.config import BundleConfig, UpdateConfig
from node_agent_runtime.config import ConfigError
from node_agent_runtime.upgrade import download_bundle, resolve_update_paths, upgrade


def _sealed_bundle_payload(files: dict[str, str]) -> str:
    encoded = {name: base64.b64encode(content.encode("utf-8")).decode("utf-8") for name, content in files.items()}
    blob = json.dumps({"files": encoded}, sort_keys=True).encode("utf-8")
    return base64.b64encode(blob).decode("utf-8")


def _sha256_bytes(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def _manifest_payload(version: str, files: dict[str, str]) -> dict[str, object]:
    return {
        "version": version,
        "files": {name: _sha256_bytes(content) for name, content in files.items()},
    }


def _bundle_json(
    version: str,
    app_value: str,
    *,
    include_manifest: bool = False,
    manifest_signature: bytes | None = None,
) -> bytes:
    files = {"version.txt": version, "app.txt": app_value}
    if include_manifest:
        manifest = _manifest_payload(version, files)
        if manifest_signature is not None:
            manifest["signature"] = base64.b64encode(manifest_signature).decode("utf-8")
        files["manifest.json"] = json.dumps(manifest, sort_keys=True)
    payload = _sealed_bundle_payload(files)
    bundle = {"bundle_id": version, "payload": payload, "provider": "noop"}
    return json.dumps(bundle, indent=2, sort_keys=True).encode("utf-8")


def _write_current(current_dir: Path, version: str, app_value: str) -> None:
    current_dir.mkdir(parents=True, exist_ok=True)
    (current_dir / "version.txt").write_text(version, encoding="utf-8")
    (current_dir / "app.txt").write_text(app_value, encoding="utf-8")


def test_upgrade_success(tmp_path):
    state_dir = tmp_path / "state"
    updates_dir = tmp_path / "updates"
    current_dir = tmp_path / "runtime" / "current"
    _write_current(current_dir, "v1", "old")

    update_config = UpdateConfig(
        updates_dir=updates_dir,
        current_dir=current_dir,
        require_manifest=True,
    )
    paths = resolve_update_paths(state_dir, update_config)
    bundle_bytes = _bundle_json("v2", "new", include_manifest=True)

    def download_fn(dest_path: Path, resume_from: int) -> None:
        mode = "ab" if resume_from else "wb"
        with dest_path.open(mode) as handle:
            handle.write(bundle_bytes[resume_from:])

    state = upgrade(
        paths,
        bundle_config=BundleConfig(crypto_provider="noop"),
        target_version="v2",
        download_fn=download_fn,
        health_check_fn=lambda _path: True,
        update_config=update_config,
    )

    assert state.status == "active"
    assert (paths.current_dir / "app.txt").read_text(encoding="utf-8") == "new"
    assert (paths.previous_dir / "app.txt").read_text(encoding="utf-8") == "old"


def test_upgrade_fail_rolls_back(tmp_path):
    state_dir = tmp_path / "state"
    updates_dir = tmp_path / "updates"
    current_dir = tmp_path / "runtime" / "current"
    _write_current(current_dir, "v1", "old")

    update_config = UpdateConfig(
        updates_dir=updates_dir,
        current_dir=current_dir,
        require_manifest=True,
    )
    paths = resolve_update_paths(state_dir, update_config)
    bundle_bytes = _bundle_json("v2", "new", include_manifest=True)

    def download_fn(dest_path: Path, resume_from: int) -> None:
        mode = "ab" if resume_from else "wb"
        with dest_path.open(mode) as handle:
            handle.write(bundle_bytes[resume_from:])

    state = upgrade(
        paths,
        bundle_config=BundleConfig(crypto_provider="noop"),
        target_version="v2",
        download_fn=download_fn,
        health_check_fn=lambda _path: False,
        update_config=update_config,
    )

    assert state.status == "rolled_back"
    assert (paths.current_dir / "app.txt").read_text(encoding="utf-8") == "old"


def test_download_resume(tmp_path):
    state_dir = tmp_path / "state"
    updates_dir = tmp_path / "updates"
    update_config = UpdateConfig(updates_dir=updates_dir)
    paths = resolve_update_paths(state_dir, update_config)
    bundle_bytes = _bundle_json("v2", "new")

    calls = {"count": 0, "resume_from": None}

    def download_fn(dest_path: Path, resume_from: int) -> None:
        calls["count"] += 1
        calls["resume_from"] = resume_from
        if calls["count"] == 1:
            half = len(bundle_bytes) // 2
            with dest_path.open("wb") as handle:
                handle.write(bundle_bytes[:half])
            raise ConnectionError("interrupted")
        with dest_path.open("ab") as handle:
            handle.write(bundle_bytes[resume_from:])

    try:
        download_bundle(paths, target_version="v2", download_fn=download_fn)
        assert False, "expected interruption"
    except ConnectionError:
        pass

    result_path = download_bundle(paths, target_version="v2", download_fn=download_fn)
    assert calls["resume_from"] is not None
    assert result_path.read_bytes() == bundle_bytes
    sealed = load_sealed_bundle(result_path)
    assert sealed.bundle_id == "v2"


def test_upgrade_requires_manifest(tmp_path):
    state_dir = tmp_path / "state"
    updates_dir = tmp_path / "updates"
    current_dir = tmp_path / "runtime" / "current"
    _write_current(current_dir, "v1", "old")

    update_config = UpdateConfig(
        updates_dir=updates_dir,
        current_dir=current_dir,
        require_manifest=True,
    )
    paths = resolve_update_paths(state_dir, update_config)
    bundle_bytes = _bundle_json("v2", "new")

    def download_fn(dest_path: Path, resume_from: int) -> None:
        mode = "ab" if resume_from else "wb"
        with dest_path.open(mode) as handle:
            handle.write(bundle_bytes[resume_from:])

    try:
        upgrade(
            paths,
            bundle_config=BundleConfig(crypto_provider="noop"),
            target_version="v2",
            download_fn=download_fn,
            health_check_fn=lambda _path: True,
            update_config=update_config,
        )
        assert False, "expected manifest error"
    except ConfigError as exc:
        assert "manifest" in str(exc).lower()


def test_upgrade_manifest_signature(tmp_path):
    state_dir = tmp_path / "state"
    updates_dir = tmp_path / "updates"
    current_dir = tmp_path / "runtime" / "current"
    _write_current(current_dir, "v1", "old")

    private_key = ed25519.Ed25519PrivateKey.generate()
    public_key_pem = private_key.public_key().public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    pub_path = tmp_path / "pub.pem"
    pub_path.write_bytes(public_key_pem)

    update_config = UpdateConfig(
        updates_dir=updates_dir,
        current_dir=current_dir,
        require_manifest=True,
        require_signature=True,
        signature_public_key_path=pub_path,
    )
    paths = resolve_update_paths(state_dir, update_config)

    manifest = _manifest_payload("v2", {"version.txt": "v2", "app.txt": "new"})
    signature = private_key.sign(
        json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode("utf-8")
    )
    bundle_bytes = _bundle_json("v2", "new", include_manifest=True, manifest_signature=signature)

    def download_fn(dest_path: Path, resume_from: int) -> None:
        mode = "ab" if resume_from else "wb"
        with dest_path.open(mode) as handle:
            handle.write(bundle_bytes[resume_from:])

    state = upgrade(
        paths,
        bundle_config=BundleConfig(crypto_provider="noop"),
        target_version="v2",
        download_fn=download_fn,
        health_check_fn=lambda _path: True,
        update_config=update_config,
    )

    assert state.status == "active"
