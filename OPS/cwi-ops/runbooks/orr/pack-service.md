# ORR Runbook: cwi-pack-service (container: internal-01-core-pack-service)

## Pre-flight
- Verify packs exist and anchors are valid.
- Confirm `PACKS_DIR` is mounted read-only.

## Startup
1. Deploy container via compose bundle.
2. Confirm `/packs/{pack}/pack.json` and `/packs/{pack}/anchor.txt` return 200.
3. Confirm versioned paths `/packs/{pack}/{version}/pack.json` and `/packs/{pack}/latest/anchor.txt` return 200.

## Readiness
- Nginx serves pack artifacts without directory traversal.

## Rollback
- Stop container.
- Restore previous pack bundle.

## Evidence
- Capture HTTP responses and pack checksum logs.
