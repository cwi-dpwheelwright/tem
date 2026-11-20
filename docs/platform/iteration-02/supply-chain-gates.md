# Supply-chain Security Gates (Iteration 02)

## SBOM generation
- Generate SBOM for each service image.
- Store SBOM artifacts under `artifacts/sbom/`.

## Artifact signing
- Sign container images and release artifacts.
- Store signature metadata in `artifacts/signatures/`.

## Dependency and image scanning
- Run dependency scan with blocking thresholds.
- Run container image scan with blocking thresholds.

## Evidence
- CI logs and scan reports archived with timestamps.
- SBOMs stored under `artifacts/sbom/`.
- Signature artifacts stored under `artifacts/signatures/`.
