import pytest

from cwi_connectors.state_store import PostgresStateStore


@pytest.mark.integration
def test_postgres_state_store_roundtrip():
    testcontainers = pytest.importorskip("testcontainers.postgres")
    PostgresContainer = testcontainers.PostgresContainer

    with PostgresContainer("postgres:16-alpine") as postgres:
        dsn = postgres.get_connection_url()
        if "+psycopg2" in dsn:
            dsn = dsn.replace("postgresql+psycopg2", "postgresql", 1)
        store = PostgresStateStore(dsn)
        saved = store.save("connector-1", "cursor-1")
        loaded = store.load("connector-1")
        assert loaded is not None
        assert loaded.cursor == saved.cursor
        assert loaded.updated_utc == saved.updated_utc
