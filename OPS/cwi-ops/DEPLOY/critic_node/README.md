# DEPLOY: critic_node

Compose bundle for the Critic node role.

The Critic node owns Critic-specific scoring and response-quality rules. Orchestrator should call this role through `CWI_CRITIC_BASE_URL`; it should not embed or run Critic scoring logic locally.

Deploy flow:

```powershell
.\OPS\cwi-ops\DEPLOY\deploy.ps1 -InventoryPath .\OPS\cwi-ops\DEPLOY\deploy.inventory.critic-node.sample.json -DryRun
.\OPS\cwi-ops\DEPLOY\deploy.ps1 -InventoryPath .\OPS\cwi-ops\DEPLOY\deploy.inventory.critic-node.sample.json
```

Operational notes:

- Set `CWI_CRITIC_BASE_URL` in the orchestrator environment only after this service is deployed and reachable.
- Keep `CWI_CRITIC_ENFORCEMENT_MODE=observe` until Critic findings have been validated in production traces.
- Leave `CWI_CRITIC_BASE_URL` blank to prevent orchestrator from invoking Critic evaluation.

Smoke validation:

```powershell
$env:CWI_CRITIC_BASE_URL = "http://<CRITIC_NODE_TAILSCALE_IP>:8086"
.\OPS\cwi-ops\scripts\smoke-critic-node.ps1
```

The smoke runner validates `/health`, posts a known observe-only response snapshot to `/runtime/critic/observe`, and writes artifacts under `testing\runs\critic-node\`.
