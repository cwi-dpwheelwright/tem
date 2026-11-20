# ETL / Graph Runtime Audit — 2026-03-20

Prepared for: Demitrius / leadership / platform ops  
Purpose: Determine where ETL and graph-related apps are actually running right now.

## Bottom Line

The live host audit shows:

- teacher-side graph tooling is currently running via `teacher-node`, not as standalone ETL microservices
- graph databases are currently running on both `internal-mcp-01` and `internal-teacher-01`
- vector retrieval is running on `vector-shard-1` and `vector-shard-2` via Qdrant
- `cwi-consent-ingest` is present on `internal-mcp-01` as `internal-01-etl-consent-ingest`, but it is currently stuck in a restart loop

## Audit Method

This audit was derived from live `cAdvisor` container metadata on each server at port `8082`, plus targeted health checks.

Direct `docker ps` via SSH was still blocked by Tailscale SSH approval requirements, so the results below are based on live host container metadata rather than direct Docker CLI output.

## Where the ETL / Graph Apps Are Running

## 1. Teacher-side graph tooling

### Running host

- `internal-teacher-01` (`100.87.205.59`)

### Live containers observed

- `client-restricted-teacher-node` -> `local/cwi-teacher-node:10h`
- `internal-02-teacher-node` -> `local/cwi-teacher-node:10h`

### Live compose roots observed from labels

- `/opt/cwi/DEPLOY/teacher_node/docker-compose.yml`
- `/root/cwi-teacher-node-internal/docker-compose.yml`

### Interpretation

This is the clearest current answer for the graph-prep side:

- ontology
- entity extraction
- relationship extraction
- rollup
- curation
- crosswalk
- AGE artifact generation

are currently packaged into and operationally hosted through `teacher-node`.

There is no evidence from the live container inventory that these are running as separate standalone containers.

## 2. Graph database on the MCP host

### Running host

- `internal-mcp-01` (`100.120.208.51`)

### Live container observed

- `internal-02-data-graph-db` -> `apache/age:latest`

### Live compose root observed

- `/opt/cwi/DEPLOY/mcp-core/docker-compose.yml`

### Compose service label

- `cwi-graph-db`

### Interpretation

The MCP host is currently running a graph database as part of the core bundle.

This is the graph database that aligns with the repo deployment model for:

- runtime orchestrator graph access
- core internal graph-backed reasoning support

## 3. Graph database on the teacher host

### Running host

- `internal-teacher-01` (`100.87.205.59`)

### Live container observed

- `cwi-age-db` -> `apache/age:release_PG16_1.6.0`

### Live compose root observed

- `/opt/cwi/graph_db/docker-compose.yml`

### Compose service label

- `cwi-age-db`

### Interpretation

A second Apache AGE instance is currently running on the teacher host.

This strongly suggests one of the following:

- teacher-side graph build/staging is happening against a separate AGE instance
- the teacher host retained an older graph deployment model
- or graph infrastructure has drifted and is now split across both teacher and MCP hosts

This should be treated as an explicit architecture reconciliation item.

## 4. Vector retrieval hosts

### Running hosts

- `vector-shard-1` (`100.115.118.16`)
- `vector-shard-2` (`100.91.142.103`)

### Live containers observed

- `qdrant` -> `qdrant/qdrant:latest` on both hosts

### Interpretation

These hosts are live retrieval infrastructure, but they are not teacher-side graph prep.

They are relevant because orchestrator health on the MCP host reported both vector shards as `ok`.

## What Was Not Found

## 1. `cwi-consent-ingest` exists, but is unhealthy

Direct `docker ps -a` on `internal-mcp-01` showed:

- `internal-01-etl-consent-ingest` -> `local/cwi-etl:10h`
- status: `Restarting (1)`

Direct `docker inspect` showed:

- `Status`: `restarting`
- `ExitCode`: `1`

Recent logs showed:

- `PermissionError: [Errno 13] Permission denied: '/srv/cwi'`
- `FileNotFoundError: [Errno 2] No such file or directory: '/srv/cwi/consent-input'`

Interpretation:

- the ETL ingest container is deployed on the MCP host
- but it is not healthy or functioning at the moment
- the immediate issue appears to be a host-path / mount / permissions problem around the consent input path

## 2. No separate graph-tool containers found

There were no standalone live containers corresponding to:

- ontology
- entity extractor
- relationship extractor
- rollup
- curation
- crosswalk
- AGE writer

That is consistent with the current repo direction, where these live as repo-native ETL modules and run through `teacher-node`.

## Current Runtime Placement Summary

| Runtime concern | Live location now | Evidence |
| --- | --- | --- |
| Teacher-side graph prep | `internal-teacher-01` | two `local/cwi-teacher-node:10h` containers |
| Core graph DB | `internal-mcp-01` | `internal-02-data-graph-db` / `apache/age:latest` |
| Teacher/staging graph DB | `internal-teacher-01` | `cwi-age-db` / `apache/age:release_PG16_1.6.0` |
| Vector retrieval | `vector-shard-1`, `vector-shard-2` | `qdrant` containers on both hosts |
| Standalone ETL ingest job | `internal-mcp-01` | `internal-01-etl-consent-ingest` / `local/cwi-etl:10h` / restarting |

## Operational Conclusion

The current live environment **does** include a separate ETL ingest containerized app on the MCP host.

Instead:

- teacher-side graph ETL functionality is running through `teacher-node`
- graph storage is split across both MCP and teacher hosts
- vector retrieval is live on the two Qdrant hosts
- the consent ingest ETL container is present but currently unhealthy

## Recommended Next Step

Do a second-pass SSH audit as soon as Tailscale SSH approval or deploy keys are available, and explicitly confirm:

1. fix the `cwi-consent-ingest` host path / permissions problem on `/srv/cwi/consent-input`
2. why both `internal-mcp-01` and `internal-teacher-01` are running Apache AGE
3. which of the two teacher-node stacks on `internal-teacher-01` is the intended one
4. whether the teacher host graph DB is staging-only, legacy, or still part of the active runtime path
