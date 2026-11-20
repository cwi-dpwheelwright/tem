#!/usr/bin/env bash
set -euo pipefail

ENV_FILE="${ENV_FILE:-OPS/cwi-ops/DEPLOY/gpu_worker/.env}"
HOST="${HOST:-localhost}"
SKIP_METRICS="${SKIP_METRICS:-0}"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Env file not found: $ENV_FILE" >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

PORTS=(
  "${MODEL_HEALTH_PORT:-}"
  "${MODEL_HEALTH_PRO_PORT:-}"
  "${MODEL_HEALTH_FREE_PORT:-}"
)

found=0
for port in "${PORTS[@]}"; do
  if [[ -z "$port" ]]; then
    continue
  fi
  found=1
  curl -fsS "http://${HOST}:${port}/health" >/dev/null
  if [[ "$SKIP_METRICS" != "1" ]]; then
    curl -fsS "http://${HOST}:${port}/metrics" >/dev/null
  fi
done

if [[ "$found" != "1" ]]; then
  echo "MODEL_HEALTH_PORT values not found in $ENV_FILE" >&2
  exit 1
fi
