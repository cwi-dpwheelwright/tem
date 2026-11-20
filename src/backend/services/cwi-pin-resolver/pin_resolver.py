from __future__ import annotations

import base64
import hashlib
import json
import os
import socket
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from flask import Flask, Response, jsonify, send_file
from nacl.exceptions import BadSignatureError
from nacl.signing import VerifyKey


@dataclass(frozen=True)
class RuntimeConfig:
    pins_dir: Path
    keys_dir: Path
    packs_dir: Path
    ledger_dir: Path
    pin_priority: list[str]
    node: str


def load_runtime_config() -> RuntimeConfig:
    return RuntimeConfig(
        pins_dir=Path(os.getenv("PINS_DIR", "/srv/cwi/pins")),
        keys_dir=Path(os.getenv("KEYS_DIR", "/srv/cwi/pins/keys")),
        packs_dir=Path(os.getenv("PACKS_DIR", "/srv/cwi/packs")),
        ledger_dir=Path(os.getenv("LEDGER_DIR", "/srv/cwi/ledger")),
        pin_priority=[
            value.strip()
            for value in os.getenv(
                "PIN_PRIORITY", "experiment,approved,latest_stable"
            ).split(",")
            if value.strip()
        ],
        node=socket.gethostname(),
    )


def utc_now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def sha256_hex(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def canonical_pin_payload(pin: dict[str, Any]) -> bytes:
    payload = {
        "schema_version": pin.get("schema_version"),
        "pack_id": pin.get("pack_id"),
        "latest_stable": pin.get("latest_stable"),
        "approved": pin.get("approved"),
        "experiment": pin.get("experiment"),
        "updated_utc": pin.get("updated_utc"),
    }
    return json.dumps(
        payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")


def load_verify_key(config: RuntimeConfig, key_id: str) -> VerifyKey:
    pub_path = config.keys_dir / f"{key_id}.raw.pub"
    if not pub_path.exists():
        raise RuntimeError(f"missing public key: {pub_path}")
    raw = pub_path.read_bytes()
    if len(raw) != 32:
        raise RuntimeError(
            f"invalid public key length (expected 32): {pub_path} len={len(raw)}"
        )
    return VerifyKey(raw)


def verify_pin_signature(
    config: RuntimeConfig, pin: dict[str, Any]
) -> tuple[bool, str, str]:
    if pin.get("schema_version") != "v1":
        raise RuntimeError("schema_version must be v1")

    signing = pin.get("signing") or {}
    alg = signing.get("alg")
    key_id = signing.get("key_id")
    sig_b64 = signing.get("signature_b64")

    payload = canonical_pin_payload(pin)
    payload_sha = sha256_hex(payload)

    if alg != "ed25519-raw":
        raise RuntimeError(f"unsupported signing.alg: {alg} (expected ed25519-raw)")
    if not key_id:
        raise RuntimeError("missing signing.key_id")
    if not sig_b64:
        raise RuntimeError("missing signing.signature_b64")

    signature = base64.b64decode(sig_b64)
    verify_key = load_verify_key(config, key_id)

    try:
        verify_key.verify(payload, signature)
        return True, payload_sha, key_id
    except BadSignatureError:
        return False, payload_sha, key_id


def resolve_hash_from_pin(
    pin: dict[str, Any], priority: list[str]
) -> tuple[str, str]:
    for field in priority:
        value = pin.get(field)
        if value:
            return value, field
    raise RuntimeError(f"no pin set; checked fields={priority}")


def write_ledger_event(config: RuntimeConfig, event: dict[str, Any]) -> None:
    config.ledger_dir.mkdir(parents=True, exist_ok=True)
    path = config.ledger_dir / "pin_resolver_events.jsonl"
    with path.open("a", encoding="utf-8") as handle:
        handle.write(
            json.dumps(event, separators=(",", ":"), ensure_ascii=False) + "\n"
        )


def resolve_pack(config: RuntimeConfig, pack_id: str) -> dict[str, Any]:
    pin_path = config.pins_dir / f"{pack_id}.json"
    if not pin_path.exists():
        raise FileNotFoundError(str(pin_path))

    pin = json.loads(pin_path.read_text(encoding="utf-8"))

    signature_valid, payload_sha, key_id = verify_pin_signature(config, pin)
    if not signature_valid:
        event = {
            "event": "PIN_RESOLVE_DENIED_BAD_SIGNATURE",
            "pack_id": pack_id,
            "node": config.node,
            "utc": utc_now(),
            "pin_key_id": key_id,
            "pin_payload_sha256": payload_sha,
            "pin_path": str(pin_path),
        }
        write_ledger_event(config, event)
        raise PermissionError("bad_signature")

    resolved_hash, source_field = resolve_hash_from_pin(pin, config.pin_priority)

    anchor_path = config.packs_dir / pack_id / resolved_hash / "anchor.txt"
    pack_json_path = config.packs_dir / pack_id / resolved_hash / "pack.json"
    pack_present_locally = anchor_path.exists() and pack_json_path.exists()

    event = {
        "event": "PIN_RESOLVED",
        "pack_id": pack_id,
        "resolved_hash": resolved_hash,
        "source_pin_field": source_field,
        "signature_valid": True,
        "pin_key_id": key_id,
        "pin_payload_sha256": payload_sha,
        "pack_present_locally": bool(pack_present_locally),
        "node": config.node,
        "utc": utc_now(),
    }
    write_ledger_event(config, event)

    return {
        "pack_id": pack_id,
        "resolved_hash": resolved_hash,
        "source_pin_field": source_field,
        "pin_key_id": key_id,
        "pin_payload_sha256": payload_sha,
        "pack_present_locally": bool(pack_present_locally),
        "utc": utc_now(),
        "node": config.node,
    }


def _latest_pack_file_response(
    config: RuntimeConfig, pack_id: str, filename: str
) -> Response:
    if filename not in ("pack.json", "anchor.txt"):
        return (
            jsonify(
                {
                    "code": "not_found",
                    "message": "Resource not found.",
                    "error": "not_found",
                }
            ),
            404,
        )

    try:
        info = resolve_pack(config, pack_id)
    except FileNotFoundError:
        return (
            jsonify(
                {
                    "code": "not_found",
                    "message": "Resource not found.",
                    "error": "not_found",
                }
            ),
            404,
        )
    except PermissionError:
        return (
            jsonify(
                {
                    "code": "forbidden",
                    "message": "Bad signature.",
                    "error": "forbidden",
                }
            ),
            403,
        )
    except Exception:
        return (
            jsonify(
                {
                    "code": "error",
                    "message": "Resolver error.",
                    "error": "error",
                }
            ),
            500,
        )

    path = config.packs_dir / pack_id / info["resolved_hash"] / filename
    if not path.exists():
        return (
            jsonify(
                {
                    "code": "not_found",
                    "message": "Resource not found.",
                    "error": "not_found",
                }
            ),
            404,
        )
    return send_file(path)


def _resolved_version_response(
    config: RuntimeConfig, pack_id: str, version: str, filename: str
) -> Response:
    if filename not in ("pack.json", "anchor.txt"):
        return (
            jsonify(
                {
                    "code": "not_found",
                    "message": "Resource not found.",
                    "error": "not_found",
                }
            ),
            404,
        )

    try:
        info = resolve_pack(config, pack_id)
    except FileNotFoundError:
        return jsonify({"error": "pin_not_found", "pack_id": pack_id}), 404
    except PermissionError:
        return jsonify({"error": "bad_signature", "pack_id": pack_id}), 403
    except Exception as exc:
        return (
            jsonify(
                {
                    "error": "resolver_error",
                    "pack_id": pack_id,
                    "message": str(exc),
                }
            ),
            500,
        )

    resolved_hash = info["resolved_hash"]
    if version != resolved_hash:
        write_ledger_event(
            config,
            {
                "event": "PIN_RESOLVE_DENIED_VERSION_BYPASS",
                "pack_id": pack_id,
                "requested_version": version,
                "resolved_hash": resolved_hash,
                "node": config.node,
                "utc": utc_now(),
            },
        )
        return (
            jsonify(
                {
                    "code": "not_found",
                    "message": "Resource not found.",
                    "error": "not_found",
                }
            ),
            404,
        )

    path = config.packs_dir / pack_id / resolved_hash / filename
    if not path.exists():
        return (
            jsonify(
                {
                    "code": "not_found",
                    "message": "Resource not found.",
                    "error": "not_found",
                }
            ),
            404,
        )
    return send_file(path)


def create_app(config: RuntimeConfig | None = None) -> Flask:
    runtime = config or load_runtime_config()
    app = Flask(__name__)

    @app.get("/health")
    def health() -> Response:
        return jsonify({"status": "ok", "utc": utc_now(), "node": runtime.node})

    @app.get("/resolve/<pack_id>")
    def resolve_api(pack_id: str) -> Response:
        try:
            info = resolve_pack(runtime, pack_id)
            return jsonify(info)
        except FileNotFoundError:
            return jsonify({"error": "pin_not_found", "pack_id": pack_id}), 404
        except PermissionError:
            return jsonify({"error": "bad_signature", "pack_id": pack_id}), 403
        except Exception as exc:
            return (
                jsonify(
                    {
                        "error": "resolver_error",
                        "pack_id": pack_id,
                        "message": str(exc),
                    }
                ),
                500,
            )

    @app.get("/packs/index.json")
    def packs_index() -> Response:
        index_path = runtime.packs_dir / "index.json"
        if not index_path.exists():
            return jsonify({"error": "index_not_found"}), 404
        return Response(
            index_path.read_text(encoding="utf-8"), mimetype="application/json"
        )

    @app.get("/packs/<pack_id>/latest/<path:filename>")
    def packs_latest(pack_id: str, filename: str) -> Response:
        return _latest_pack_file_response(runtime, pack_id, filename)

    @app.get("/packs/<pack_id>/<filename>")
    def packs_latest_compat(pack_id: str, filename: str) -> Response:
        return _latest_pack_file_response(runtime, pack_id, filename)

    @app.get("/packs/<pack_id>/<version>/<path:filename>")
    def packs_version(pack_id: str, version: str, filename: str) -> Response:
        return _resolved_version_response(runtime, pack_id, version, filename)

    return app


APP = create_app()


if __name__ == "__main__":
    APP.run(host="0.0.0.0", port=int(os.getenv("PORT", "8090")))
