#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
compose_file="${COMPOSE_FILE:-${script_dir}/docker-compose.full.yml}"
env_file="${ENV_FILE:-${script_dir}/.env}"

echo "docker compose -f \"$compose_file\" --env-file \"$env_file\" down"
docker compose -f "$compose_file" --env-file "$env_file" down
