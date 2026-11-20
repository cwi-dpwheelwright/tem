from __future__ import annotations

import os
import re
import sqlite3
from dataclasses import dataclass
from typing import Iterable

from cwi_etl.operator_memory import ConsentMissingError, ConsentRecord


@dataclass(frozen=True)
class ConsentStoreConfig:
    path: str
    table: str = "operator_consent"
    ensure_schema: bool = True

    @staticmethod
    def from_env(prefix: str = "CWI_CONSENT_DB") -> "ConsentStoreConfig":
        path = os.environ.get(f"{prefix}_PATH", "").strip()
        if not path:
            raise ConsentMissingError(f"{prefix}_PATH is required to resolve consent")
        table = os.environ.get(f"{prefix}_TABLE", "operator_consent").strip() or "operator_consent"
        _validate_table_name(table)
        ensure_schema_raw = os.environ.get(f"{prefix}_ENSURE_SCHEMA", "true").strip().lower()
        ensure_schema = ensure_schema_raw in {"1", "true", "yes", "on"}
        return ConsentStoreConfig(path=path, table=table, ensure_schema=ensure_schema)


class ConsentStore:
    def __init__(self, config: ConsentStoreConfig) -> None:
        self._config = config
        self._table = _validate_table_name(self._config.table)
        if self._config.ensure_schema:
            self.ensure_schema()

    def ensure_schema(self) -> None:
        with self._connect() as conn:
            # nosemgrep: python.lang.security.audit.formatted-sql-query
            conn.execute(
                f"""
                CREATE TABLE IF NOT EXISTS {self._table} (
                    tenant_id TEXT NOT NULL,
                    operator_id TEXT NOT NULL,
                    granted INTEGER NOT NULL,
                    granted_utc TEXT NOT NULL,
                    source TEXT,
                    created_utc TEXT NOT NULL,
                    PRIMARY KEY (tenant_id, operator_id, granted_utc)
                )
                """
            )
            # nosemgrep: python.lang.security.audit.formatted-sql-query
            conn.execute(
                f"""
                CREATE INDEX IF NOT EXISTS idx_{self._table}_lookup
                ON {self._table} (tenant_id, operator_id, granted_utc DESC)
                """
            )

    def record_consent(
        self,
        *,
        tenant_id: str,
        operator_id: str,
        granted: bool,
        granted_utc: str,
        source: str | None,
        created_utc: str,
    ) -> None:
        with self._connect() as conn:
            # nosemgrep: python.lang.security.audit.formatted-sql-query
            conn.execute(
                f"""
                INSERT INTO {self._table}
                    (tenant_id, operator_id, granted, granted_utc, source, created_utc)
                VALUES (?, ?, ?, ?, ?, ?)
                """,
                (tenant_id, operator_id, 1 if granted else 0, granted_utc, source, created_utc),
            )

    def get_latest_consent(
        self,
        *,
        tenant_id: str,
        operator_id: str,
        observed_utc: str | None = None,
    ) -> ConsentRecord | None:
        with self._connect() as conn:
            if observed_utc:
                # nosemgrep: python.lang.security.audit.formatted-sql-query
                row = conn.execute(
                    f"""
                    SELECT granted, granted_utc, source
                    FROM {self._table}
                    WHERE tenant_id = ? AND operator_id = ? AND granted_utc <= ?
                    ORDER BY granted_utc DESC
                    LIMIT 1
                    """,
                    (tenant_id, operator_id, observed_utc),
                ).fetchone()
            else:
                # nosemgrep: python.lang.security.audit.formatted-sql-query
                row = conn.execute(
                    f"""
                    SELECT granted, granted_utc, source
                    FROM {self._table}
                    WHERE tenant_id = ? AND operator_id = ?
                    ORDER BY granted_utc DESC
                    LIMIT 1
                    """,
                    (tenant_id, operator_id),
                ).fetchone()
        if row is None:
            return None
        granted, granted_utc, source = row
        return ConsentRecord(granted=bool(granted), granted_utc=granted_utc, source=source)

    def get_consent_or_raise(
        self,
        *,
        tenant_id: str,
        operator_id: str,
        observed_utc: str | None = None,
    ) -> ConsentRecord:
        consent = self.get_latest_consent(
            tenant_id=tenant_id,
            operator_id=operator_id,
            observed_utc=observed_utc,
        )
        if consent is None:
            raise ConsentMissingError("no consent record found for operator")
        return consent

    def list_consents(
        self,
        *,
        tenant_id: str,
        operator_id: str,
    ) -> Iterable[ConsentRecord]:
        with self._connect() as conn:
            # nosemgrep: python.lang.security.audit.formatted-sql-query
            rows = conn.execute(
                f"""
                SELECT granted, granted_utc, source
                FROM {self._table}
                WHERE tenant_id = ? AND operator_id = ?
                ORDER BY granted_utc DESC
                """,
                (tenant_id, operator_id),
            ).fetchall()
        for granted, granted_utc, source in rows:
            yield ConsentRecord(granted=bool(granted), granted_utc=granted_utc, source=source)

    def _connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self._config.path)
        conn.row_factory = sqlite3.Row
        return conn


_TABLE_NAME_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")


def _validate_table_name(name: str) -> str:
    if not _TABLE_NAME_RE.match(name):
        raise ConsentMissingError("consent table name must be alphanumeric/underscore")
    return name
