# Iteration-08 Release Readiness Checklist

## Purpose
Define the v1 release gate checklist and evidence required for production readiness.

## Checklist
1. **Images**
   - Build images: `OPS/cwi-ops/ci/build-images.ps1`
   - Multi-arch build (amd64/arm64): `OPS/cwi-ops/ci/build-images-multiarch.ps1`
   - Record image tags + digests in release notes

2. **Environment validation**
   - Validate .env examples: `OPS/cwi-ops/scripts/validate-env.ps1`
   - Secret scan: `OPS/cwi-ops/scripts/scan-secrets.py`

3. **Test gates**
   - .NET tests: `OPS/cwi-ops/ci/run-dotnet-tests.ps1`
   - Node tests: `OPS/cwi-ops/ci/run-node-tests.ps1`
   - Python tests: `OPS/cwi-ops/ci/run-python-tests.ps1`
   - Gold tests (compose): `OPS/cwi-ops/ci/run-gold-tests-compose.ps1`
   - Gold tests (API): `OPS/cwi-ops/ci/run-gold-tests.ps1`
   - Perf smoke: `OPS/cwi-ops/ci/run-perf-smoke.ps1`

4. **Operational smoke**
   - Full stack smoke: `OPS/cwi-ops/scripts/full-stack-smoke.ps1`
   - Service smokes: `OPS/cwi-ops/scripts/smoke-router.ps1`, `OPS/cwi-ops/scripts/smoke-vector-shard.ps1`

5. **Security & compliance**
   - Authn enforcement checks (401/403): capture artifacts
   - Security scan: `OPS/cwi-ops/ci/run-security-scans.ps1`
   - Log redaction review (see `log-redaction-review.md`)

6. **Backups & rollback**
   - Backup/restore runbook: `OPS/cwi-ops/runbooks/backup-restore.md`
   - Rollback plan: rollback to previous image tag + validate health

7. **Supply chain**
   - SBOM generation: `OPS/cwi-ops/ci/generate-sbom.ps1`
   - Artifact signing: `OPS/cwi-ops/ci/sign-artifacts.ps1`

## Evidence bundle
Attach artifacts under `artifacts/qa/iteration-08-*.txt` and `artifacts/test-results/*`.
