# Supply Chain Integrity (Iteration-08)

## SBOM generation
- Run `OPS/cwi-ops/ci/generate-sbom.ps1` and `generate-sbom-sources.ps1`.
- Store SBOMs with release artifacts.

## Artifact signing
- Use `OPS/cwi-ops/ci/sign-artifacts.ps1` for bundle signing guidance.

## Reproducibility
- Run `OPS/cwi-ops/ci/check-reproducibility.ps1`.
- Ensure lockfiles and base image tags are pinned for release builds.

## Multi-arch images
- Build multi-arch manifests with `OPS/cwi-ops/ci/build-images-multiarch.ps1`.

## Image provenance
- Record image digests and keep a release manifest (image -> digest).
- Use immutable tags for production rollouts.
