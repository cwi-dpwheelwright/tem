# CI pipeline (portable scripts)

This folder contains CI helper scripts. Integrate these into your CI system (GitHub Actions/Azure DevOps/etc.).

## Scripts
- `build-images.ps1`: build router/orchestrator images
- `build-images-multiarch.ps1`: build/push multi-arch router/orchestrator images
- `run-dotnet-tests.ps1`: run .NET unit/arch tests
- `run-node-tests.ps1`: run Node/TypeScript lint/test
- `run-python-tests.ps1`: run Python lint/test
- `run-node-agent-activation-sync.ps1`: run activation status sync check (mocked)
- `run-node-agent-kill-switch-sync.ps1`: run kill-switch sync integration test against compose stack
- `run-node-agent-enrollment-smoke.ps1`: run node-agent enrollment smoke against local mock
- `run-perf-smoke.ps1`: run perf smoke runner (ask/action)
- `run-stack-stress.ps1`: ramped stress runner (gateway -> model)
- `run-model-worker-smoke.ps1`: compose-based smoke for model worker health + generate
- `run-model-worker-capacity.ps1`: run model worker capacity probe
- `run-gold-tests.ps1`: run gold-tests smoke suite
- `run-gold-tests-compose.ps1`: start compose test stack and run gold-tests smoke suite
- un-e2e-simulation.ps1: end-to-end simulation runner (clean start + compose + gold tests + failure scenarios)
- `check-env-examples.ps1`: scan `.env.template` files for secrets
- `check-codegen-drift.ps1`: regenerate contracts and fail on drift
- `check-licenses.ps1`: enforce license allow/block policy
- `check-reproducibility.ps1`: verify lockfiles and pinned base images
- `check-orr-runbooks.ps1`: enforce ORR runbooks exist
- `check-compose-profiles.ps1`: sanity check compose test profiles
- `check-caddy-config.ps1`: validate Caddyfile syntax
- `check-nginx-config.ps1`: validate nginx.conf syntax and /packs/ route
- `generate-sbom.ps1`: generate SBOMs for built service images
- `sign-artifacts.ps1`: sign SBOM artifacts and emit signatures
- `run-bundle-encrypt.ps1`: encrypt bundles from SBOM inputs

## Test reporting + coverage
- Run the test scripts above and publish JUnit/coverage artifacts from CI jobs.
