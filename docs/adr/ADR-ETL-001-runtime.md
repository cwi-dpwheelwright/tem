# ADR-ETL-001: ETL Runtime + Packaging

## Status
Accepted

## Context
Iteration-03 requires an ETL ingestion pipeline with deterministic chunking, provenance emission, and CI evidence. The team needs a small, portable runtime with minimal dependencies and easy containerization.

## Decision
Use Python 3.11 as the ETL runtime with a slim Docker image, `pyproject.toml` packaging, and a simple CLI entrypoint. This keeps the stack consistent with existing gold tests and enables rapid iteration.

## Consequences
- Pros: fast iteration, easy container build, consistent tooling with gold tests.
- Cons: runtime may need optimization for large-scale ingestion; further profiling required before production scale.
