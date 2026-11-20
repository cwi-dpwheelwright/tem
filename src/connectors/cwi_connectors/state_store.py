from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Protocol

try:
    import psycopg
except Exception:  # pragma: no cover - optional dependency
    psycopg = None


@dataclass(frozen=True)
class ConnectorState:
    cursor: str | None
    updated_utc: str


class StateStore(Protocol):
    def load(self, key: str) -> ConnectorState | None:
        ...

    def save(self, key: str, cursor: str | None) -> ConnectorState:
        ...


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


class FileStateStore:
    def __init__(self, path: str) -> None:
        self.path = path

    def load(self, key: str) -> ConnectorState | None:
        try:
            with open(self.path, "r", encoding="utf-8") as handle:
                raw = json.load(handle)
        except FileNotFoundError:
            return None
        value = raw.get(key)
        if not value:
            return None
        return ConnectorState(cursor=value.get("cursor"), updated_utc=value.get("updated_utc", ""))

    def save(self, key: str, cursor: str | None) -> ConnectorState:
        try:
            with open(self.path, "r", encoding="utf-8") as handle:
                raw = json.load(handle)
        except FileNotFoundError:
            raw = {}
        state = ConnectorState(cursor=cursor, updated_utc=utc_now())
        raw[key] = {"cursor": state.cursor, "updated_utc": state.updated_utc}
        with open(self.path, "w", encoding="utf-8") as handle:
            json.dump(raw, handle, indent=2)
        return state


class PostgresStateStore:
    def __init__(self, dsn: str) -> None:
        if psycopg is None:
            raise RuntimeError("psycopg is required for Postgres state store")
        self.dsn = dsn
        self._init_schema()

    def load(self, key: str) -> ConnectorState | None:
        with psycopg.connect(self.dsn) as conn:
            with conn.cursor() as cur:
                cur.execute(
                    "SELECT cursor, updated_utc FROM connector_state WHERE connector_id = %s",
                    (key,),
                )
                row = cur.fetchone()
        if not row:
            return None
        return ConnectorState(cursor=row[0], updated_utc=row[1])

    def save(self, key: str, cursor: str | None) -> ConnectorState:
        state = ConnectorState(cursor=cursor, updated_utc=utc_now())
        with psycopg.connect(self.dsn) as conn:
            with conn.cursor() as cur:
                cur.execute(
                    """
                    INSERT INTO connector_state (connector_id, cursor, updated_utc)
                    VALUES (%s, %s, %s)
                    ON CONFLICT (connector_id)
                    DO UPDATE SET cursor = EXCLUDED.cursor, updated_utc = EXCLUDED.updated_utc
                    """,
                    (key, state.cursor, state.updated_utc),
                )
            conn.commit()
        return state

    def _init_schema(self) -> None:
        with psycopg.connect(self.dsn) as conn:
            with conn.cursor() as cur:
                cur.execute(
                    """
                    CREATE TABLE IF NOT EXISTS connector_state (
                        connector_id TEXT PRIMARY KEY,
                        cursor TEXT NULL,
                        updated_utc TEXT NOT NULL
                    )
                    """
                )
            conn.commit()
