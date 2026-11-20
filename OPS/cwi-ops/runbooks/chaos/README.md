# Chaos Drills Runbook

## Scope
Staged chaos drills for service disruption, network impairment, key rotation/revocation,
and storage pressure. Use the `OPS/cwi-ops/scripts/chaos` scripts with the test compose
stack unless otherwise noted.

## Prereqs
- Docker + Docker Compose
- Stack running with `OPS/cwi-ops/compose/test/docker-compose.test.yml`
- Gold tests available for verification

## Drill 1: Kill service and verify recovery
1. Run `OPS/cwi-ops/scripts/chaos/chaos-stop-service.ps1 -Service cwi-router -DurationSeconds 60`
2. Verify API ready endpoint is degraded during outage.
3. Verify recovery once the service restarts:
   - `python tools/cwi-gold-tests/run_tests.py --suite simulation --base-url http://localhost:18081`

## Drill 2: Packet loss/latency injection
1. Run `OPS/cwi-ops/scripts/chaos/chaos-netem.ps1 -Service cwi-router -LossPercent 10 -LatencyMs 250 -DurationSeconds 60`
2. Verify degraded behavior or retry behavior via simulation tests.

## Drill 3: Key rotation / revocation enforcement
1. Replace trust bundle (and CRL if applicable):
   - `OPS/cwi-ops/scripts/chaos/chaos-mtls-rotate.ps1 -Service cwi-gateway -LocalBundlePath <bundle.pem> -LocalCrlPath <crl.pem> -Restart`
2. Verify mTLS enforcement via simulation tests:
   - `CWI_EXPECT_MTLS_ENFORCED=true CWI_MTLS_BASE_URL=https://localhost:18443 python tools/cwi-gold-tests/run_tests.py --suite simulation --base-url http://localhost:18081`

## Drill 4: Disk pressure
1. Run `OPS/cwi-ops/scripts/chaos/chaos-disk-pressure.ps1 -Service cwi-orchestrator -TargetPath /tmp -SizeMb 256 -DurationSeconds 60`
2. Verify services remain responsive or fail closed (observe logs).

## Evidence
- Capture test output, logs, and timings under `artifacts/chaos/<timestamp>/`.
- Note drill duration, affected services, and observed behavior.
