# cwi-pin-resolver

Source-backed implementation of the MCP pin resolver service.

## Purpose

`cwi-pin-resolver` verifies signed pack pins, resolves the governed active pack
hash, exposes pack-service-compatible pack routes, and records resolver events
to the ledger.

## Runtime behavior

- Reads signed pin files from `PINS_DIR`
- Reads Ed25519 public keys from `KEYS_DIR`
- Reads pack artifacts from `PACKS_DIR`
- Writes audit/resolution events to `LEDGER_DIR`
- Resolves pack version/hash using `PIN_PRIORITY`
- Rejects versioned artifact reads that do not match the currently resolved
  governed version/hash

## Endpoints

- `GET /health`
- `GET /resolve/<pack_id>`
- `GET /packs/index.json`
- `GET /packs/<pack_id>/pack.json`
- `GET /packs/<pack_id>/anchor.txt`
- `GET /packs/<pack_id>/latest/pack.json`
- `GET /packs/<pack_id>/latest/anchor.txt`
- `GET /packs/<pack_id>/<version>/pack.json`
- `GET /packs/<pack_id>/<version>/anchor.txt`

## Environment

- `PORT` default `8090`
- `PINS_DIR` default `/srv/cwi/pins`
- `KEYS_DIR` default `/srv/cwi/pins/keys`
- `PACKS_DIR` default `/srv/cwi/packs`
- `LEDGER_DIR` default `/srv/cwi/ledger`
- `PIN_PRIORITY` default `experiment,approved,latest_stable`

## Local run

```bash
python3 -m pip install -r requirements.txt
python3 pin_resolver.py
```

## Docker build

```bash
docker build -t local/cwi-pin-resolver:phase5 .
```
