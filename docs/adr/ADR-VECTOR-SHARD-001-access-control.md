# ADR-VECTOR-SHARD-001: Vector Shard Access Control

## Status
Accepted

## Context
Vector shard (Qdrant) runs on the internal `cwi-internal` network and is consumed by
orchestrator/ETL/connectors. Some deployments require explicit auth, while others
prefer the lowest-operational-overhead internal-only posture.

## Decision
Default to internal network isolation only (no public ports). For deployments that
need additional controls, enable Qdrant API key auth via container environment. We
will not require an mTLS sidecar by default because internal network isolation plus
optional API keys meets current threat models and simplifies operations.

## Consequences
- Pros: simpler deploys, fewer certificates to rotate, minimal overhead for internal-only access.
- Cons: environments needing strict mutual authentication must layer on API keys or
  deploy a sidecar/proxy with mTLS.
