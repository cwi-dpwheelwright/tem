# Pack Schema Migration & Rollback (Iteration 04)

## Goals
- Provide a predictable path for evolving pack schema.
- Allow rollback to a previous pack version without breaking clients.

## Versioning Strategy
- Use versioned directories under `OPS/cwi-ops/compose/packs/<pack>/<version>/`.
- Maintain a `latest/` symlink or copy for the active version.
- Preserve `anchor.txt` for each version to validate integrity.

## Migration Steps
1. Add new schema version (backward compatible where possible).
2. Publish new pack version alongside the prior version.
3. Update `latest/` to point to the new version.
4. Validate via pack-service health checks and anchor verification.

## Rollback Steps
1. Point `latest/` back to the previous version.
2. Verify anchor and pack integrity.
3. Record rollback in iteration notes.

## Evidence
- Pack artifacts and anchors under `OPS/cwi-ops/compose/packs/`.
- Validation script: `OPS/cwi-ops/compose/pack-service/validate-packs.sh`.
