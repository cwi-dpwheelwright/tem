#!/bin/sh
set -e

if [ -z "$IDENTITY_DB_CONNECTION" ]; then
  echo "IDENTITY_DB_CONNECTION is required."
  exit 1
fi

get_conn_value() {
  key="$1"
  echo "$IDENTITY_DB_CONNECTION" | tr ';' '\n' | awk -F= -v k="$key" '
    {
      key=tolower($1);
      target=tolower(k);
      if (key == target) {
        print $2;
      }
    }' | tail -n 1
}

export PGHOST="$(get_conn_value Host)"
export PGPORT="$(get_conn_value Port)"
export PGDATABASE="$(get_conn_value Database)"
export PGUSER="$(get_conn_value Username)"
if [ -z "$PGUSER" ]; then
  export PGUSER="$(get_conn_value UserID)"
fi
export PGPASSWORD="$(get_conn_value Password)"

if [ -z "$PGDATABASE" ]; then
  PGDATABASE="cwi_identity"
  export PGDATABASE
fi

attempts=0
until psql -c "select 1" >/dev/null 2>&1; do
  attempts=$((attempts + 1))
  if [ "$attempts" -ge 30 ]; then
    echo "Database not ready after ${attempts} attempts."
    exit 1
  fi
  sleep 2
done

for file in /migrations/000*.sql; do
  if [ ! -f "$file" ]; then
    continue
  fi

  migration_id=$(sed -n "s/.*VALUES ('\\([^']*\\)'.*/\\1/p" "$file" | head -n 1)
  if [ -z "$migration_id" ]; then
    echo "Skipping $file (no migration id)."
    continue
  fi

  applied=$(psql -tAc "select 1 from \"__EFMigrationsHistory\" where \"MigrationId\"='${migration_id}'" 2>/dev/null | tr -d '[:space:]' || true)
  if [ "$applied" = "1" ]; then
    echo "Migration ${migration_id} already applied."
    continue
  fi

  echo "Applying ${migration_id} from ${file}..."
  psql -v ON_ERROR_STOP=1 -f "$file"
done

echo "Identity migrations complete."
