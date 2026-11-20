# DEPLOY: graph_db

Standalone Apache AGE deployment bundle for the retained teacher-host graph database.

## Purpose

Deploy the separate `cwi-age-db` instance that remains on the teacher host for graph build, staging, or validation workflows.

## Files

- `docker-compose.yml`
- `.env.template` (copy to `.env` if present in your environment workflow)

## Validation

Use the existing graph DB role validation:

```bash
python OPS/cwi-ops/scripts/validate-env.py --role graph_db --env-file OPS/cwi-ops/DEPLOY/graph_db/.env
```

```powershell
.\OPS\cwi-ops\scripts\validate-env.ps1 -Role graph_db -EnvFile OPS/cwi-ops/DEPLOY/graph_db/.env
```

## Start

```bash
docker compose up -d
```

## Expected runtime

- service: `cwi-age-db`
- default container name: `cwi-age-db`
- port mapping: `${CWI_GRAPH_DB_HOST_PORT}:5432`
- retained live parity mount:
  - `${CWI_GRAPH_WORK_AGE_HOST_DIR:-/srv/cwi/graph-work/age}:/srv/cwi/graph-work/age:ro`

## Notes

- This bundle is separate from the MCP-host `cwi-graph-db` in `OPS/cwi-ops/DEPLOY/mcp-core`.
- Keep the image tag pinned in the env file or deployment pipeline.
- If you are preserving the current teacher-host graph workflow shape, set:
  - `CWI_GRAPH_DB_DATA_DIR=/srv/cwi/graph-db/postgres`
  - `CWI_GRAPH_WORK_AGE_HOST_DIR=/srv/cwi/graph-work/age`
- Follow `OPS/cwi-ops/runbooks/graph-db.md` for AGE extension enablement, backup, restore, and upgrade steps.
