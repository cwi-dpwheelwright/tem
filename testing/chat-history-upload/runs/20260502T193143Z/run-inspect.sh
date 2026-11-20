docker cp /tmp/inspect-memory-20260502T193143Z.sql internal-02-orchestrator-postgres:/tmp/inspect-memory.sql
docker exec internal-02-orchestrator-postgres sh -lc 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atf /tmp/inspect-memory.sql'
