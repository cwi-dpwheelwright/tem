#!/usr/bin/env bash
set -Eeuo pipefail

DEPLOY_DIR="${CWI_MCP_CORE_DEPLOY_DIR:-/opt/cwi/DEPLOY/mcp-core}"
POSTGRES_CONTAINER="${CWI_ORCHESTRATOR_POSTGRES_CONTAINER:-internal-02-orchestrator-postgres}"

cd "$DEPLOY_DIR"

db_user="$(sed -n 's/^ORCHESTRATOR_DB_USER=//p' .env | tail -n 1)"
db_name="$(sed -n 's/^ORCHESTRATOR_DB_NAME=//p' .env | tail -n 1)"

if [[ -z "$db_user" || -z "$db_name" ]]; then
  echo "Missing ORCHESTRATOR_DB_USER or ORCHESTRATOR_DB_NAME in $DEPLOY_DIR/.env" >&2
  exit 2
fi

for file in \
  orchestrator-migrations/Migrations/0012_recursive_events.sql \
  orchestrator-migrations/Migrations/0013_validated_cache_records.sql \
  orchestrator-migrations/Migrations/0014_graph_update_candidates.sql \
  orchestrator-migrations/Migrations/0015_graph_vector_refresh_jobs.sql \
  orchestrator-migrations/Migrations/0016_sustainability_request_records.sql \
  orchestrator-migrations/Migrations/0017_sustainability_request_records_backfill.sql
do
  if [[ ! -f "$file" ]]; then
    echo "Missing migration file: $file" >&2
    exit 3
  fi

  echo "Applying $(basename "$file")"
  docker exec -i "$POSTGRES_CONTAINER" \
    psql -U "$db_user" -d "$db_name" -v ON_ERROR_STOP=1 < "$file"
done

for table in \
  recursive_events \
  recursive_event_quarantine \
  validated_cache_records \
  graph_update_candidates \
  graph_vector_refresh_jobs \
  sustainability_request_records
do
  if docker exec -i "$POSTGRES_CONTAINER" \
    psql -U "$db_user" -d "$db_name" -tAc "select to_regclass('$table') is not null" |
    grep -q t
  then
    echo "$table=present"
  else
    echo "$table=missing"
    exit 4
  fi
done
