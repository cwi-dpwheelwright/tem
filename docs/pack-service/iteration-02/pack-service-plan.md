# Pack Service Plan (Iteration 02)

## Pack registry
- `/packs/index.json` with dependencies, versions, artifact links.
- Cache headers with conservative revalidation.

## Versioned layouts
- `packs/{pack}/{version}/pack.json`
- `latest` pointer (symlink or index file).

## Manifests
- Define manifest format and serve via Nginx.
- Cache headers and CORS policy.

## Integrity-first delivery
- Immutable publish semantics.
- CI check for artifact immutability.

## Tests
- Fixture-based canonicalization and anchor tests.
- Integration smoke tests for /packs endpoints.
