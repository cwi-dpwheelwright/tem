# ADR-PLAT-002: Graph DB Selection

## Status
Accepted

## Context
GraphRAG and ownership lineage features require a property-graph store that can
express nodes, edges, and traversal queries with tenant isolation. The platform
already operates PostgreSQL and prefers minimizing new infrastructure while
keeping an upgrade path to a dedicated graph database if needed.

## Decision
- Adopt PostgreSQL with the Apache AGE extension as the baseline graph store.
- Use Cypher as the primary query language.
- Standardize node/edge payload contracts via
  `contracts/json-schema/graph_node.json` and `contracts/json-schema/graph_edge.json`.
- Keep the graph interface DB-agnostic so a future migration to a dedicated
  graph database (Neo4j/Memgraph/ArangoDB) remains possible without contract
  changes.

## Consequences
- Pros: reuses existing Postgres operations, backups, and security controls; no
  additional service required for baseline deployments.
- Cons: AGE maturity/performance must be validated for large-scale graphs;
  migrations to a dedicated graph DB may be required as the graph grows.
