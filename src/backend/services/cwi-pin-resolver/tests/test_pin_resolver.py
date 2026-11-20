from __future__ import annotations

import base64
import json
import sys
import tempfile
import unittest
from pathlib import Path

from nacl.signing import SigningKey

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from pin_resolver import RuntimeConfig, canonical_pin_payload, create_app


class PinResolverTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmpdir = tempfile.TemporaryDirectory()
        root = Path(self.tmpdir.name)
        self.pins_dir = root / "pins"
        self.keys_dir = self.pins_dir / "keys"
        self.packs_dir = root / "packs"
        self.ledger_dir = root / "ledger"
        self.keys_dir.mkdir(parents=True)
        self.packs_dir.mkdir(parents=True)

        self.signing_key = SigningKey.generate()
        verify_key = self.signing_key.verify_key.encode()
        (self.keys_dir / "teacher.raw.pub").write_bytes(verify_key)

        self.config = RuntimeConfig(
            pins_dir=self.pins_dir,
            keys_dir=self.keys_dir,
            packs_dir=self.packs_dir,
            ledger_dir=self.ledger_dir,
            pin_priority=["experiment", "approved", "latest_stable"],
            node="test-node",
        )
        self.client = create_app(self.config).test_client()

    def tearDown(self) -> None:
        self.tmpdir.cleanup()

    def _write_pin(self, pack_id: str, approved_hash: str, *, tamper: bool = False) -> None:
        pin = {
            "schema_version": "v1",
            "pack_id": pack_id,
            "latest_stable": None,
            "approved": approved_hash,
            "experiment": None,
            "updated_utc": "2026-03-22T00:00:00Z",
        }
        payload = canonical_pin_payload(pin)
        signature = self.signing_key.sign(payload).signature
        if tamper:
            pin["approved"] = f"{approved_hash}-tampered"
        pin["signing"] = {
            "alg": "ed25519-raw",
            "key_id": "teacher",
            "signature_b64": base64.b64encode(signature).decode("ascii"),
        }
        (self.pins_dir / f"{pack_id}.json").write_text(
            json.dumps(pin), encoding="utf-8"
        )

    def test_resolve_returns_selected_hash(self) -> None:
        pack_id = "Operations-Pack"
        approved_hash = "abc123"
        pack_dir = self.packs_dir / pack_id / approved_hash
        pack_dir.mkdir(parents=True)
        (pack_dir / "pack.json").write_text('{"ok":true}', encoding="utf-8")
        (pack_dir / "anchor.txt").write_text("abc123", encoding="utf-8")
        self._write_pin(pack_id, approved_hash)

        response = self.client.get(f"/resolve/{pack_id}")
        self.assertEqual(response.status_code, 200)
        body = response.get_json()
        self.assertEqual(body["resolved_hash"], approved_hash)
        self.assertEqual(body["source_pin_field"], "approved")
        self.assertTrue(body["pack_present_locally"])

    def test_bad_signature_returns_forbidden(self) -> None:
        self._write_pin("Operations-Pack", "abc123", tamper=True)
        response = self.client.get("/resolve/Operations-Pack")
        self.assertEqual(response.status_code, 403)
        self.assertEqual(response.get_json()["error"], "bad_signature")

    def test_pack_routes_support_latest_and_root_compat(self) -> None:
        pack_id = "Operations-Pack"
        approved_hash = "abc123"
        pack_dir = self.packs_dir / pack_id / approved_hash
        pack_dir.mkdir(parents=True)
        (pack_dir / "pack.json").write_text('{"name":"ops"}', encoding="utf-8")
        (pack_dir / "anchor.txt").write_text("abc123", encoding="utf-8")
        self._write_pin(pack_id, approved_hash)

        latest = self.client.get(f"/packs/{pack_id}/latest/pack.json")
        compat = self.client.get(f"/packs/{pack_id}/pack.json")
        try:
            self.assertEqual(latest.status_code, 200)
            self.assertEqual(compat.status_code, 200)
            self.assertEqual(latest.get_data(as_text=True), '{"name":"ops"}')
            self.assertEqual(compat.get_data(as_text=True), '{"name":"ops"}')
        finally:
            latest.close()
            compat.close()

    def test_version_route_rejects_non_resolved_version(self) -> None:
        pack_id = "Operations-Pack"
        approved_hash = "abc123"
        stale_hash = "stale456"

        approved_dir = self.packs_dir / pack_id / approved_hash
        approved_dir.mkdir(parents=True)
        (approved_dir / "pack.json").write_text('{"name":"ops"}', encoding="utf-8")
        (approved_dir / "anchor.txt").write_text(approved_hash, encoding="utf-8")

        stale_dir = self.packs_dir / pack_id / stale_hash
        stale_dir.mkdir(parents=True)
        (stale_dir / "pack.json").write_text('{"name":"stale"}', encoding="utf-8")
        (stale_dir / "anchor.txt").write_text(stale_hash, encoding="utf-8")

        self._write_pin(pack_id, approved_hash)

        response = self.client.get(f"/packs/{pack_id}/{stale_hash}/pack.json")
        self.assertEqual(response.status_code, 404)


if __name__ == "__main__":
    unittest.main()
