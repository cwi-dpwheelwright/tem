# Runbook: Pro Model Worker (GPU)

## Prerequisites
- NVIDIA GPU with drivers installed
- nvidia-container-toolkit installed
- Docker/Compose available

## Configuration
Update `OPS/cwi-ops/compose/gpu_worker/.env`:
- `OLLAMA_PRO_IMAGE`
- `PRO_WORKER_HTTP_PORT`
- `PRO_WORKER_DATA_DIR`
- `OLLAMA_FREE_IMAGE`
- `FREE_WORKER_HTTP_PORT`
- `FREE_WORKER_DATA_DIR`
- `MODEL_CAPABILITIES_PRO_PORT`
- `MODEL_CAPABILITIES_FREE_PORT`

## Startup
```
cd OPS/cwi-ops/compose/gpu_worker
# ensure .env is configured
# docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d
# for CPU-only validation: docker compose up -d
```

## Validation
- Healthcheck returns 200 on `/api/tags`
- Health proxy returns 200 on `/health` and `/metrics`
- Verify GPU usage in container logs

## Model Pull (Example)
```
# docker exec -it internal-02-model-worker-pro ollama pull <model-name>
# docker exec -it internal-02-model-worker-pro ollama list
```

## Benchmark (quick)
```
.\OPS\cwi-ops\scripts\benchmark-model-worker.ps1 -BaseUrl http://localhost:11435 -Model llama3:70b
```

## Capacity smoke
```
$env:CWI_MODEL_WORKER_URL="http://localhost:11435"
$env:CWI_MODEL_WORKER_MODEL="llama3:70b"
.\OPS\cwi-ops\ci\run-model-worker-capacity.ps1 -DurationSeconds 30 -Concurrency 4
```

## Sealed network mode
To run without outbound egress (sealed environment), use:
```
cd OPS/cwi-ops/DEPLOY/gpu_worker
docker compose -f docker-compose.yml -f docker-compose.sealed.yml up -d
```
Notes:
- Pre-pull models before enabling sealed mode.
- Health/metrics endpoints stay reachable on the internal network.

## Rollback
- Stop container
- Revert image tag or configuration

## Evidence
- Capture `/api/tags` response and GPU logs
