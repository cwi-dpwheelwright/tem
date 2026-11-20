# DEPLOY: cwi-orchestrator

## Purpose
Compose bundle for the cwi-orchestrator role.

## Files
- docker-compose.yml
- .env.template (copy to .env)

## Setup
1. Copy .env.template to .env and update values.
2. Validate env:
   python OPS/cwi-ops/scripts/validate-env.py --role cwi-orchestrator --env-file OPS/cwi-ops/DEPLOY/cwi-orchestrator/.env
   .\OPS\cwi-ops\scripts\validate-env.ps1 -Role cwi-orchestrator -EnvFile OPS/cwi-ops/DEPLOY/cwi-orchestrator/.env
3. Apply DB migrations before starting the service. The shared deployment path
   uses `OPS/cwi-ops/scripts/orchestrator-migrate.sh` and the SQL files in
   `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.DataAccess/Migrations`.
   Current latest: `0017_sustainability_request_records_backfill.sql`.
4. Start service:
   docker compose up -d
