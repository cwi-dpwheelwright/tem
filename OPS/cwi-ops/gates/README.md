# Gates

This folder contains gate runners that orchestrate deployment and verification steps with a unified report.

## Deploy + Verify Gate

Runs full deployment (inventory-based), verification checks, and an end-to-end gateway flow.

```powershell
powershell -ExecutionPolicy Bypass -File OPS/cwi-ops/gates/deploy-verify.ps1 `
  -InventoryPath OPS/cwi-ops/DEPLOY/deploy.inventory.local.json `
  -GatewayEnvFile OPS/cwi-ops/DEPLOY/public-gateway.env `
  -CoreEnvFile OPS/cwi-ops/DEPLOY/mcp-core/.env `
  -TeacherEnvFile OPS/cwi-ops/DEPLOY/teacher_node/.env
```

Artifacts are written under `artifacts/gates/<gate-name>-<timestamp>/`.
