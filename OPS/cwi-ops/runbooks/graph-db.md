# Graph DB Runbook (Postgres + Apache AGE)

## Scope
Covers local/staging deployment, extension enablement, backup, restore, and upgrade steps for the graph store.

## Provisioning
1. Deploy the graph DB container with the Apache AGE image (see compose configs).
2. Ensure `CWI_GRAPH_DB_USER`, `CWI_GRAPH_DB_PASSWORD`, and `CWI_GRAPH_DB_DATABASE` match `CWI_GRAPH_DB_DSN`.
3. Verify health with `pg_isready` against the configured database.

## Enable AGE extension
1. Connect to the graph DB using `psql` or `psycopg`.
2. Run:
   - `CREATE EXTENSION IF NOT EXISTS age;`
   - `LOAD 'age';`
   - `SET search_path = ag_catalog, "$user", public;`
3. Confirm the graph exists or allow the ETL to create it via `CWI_GRAPH_DB_CREATE_GRAPH=true`.

## Backup
1. Execute `pg_dump` for the graph DB database:
   - `pg_dump -Fc -f graph-db.backup <database>`
2. Store backups in immutable storage.
3. Record checksum and timestamp in the backup ledger.

## Restore
1. Provision a clean Postgres+AGE instance.
2. Restore with `pg_restore -d <database> graph-db.backup`.
3. Re-run the AGE extension enablement steps if required.
4. Validate graph metadata:
   - `SELECT name, namespace FROM ag_catalog.ag_graph;`

## Upgrade
1. Pin the AGE image tag in deployment manifests.
2. Take a full backup before upgrade.
3. Apply upgrades in staging and validate graph ingestion tests.

## Evidence
- Backup artifacts + checksums
- Restore logs and validation output
- Graph ingestion integration test results
