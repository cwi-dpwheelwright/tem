# Release Pipeline & Supply Chain (Iteration 02)

## CI/CD gates
- SBOM generation.
- Artifact signing.
- Dependency and image scans.

## Evidence
- Store CI logs and scan outputs in `artifacts/`.

## Implementation notes
- SBOMs are generated for service images via `OPS/cwi-ops/ci/generate-sbom.ps1`.
- SBOM signatures are produced via `OPS/cwi-ops/ci/sign-artifacts.ps1` using `COSIGN_PRIVATE_KEY` (and optional `COSIGN_PASSWORD`).
