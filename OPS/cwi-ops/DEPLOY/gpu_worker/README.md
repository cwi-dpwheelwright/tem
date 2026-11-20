# DEPLOY: gpu_worker

## Purpose
Compose bundle for the GPU worker role (Ollama baseline + Pro).

## Files
- docker-compose.yml
- docker-compose.gpu.yml (GPU override for local Compose)
- .env.template (copy to .env)

## Setup
1. Copy .env.template to .env and update values.
2. Validate env (allow extra vars for the Pro worker settings):
   python OPS/cwi-ops/scripts/validate-env.py --role gpu_worker --env-file OPS/cwi-ops/DEPLOY/gpu_worker/.env --allow-extra
   .\OPS\cwi-ops\scripts\validate-env.ps1 -Role gpu_worker -EnvFile OPS/cwi-ops/DEPLOY/gpu_worker/.env -AllowExtra
3. Start service (GPU local Compose):
   docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d
   For CPU-only validation, run: docker compose up -d

## Model pull/provision (example values)
```
# baseline worker
docker exec -it internal-02-model-worker ollama pull llama3:8b
docker exec -it internal-02-model-worker ollama list

# pro worker (GPU)
docker exec -it internal-02-model-worker-pro ollama pull llama3:70b
docker exec -it internal-02-model-worker-pro ollama list
```

## Free/overflow worker (CPU)
The `cwi-gpu-worker-free` service provides a CPU-only overflow tier. Configure:
- `OLLAMA_FREE_IMAGE`
- `FREE_WORKER_HTTP_PORT`
- `FREE_WORKER_DATA_DIR`

## GPU enablement notes
- Requires NVIDIA drivers and `nvidia-container-toolkit`.
- Use `docker-compose.gpu.yml` for local Compose; it sets `gpus: all` on the Pro worker and fails fast when GPU is unavailable.
- The base compose keeps a `deploy.resources.reservations.devices` hint for environments that honor it.

## Timeouts + concurrency tuning
Use the following env knobs (optional):
- `OLLAMA_NUM_PARALLEL`: concurrent requests per worker
- `OLLAMA_MAX_LOADED_MODELS`: max cached models
- `OLLAMA_KEEP_ALIVE`: model keep-alive window

## Capabilities endpoint
Static capabilities endpoints are exposed via:
- `http://cwi-model-capabilities-pro:${MODEL_CAPABILITIES_PRO_PORT}/capabilities`
- `http://cwi-model-capabilities-free:${MODEL_CAPABILITIES_FREE_PORT}/capabilities`

Set router env vars:
- `MODEL_PRO_URL` -> `http://cwi-gpu-worker-pro:${PRO_WORKER_HTTP_PORT}`
- `MODEL_FREE_URL` -> `http://cwi-gpu-worker-free:${FREE_WORKER_HTTP_PORT}`
- `MODEL_HEALTH_PATH` -> `/api/tags`

## Health + metrics endpoints
The health/metrics proxy exposes standardized endpoints:
- `http://cwi-model-worker-health:${MODEL_HEALTH_PORT}/health`
- `http://cwi-model-worker-health:${MODEL_HEALTH_PORT}/metrics`
- `http://cwi-model-worker-health-pro:${MODEL_HEALTH_PRO_PORT}/health`
- `http://cwi-model-worker-health-pro:${MODEL_HEALTH_PRO_PORT}/metrics`
- `http://cwi-model-worker-health-free:${MODEL_HEALTH_FREE_PORT}/health`
- `http://cwi-model-worker-health-free:${MODEL_HEALTH_FREE_PORT}/metrics`

## Lifecycle wrappers
Node-agent compatible wrappers live in:
- `OPS/cwi-ops/DEPLOY/gpu_worker/lifecycle`

Start/stop/health examples:
```
.\OPS\cwi-ops\DEPLOY\gpu_worker\lifecycle\start.ps1
.\OPS\cwi-ops\DEPLOY\gpu_worker\lifecycle\health.ps1
.\OPS\cwi-ops\DEPLOY\gpu_worker\lifecycle\stop.ps1
```

## Smoke checks
```
.\OPS\cwi-ops\scripts\smoke-model-worker.ps1 -BaseUrl http://localhost:11434 -HealthUrl http://localhost:18092 -Model llama3:8b
```

## Sealed network mode
To run without outbound egress (sealed environment), use the sealed override:
```
docker compose -f docker-compose.yml -f docker-compose.sealed.yml up -d
```
Notes:
- Pre-pull models before enabling sealed mode.
- The internal network remains available for local orchestration and health checks.
