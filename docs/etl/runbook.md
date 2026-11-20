# CWI ETL Runbook

## Local run
1. Ensure Python 3.10+ is available.
2. Install dependencies with `pip install src/etl`.
3. Run chunking or provenance utilities as needed.

## Docker
```bash
docker build -f src/etl/docker/Dockerfile -t cwi-etl .
docker run --rm cwi-etl
```

## Troubleshooting
- Ensure `PYTHONPATH` includes `src/etl` when running from repo root.
- If provenance calls fail, verify gateway base URL and auth token.

## Rollback
- Revert to previous container image tag and redeploy.
