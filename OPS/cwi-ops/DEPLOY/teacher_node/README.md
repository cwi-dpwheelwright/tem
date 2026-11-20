# DEPLOY: teacher_node

## Purpose
Compose bundle for the teacher node role.

## Files
- docker-compose.yml
- .env.template (copy to .env)

## Setup
1. Copy .env.template to .env and update values.
2. Validate env:
   python OPS/cwi-ops/scripts/validate-env.py --role teacher_node --env-file OPS/cwi-ops/DEPLOY/teacher_node/.env
   .\OPS\cwi-ops\scripts\validate-env.ps1 -Role teacher_node -EnvFile OPS/cwi-ops/DEPLOY/teacher_node/.env
3. Start service:
   docker compose up -d

## Remote deploy script
- Script: `OPS/cwi-ops/DEPLOY/teacher_node/deploy-teacher-node.ps1`
- Validation: `OPS/cwi-ops/DEPLOY/teacher_node/validate-teacher-node-architecture.ps1` (called by deploy script)
- Build inputs: the deploy path now stages both `tools/teacher-node` and `src/etl` so the teacher image includes the repo-native graph tooling package.
- Canonical internal teacher profile:
  - filesystem-backed:
    - `TEACHER_NODE_ARTIFACT_BACKEND=filesystem`
    - artifact root `/data/teacher-node`
    - host bind `/srv/cwi/teacher-node-artifacts`
  - shared internal defaults:
    - container `internal-02-teacher-node`
    - remote path `~/cwi-teacher-node-internal`
    - pack root mount `/srv/cwi/packs` -> `/app/packs`
    - graph staging `/data/teacher-node/graph-staging`
    - vector export root `/data/teacher-node/vector`
    - Teacher Critic report `/data/teacher-node/critic/latest.json`
    - last successful validation marker `/data/teacher-node/last-successful-validation.json`
- MinIO-backed mode is still supported by the validator and bundle set, but it is optional and not part of the current target deployment model.
- Example:
  - `powershell -ExecutionPolicy Bypass -File OPS/cwi-ops/DEPLOY/teacher_node/deploy-teacher-node.ps1 -ServerHost <ip> -ServerUser root -BuildOnRemote`
  - Add `-RunCriticValidation` when packs/vector exports are seeded and the deployment should fail if Teacher Critic finds a blocking issue.

## Artifact storage
- Default generated env from the deploy script uses filesystem-backed artifacts for the internal teacher host deployment.
- The current target deployment keeps teacher artifacts filesystem-backed on the teacher host.
- MinIO-backed teacher-node remains available only as an optional future variant.
- The separate teacher-host AGE database should be deployed via `OPS/cwi-ops/DEPLOY/graph_db`.
