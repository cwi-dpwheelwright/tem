#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="${COMPOSE_FILE:-OPS/cwi-ops/DEPLOY/gpu_worker/docker-compose.yml}"
ENV_FILE="${ENV_FILE:-OPS/cwi-ops/DEPLOY/gpu_worker/.env}"
ATTACH="${ATTACH:-0}"

if [[ ! -f "$COMPOSE_FILE" ]]; then
  echo "Compose file not found: $COMPOSE_FILE" >&2
  exit 1
fi
if [[ ! -f "$ENV_FILE" ]]; then
  echo "Env file not found: $ENV_FILE" >&2
  exit 1
fi

SERVICES=(
  cwi-gpu-worker
  cwi-gpu-worker-pro
  cwi-gpu-worker-free
  cwi-model-worker-health
  cwi-model-worker-health-pro
  cwi-model-worker-health-free
)

if [[ $# -gt 0 ]]; then
  SERVICES=("$@")
fi

if [[ "$ATTACH" == "1" ]]; then
  docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up "${SERVICES[@]}"
else
  docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up -d "${SERVICES[@]}"
fi
