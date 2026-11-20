# DEPLOY Bundles

This folder contains minimal, role-specific deployment bundles intended to be copied to host install paths (for example, /opt/cwi/DEPLOY/<role>). Each bundle includes:
- docker-compose.yml
- .env.template (copy to .env and edit)
- README.md with validation steps

Use the env matrix to validate required variables:

python OPS/cwi-ops/scripts/validate-env.py --role <role> --env-file OPS/cwi-ops/DEPLOY/<role>/.env
.\OPS\cwi-ops\scripts\validate-env.ps1 -Role <role> -EnvFile OPS/cwi-ops/DEPLOY/<role>/.env


## Deployment helper script
Use `deploy.ps1` with an inventory file to push bundles to remote hosts and run `docker compose` remotely.

1. Copy the sample inventory and fill in hostnames/roles:
   `Copy-Item OPS/cwi-ops/DEPLOY/deploy.inventory.sample.json OPS/cwi-ops/DEPLOY/deploy.inventory.local.json`
2. Run a dry run:
   `powershell OPS/cwi-ops/DEPLOY/deploy.ps1 -InventoryPath OPS/cwi-ops/DEPLOY/deploy.inventory.local.json -DryRun`
3. Deploy:
   `powershell OPS/cwi-ops/DEPLOY/deploy.ps1 -InventoryPath OPS/cwi-ops/DEPLOY/deploy.inventory.local.json`

Notes:
- Roles map to bundle folder names under `OPS/cwi-ops/DEPLOY` by default.
- Use a role object with `bundle` to point at `OPS/cwi-ops/compose/*` (e.g., gateway).
- The script seeds `.env` from `.env.template` if no env file is provided and `.env` is missing.
- `deploymentMode` controls cleanup scope:
  - `strict`: current host-wide behavior; removes all non-excluded containers, images, and named volumes on the host before deploying.
  - `safe`: removes only the containers, images, and named volumes associated with each role being deployed, then brings that role back up.
- `teacher_node` is the canonical teacher-host deployment role.
- `critic_node` is the canonical Critic-host deployment role. Critic-specific scoring should run there, not inside orchestrator.
- `teacher_minio` remains available only as an optional adjunct if a MinIO-backed teacher artifact store is explicitly desired.
- Remote Docker builds are source-hygienic by default:
  - temporary remote build contexts are removed on success or failure;
  - Docker builder cache is pruned after remote builds so source-bearing intermediate layers are not retained on the server;
  - `-KeepRemoteBuildContext` and `-KeepRemoteBuildCache` exist only for short-lived debugging and should not be used on shared or production hosts.
- Optional host cleanup is supported through inventory settings:
  - `clearContainersBeforeDeploy`: when `true`, the deploy script removes all non-excluded containers on that host before deploying roles.
  - `clearImagesBeforeDeploy`: when `true`, the deploy script removes all non-excluded Docker images on that host before deploying roles.
  - `clearVolumesBeforeDeploy`: when `true`, the deploy script removes all non-excluded named Docker volumes on that host before deploying roles.
  - `clearCleanupExclusions`: exclusion patterns that are applied across cleanup. Exact names and shell-style patterns are supported. This is the preferred key.
  - `clearContainerExclusions`: legacy alias for `clearCleanupExclusions`, still supported for backward compatibility.
  - Exclusions are matched directly against the resource name being cleaned:
    - containers by container name
    - images by repository:tag
    - volumes by volume name
  - Exclusions apply to `strict` deployment mode. `safe` deployment mode scopes cleanup to the role being deployed instead of the whole host.
  - Use patterns that match the real resource names you want to keep. Example baseline: `*cadvisor*`, `*node-exporter*`, `nextcloud*`, `postgres:16`, `redis:7`.
  - The default baseline preserves the currently retained Nextcloud stack, including its sidecar images and `nextcloud*` named volumes and containers.
- Cleanup is evaluated per host before role deployment starts, so use it carefully on shared hosts. This is destructive and can remove non-excluded database/cache/application data volumes.

Run the secret scan before sharing bundles externally:

python OPS/cwi-ops/scripts/scan-secrets.py --path OPS/cwi-ops/DEPLOY
.\OPS\cwi-ops\scripts\scan-secrets.ps1 -Path OPS/cwi-ops/DEPLOY

## Gateway + router helper
Use `deploy-gateway-router.ps1` to deploy gateway and router bundles directly from `OPS/cwi-ops/compose`.

Examples:
- Deploy gateway only to public host:
  `powershell OPS/cwi-ops/DEPLOY/deploy-gateway-router.ps1 -GatewayHost 100.124.140.66 -SkipRouter`
- Deploy both (different hosts):
  `powershell OPS/cwi-ops/DEPLOY/deploy-gateway-router.ps1 -GatewayHost 100.124.140.66 -RouterHost 100.87.205.59`
- Deploy both with explicit env files:
  `powershell OPS/cwi-ops/DEPLOY/deploy-gateway-router.ps1 -GatewayHost 100.124.140.66 -GatewayEnvFile C:\path\gateway.env -RouterHost 100.87.205.59 -RouterEnvFile C:\path\router.env`
