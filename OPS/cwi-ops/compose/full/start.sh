#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
compose_file="${COMPOSE_FILE:-${script_dir}/docker-compose.full.yml}"
env_file="${ENV_FILE:-${script_dir}/.env}"
env_template="${script_dir}/.env.template"

if [ ! -f "$env_file" ]; then
  cp "$env_template" "$env_file"
  echo "Created $env_file from .env.template"
fi

build_arg="--build"
if [ "${NO_BUILD:-}" = "1" ]; then
  build_arg=""
fi

echo "docker compose -f \"$compose_file\" --env-file \"$env_file\" up -d $build_arg"
docker compose -f "$compose_file" --env-file "$env_file" up -d $build_arg
