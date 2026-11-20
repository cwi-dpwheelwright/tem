# ADR-ETL-002: ETL Test Strategy

## Status
Accepted

## Context
ETL ingestion must be deterministic and evidence-driven. Tests should be stack-agnostic where possible, with CI-only integration gates tied to compose environments and gold-test artifacts.

## Decision
Adopt a layered test strategy:
- Unit tests for chunking, provenance payloads, and ingest runner.
- Integration test (compose) for ingest → search → trace linkage, gated by env secrets.
- Smoke scripts for end-to-end runs with deterministic fixtures.

## Consequences
- Requires CI secrets for integration environments.
- Enables repeatable, artifact-backed evidence for iteration close-out.
