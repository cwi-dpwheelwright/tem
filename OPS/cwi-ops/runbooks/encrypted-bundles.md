# Encrypted Bundle Build & Key Handling

## Build Pipeline
- Generate SBOM: `OPS/cwi-ops/ci/generate-sbom.ps1`
- Encrypt bundle: `OPS/cwi-ops/ci/run-bundle-encrypt.ps1`
- Sign artifacts: `OPS/cwi-ops/ci/sign-artifacts.ps1`

## Key Handling
- Encryption key provided via `BUNDLE_ENCRYPTION_KEY`.
- Store keys in approved secret manager.
- Rotate keys quarterly or after incidents.

## Evidence
- Encrypted bundle and manifest under `artifacts/bundles/`.
- Signature bundles under `artifacts/signatures/`.
