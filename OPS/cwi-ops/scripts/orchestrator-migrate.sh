#!/usr/bin/env sh
set -eu

MIGRATIONS_DIR="${MIGRATIONS_DIR:-/migrations}"
PGHOST="${PGHOST:-orchestrator-postgres}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-${ORCHESTRATOR_DB_NAME:-cwi}}"
PGUSER="${PGUSER:-${ORCHESTRATOR_DB_USER:-cwi}}"

if [ -z "${PGPASSWORD:-}" ]; then
  PGPASSWORD="${ORCHESTRATOR_DB_PASSWORD:-}"
  export PGPASSWORD
fi

if [ ! -d "$MIGRATIONS_DIR" ]; then
  echo "Migrations directory not found: $MIGRATIONS_DIR" >&2
  exit 1
fi

echo "Waiting for orchestrator database at ${PGHOST}:${PGPORT}/${PGDATABASE}..."
attempt=0
until pg_isready -h "$PGHOST" -p "$PGPORT" -d "$PGDATABASE" -U "$PGUSER" >/dev/null 2>&1; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 30 ]; then
    echo "Database did not become ready after $attempt attempts." >&2
    exit 1
  fi
  sleep 2
done

found=0
for file in "$MIGRATIONS_DIR"/[0-9][0-9][0-9][0-9]_*.sql; do
  [ -e "$file" ] || continue
  found=1
  echo "Applying orchestrator migration: $(basename "$file")"
  psql -h "$PGHOST" -p "$PGPORT" -d "$PGDATABASE" -U "$PGUSER" -v ON_ERROR_STOP=1 -f "$file"
done

if [ "$found" -eq 0 ]; then
  echo "No orchestrator migration files found in $MIGRATIONS_DIR." >&2
  exit 1
fi

echo "Orchestrator migrations complete."
