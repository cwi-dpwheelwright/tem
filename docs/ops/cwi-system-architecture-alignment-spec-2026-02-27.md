# CWI System Architecture Alignment Specification (Codex-ready)

Document date: 2026-02-27
Baseline: CWI 2025.10H ("10H")
Status: Draft alignment spec (validated vs repo)
Sanitization: This spec intentionally uses placeholders (e.g., <CORE_HOST>) instead of internal IPs/hostnames. Populate values via environment overlays / IaC.

Important note: this is a historical alignment draft, not the current runtime
stack source of truth. For the active stack, runtime placement, and
intelligence flow, use `docs/architecture/application-architecture.md`.

Validated against (repo sources of truth):
- docs/architecture/application-architecture.md
- docs/ops/current-application-stack.md
- docs/zoning-deployments/cwi_app_zone_map_2026-02-10_v3.md
- OPS/cwi-ops/env/ENV_VAR_MATRIX.json
- OPS/cwi-ops/compose/* and OPS/cwi-ops/DEPLOY/*
- OPS/cwi-ops/runbooks/pack-mirror-minio.md

---

## 0. Intended use by Codex (how to interpret this spec)

This document is designed to be machine-actionable by a coding agent (Codex) while remaining reviewable by humans.

Normative source of truth
- Appendix A contains the ARCH_SPEC YAML. Treat it as the single source of truth for:
  - node roles and placement,
  - service inventory,
  - network flows (allowed edges),
  - knowledge-pack lifecycle (publish -> adopt -> serve),
  - vector topology and integrity monitoring (Critic status).

Expected Codex outputs (not included here)
- Docker Compose bundles per node role (Gateway / Core / Teacher / Vector Shards / Compute / Ops).
- Gateway routes (Caddyfile) and pack-service static serving config (Nginx or API service).
- Pack mirror + anchor verification scripts (Core).
- Critic systemd unit + timer files (Teacher) if/when implemented.
- Verification scripts / smoke tests (curl + Qdrant queries) and gold-test suite hooks.

---

## 1. Problem framing

### Goal
Align the current deployed stack to the 10H baseline and the Phase 2-4 Knowledge Infrastructure topology, producing a clear, codex-parsable specification of:
- what runs where
- what talks to what
- what must be true (invariants)
- how to verify it

### Constraints
- No secrets in-spec; endpoints/identifiers must be parameterized.
- Fail-closed behavior where required by the baseline (approvals, pack integrity, ledger).
- Must account for dual vector shard topology and the Teacher/MinIO publication boundary.
- Must incorporate operational governance: deterministic adoption, immutable serving, version pins.
- Deployment supports two pack-service modes (API service or static Nginx). Spec must describe both.

### Success criteria (definition of "aligned")
A deployment is aligned when all are true:
1. Primary request path works: Clients -> Gateway -> Router -> Orchestrator (ASK/ACTION).
2. Pack lifecycle is deterministic and governance-separated:
   - Teacher publishes to immutable storage,
   - Core adopts only after anchor verification,
   - Pack-service serves adopted packs read-only,
   - Orchestrator consumes only via governance pins (pins are currently a gap; see Section 10).
3. Evidence plane operates as dual-shard, deterministic, rebuildable memory.
4. Integrity monitoring (Critic) continuously verifies pack hashes and cross-shard drift without blocking runtime (Critic not yet implemented; see Section 10).
5. Baseline invariants are enforced and testable: pack trust, trace contract, approvals, provenance, kill-switch.

---

## 2. Options (representation formats) and recommendation

| Option | What you get | Time/Cost | Risk | Complexity |
|---|---|---:|---:|---:|
| A | Markdown-only architecture narrative | Low | Medium (hard to automate) | Low |
| B (recommended) | Markdown narrative + machine-readable YAML ARCH_SPEC | Medium | Low | Medium |
| C | Full IaC generation (Compose/Helm) + code scaffolds + tests | High | Medium (overfit) | High |

Recommendation: Option B. It balances human review with Codex automation and keeps the truth in a single structured spec.

---

## 3. Architecture invariants (non-negotiables)

These invariants are a blend of the 10H baseline contracts and the Phase 2-4 knowledge-infrastructure governance boundaries.

### 3.1 Pack trust (integrity)
- Pack artifacts are pack.json + anchor.txt.
- Anchor is SHA-256 over canonical JSON.
- Canonicalization used in this repo is JCS-style (sorted keys, separators ",:", UTF-8; see OPS/cwi-ops/scripts/verify_pack_anchor.py and tools/teacher-node/pack_utils.py). This is RFC8785-like but not full RFC8785.
- Clients and/or core verification must fail closed if the anchor does not match.

### 3.2 Trace contract (explainability)
Every ASK/ACTION response includes, at minimum:
- trace.sources[]
- trace.confidence
- trace.policy_mode
- trace.model
- trace.pack_hash

### 3.3 Approval-gated actions + idempotency
- If risk is not low or outside safe bounds: return an ApprovalTicket; do not execute.
- Approval completion must be idempotent.

### 3.4 Provenance / ledger (tamper-evident audit)
- ASK/ACTION/TELEMETRY/PACK events must be recorded to an append-only, hash-chained ledger.
- In the current implementation, the ledger lives in the orchestrator Postgres database (ledger_events, ledger_outbox).
- Fail closed for protected operations if ledger append is unavailable.

### 3.5 Kill-switch
- Must be non-bypassable and immediately stop ACTION execution when engaged.

### 3.6 Knowledge infrastructure governance (Phase 4 operational boundary)
- Teacher publishes (authoritative).
- Core adopts after verification and records adoption metadata (mirror script currently does not emit ledger events; see gaps).
- Pack-service serves adopted packs read-only (immutable serving boundary).
- Orchestrator consumes packs subject to pins (pins not enforced yet; see gaps).

---

## 4. Observed vs target topology (alignment view)

### 4.1 Observed node placement (from docs/ops/current-application-stack.md)
Current stack is a 5-node placement:
- public-gateway-01: cwi-gateway + cwi-gpt-web-ui
- internal-mcp-01: router, orchestrator, coordination-layer, identity, pack-service, teams-bot, consent-ingest, redis, postgres, graphdb
- internal-teacher-01: teacher-node (MinIO optional)
- vector-shard-1: Qdrant
- vector-shard-2: Qdrant

Note: Zoning docs allow internal-02 to host data/teacher/compute; in some environments core + data are co-located on internal-mcp-01.

### 4.2 Observed running services (from compose/runbooks)
- cwi-gateway
- cwi-router
- cwi-orchestrator
- cwi-coordination-layer
- cwi-identity-server (+ identity admin optional)
- cwi-pack-service
- redis + postgres (identity/orchestrator/coordination DBs)
- graph-db
- teacher-node (+ teacher-minio optional)
- cwi-teams-bot
- cwi-consent-ingest (ETL)
- gpt UI (glassbox/admin optional)
- model-worker-health probes

---

## 5. Aligned node-role topology (logical)

### 5.1 Zones
- public: Internet-exposed ingress host and public UI surfaces (gateway + public UIs)
- internal-01: Core control-plane/services (routing, orchestration, identity, bots, pack serving)
- internal-02: Data/evidence/compute/tooling layers (datastores, vector/graph, model workers, teacher tooling)
- client-restricted: Client-site teacher node (validator/eval only)
- ops: Observability hub (if deployed)

### 5.2 Node roles (minimum set)
- Gateway node: TLS termination + routes /api/*, /packs/*, /artifacts/*, /auth/*, /jwks*, /coordination/* (+ optional public UI)
- Core node: Router + Orchestrator + Pack-service + Identity + Teams approvals + state stores (+ optional UIs)
- Teacher node: pack canonicalization/anchor generation + publish to MinIO + artifact store
- Vector shard nodes (2): Qdrant primary collection (derived memory)
- Optional compute nodes: model endpoints (Pro, Free/overflow)
- Optional ops hub: dashboards and traces

---

## 6. Network edges (allowlist flows)

The system should be operable using only the following high-level edges (exact ports are environment-specific):

1. Clients -> Gateway: HTTPS (443)
2. Gateway -> Core.Router: internal HTTP(S) for /api/*
3. Gateway -> Core.Orchestrator: internal HTTP(S) for /api/ledger/* and /api/reconciliation/*
4. Gateway -> Core.PackService: internal HTTP(S) for /packs/*, /artifacts/*, /swagger/*
5. Gateway -> Core.Identity: internal HTTP(S) for /auth/* and /jwks*
6. Gateway -> Core.Coordination: internal HTTP(S) for /coordination/*
7. Gateway -> Public UI(s): internal HTTP(S)
8. Core.Router -> Core.Orchestrator: internal HTTP(S)
9. Core.Router -> Model endpoints: internal HTTP(S) for health/tier selection
10. Core.Orchestrator -> VectorShard1/2: vector DB API (HTTP and/or gRPC)
11. Core.Orchestrator -> GraphDB: internal DB protocol (optional)
12. Core.Orchestrator -> PackService (PACKS_BASE_URL): internal HTTP(S)
13. Teacher -> MinIO: S3 API (publisher identity)
14. Core (pack mirror) -> MinIO: S3 API (read-only)
15. Teacher -> Orchestrator: optional provenance/artifact registration endpoints

Security note: Evidence plane and data stores must not be reachable from the public internet. Only the gateway is publicly exposed.

---

## 7. Knowledge pack lifecycle (Phase 3-4 aligned)

### 7.1 Publication (Teacher boundary)
Teacher responsibilities
- Generate pack.json
- Canonicalize deterministically (JCS-style)
- Generate anchor.txt (sha256)
- Publish versioned pack artifacts to MinIO bucket (cwi-packs-published)
- Published layout in MinIO:
  - <pack_id>/latest.txt
  - <pack_id>/<hash>/pack.json
  - <pack_id>/<hash>/anchor.txt

### 7.2 Adoption (Core mirror boundary)
Core mirror responsibilities
- Pull pack.json + anchor.txt from published storage (MinIO)
- Verify anchor using /opt/cwi/bin/verify_pack_anchor.py
- Replace live directory only after verification
- Update /srv/cwi/packs/index.json and current_hash.txt
- Note: pack_mirror_one.sh does not currently emit PACK_ADOPTED ledger events (alignment gap)

### 7.3 Serving (Pack-service boundary)
Pack-service modes in repo:
- API service (cwi-pack-service image): supports pack ingest/metadata APIs; PACKS_DIR is read/write.
- Static Nginx (OPS/cwi-ops/compose/pack-service/nginx.conf): serves /packs and /artifacts from PACKS_DIR; mount should be read-only.

Pack-service serves:
- /packs/index.json
- /packs/{pack_id}/pack.json
- /packs/{pack_id}/anchor.txt
- /packs/{pack_id}/{hash}/pack.json
- /packs/{pack_id}/latest/anchor.txt
- /artifacts/reports/index.json
- /artifacts/docs/index.json

### 7.4 Governance pins (Orchestrator boundary)
- Pins are not implemented in the current codebase.
- Keep as a future enforcement point for pack selection (latest_stable / approved / experiment).

---

## 8. Vector topology (Phase 2-3 aligned)

### 8.1 Baseline configuration
- Two Qdrant shards, symmetric configuration.
- Collection name comes from VECTOR_COLLECTION (default varies by environment).
- Vector dimension comes from VECTOR_EMBEDDING_DIMENSION (tests use 8; production often 1536).

### 8.2 Determinism expectations
- Vectors are derived, rebuildable memory.
- Dual-shard ingestion is expected to be identical across shards.
- Some environments may point both VECTOR_SHARD_1_URL and VECTOR_SHARD_2_URL to a single shard (degraded mode; see gaps).

---

## 9. Critic integrity monitoring (Phase 3.5 aligned)

Critic is not implemented in this repo as of 2026-02-27. Keep this section as a planned component.

Expected future behavior:
- Non-blocking observer mode (does not halt Teacher or shards)
- Verifies pack sha256 integrity (downloads pack.json and recomputes hash)
- Verifies cross-shard drift (payload hash comparison after deterministic JSON normalization)
- Writes reports to teacher-node/critic/reports/<timestamp>/critic_report.json
- Runs every 10 minutes via systemd timer (cwi-critic-scan.timer)

---

## 10. Alignment gaps / open confirmations (Codex TODO list)

1. Critic integrity monitoring
- No critic scripts, systemd units, or reports path in repo. Implement if required by baseline.

2. Pack adoption ledger events
- pack_mirror_one.sh updates packs on disk but does not emit PACK_ADOPTED ledger entries.

3. Governance pins (Phase 5)
- No pin files or enforcement logic in orchestrator.

4. Pack-service artifacts route in DEPLOY config
- OPS/cwi-ops/DEPLOY/pack-service/nginx.conf does not serve /artifacts; compose config does. Align DEPLOY config or use API pack-service.

5. Dual-shard enforcement
- Some environments use a single shard for both VECTOR_SHARD_1_URL and VECTOR_SHARD_2_URL. This should be treated as degraded mode.

6. mTLS posture
- Router/orchestrator support MTLS_REQUIRED and CA paths, but internal service-to-service mTLS is not universally enforced. Confirm per environment.

7. Ledger signing / KMS integration
- Ledger schema supports signature fields; ADRs mention PQC/KMS, but no wiring is visible in deployment configs.

Codex should treat these as alignment tasks and either:
- locate the missing config/code and update ARCH_SPEC, or
- generate the missing components/configurations (if truly absent).

---

## Appendix A -- ARCH_SPEC (machine-readable YAML)

```yaml
arch_spec_version: "2026-02-27"
baseline_release: "CWI 2025.10H (10H)"

# All environment-specific values MUST be provided via env/overlays.
placeholders:
  GATEWAY_FQDN: "<GATEWAY_FQDN>"
  GATEWAY_HTTP_PORT: "<GATEWAY_HTTP_PORT>"
  GATEWAY_HTTPS_PORT: "<GATEWAY_HTTPS_PORT>"
  ROUTER_HTTP_PORT: "<ROUTER_HTTP_PORT>"
  ORCHESTRATOR_HTTP_PORT: "<ORCHESTRATOR_HTTP_PORT>"
  PACK_SERVICE_HTTP_PORT: "<PACK_SERVICE_HTTP_PORT>"
  ROUTER_HOSTPORT: "<ROUTER_HOSTPORT>"
  ORCHESTRATOR_HOSTPORT: "<ORCHESTRATOR_HOSTPORT>"
  PACK_SERVICE_HOSTPORT: "<PACK_SERVICE_HOSTPORT>"
  IDENTITY_HOSTPORT: "<IDENTITY_HOSTPORT>"
  COORDINATION_HOSTPORT: "<COORDINATION_HOSTPORT>"
  PACKS_BASE_URL: "<PACKS_BASE_URL>"
  PACKS_DIR: "<PACKS_DIR>"
  VECTOR_SHARD_1_URL: "<VECTOR_SHARD_1_URL>"
  VECTOR_SHARD_2_URL: "<VECTOR_SHARD_2_URL>"
  VECTOR_SHARD_1_HOST: "<VECTOR_SHARD_1_HOST>"
  VECTOR_SHARD_2_HOST: "<VECTOR_SHARD_2_HOST>"
  VECTOR_SHARD_HTTP_PORT: "<VECTOR_SHARD_HTTP_PORT>"
  VECTOR_EMBEDDING_DIMENSION: "<VECTOR_EMBEDDING_DIMENSION>"
  VECTOR_COLLECTION: "<VECTOR_COLLECTION>"
  MINIO_ENDPOINT: "<MINIO_ENDPOINT>"
  MINIO_BUCKET: "<MINIO_BUCKET>"
  MODEL_PRO_ENDPOINT_1: "<MODEL_PRO_ENDPOINT_1>"
  MODEL_FREE_ENDPOINT_1: "<MODEL_FREE_ENDPOINT_1>"
  MODEL_BASE_URL: "<MODEL_BASE_URL>"

zones:
  public: {}
  internal-01: {}
  internal-02: {}
  client-restricted: {}
  ops: {}

observed_runtime_inventory:
  # Observed running services list (placement may vary by environment)
  nodes:
    public-gateway-01:
      zone: public
      services: ["cwi-gateway", "cwi-gpt-web-ui"]
    internal-mcp-01:
      zone: internal-01
      services:
        - "cwi-router"
        - "cwi-orchestrator"
        - "cwi-coordination-layer"
        - "cwi-identity-server"
        - "cwi-pack-service"
        - "cwi-teams-bot"
        - "cwi-consent-ingest"
        - "redis"
        - "postgres"
        - "graph-db"
    internal-teacher-01:
      zone: internal-02
      services: ["teacher-node", "teacher-minio(optional)"]
    vector-shard-1:
      zone: internal-02
      services: ["qdrant"]
    vector-shard-2:
      zone: internal-02
      services: ["qdrant"]
  services_up:
    - "cwi-gateway"
    - "cwi-router"
    - "cwi-orchestrator"
    - "cwi-coordination-layer"
    - "cwi-identity-server"
    - "cwi-pack-service"
    - "redis"
    - "postgres"
    - "graph-db"
    - "teacher-node"
    - "cwi-teams-bot"
    - "cwi-consent-ingest"
    - "cwi-gpt-web-ui"

nodes:
  gateway:
    zone: public
    role: "public_ingress"
    services:
      - name: "cwi-gateway"
        kind: "reverse_proxy"
        listens:
          - proto: "http"
            port: "<GATEWAY_HTTP_PORT>"
          - proto: "https"
            port: "<GATEWAY_HTTPS_PORT>"
        routes:
          - path_prefix: "/api/"
            upstream: "core.router"
          - path_prefix: "/api/ledger/"
            upstream: "core.orchestrator"
          - path_prefix: "/api/reconciliation/"
            upstream: "core.orchestrator"
          - path_prefix: "/packs/"
            upstream: "core.pack_service"
          - path_prefix: "/artifacts/"
            upstream: "core.pack_service"
          - path_prefix: "/auth/"
            upstream: "core.identity_server"
          - path_prefix: "/jwks"
            upstream: "core.identity_server"
          - path_prefix: "/coordination/"
            upstream: "core.coordination_layer"
          - path_prefix: "/swagger"
            upstream: "core.pack_service"
          - path_prefix: "/mcp/"
            upstream: "core.router" # MCP alias toggled via env
        security:
          tls_termination: true
      - name: "ui_gpt_web"
        component: "cwi-gpt-web-ui"
        kind: "ui_surface"
        placement_confidence: "observed_in_stack"
      - name: "ui_glassbox"
        component: "cwi-glassbox-web-ui"
        kind: "ui_surface"
        placement_confidence: "optional"

  core:
    zone: internal-01
    role: "api_mcp_core"
    placement_notes: "Core services are co-located with data stores in current deployments; some environments split data to internal-02."
    services:
      - name: "router"
        component: "cwi-router"
        kind: "api_router"
        listens: [{proto: "http", port: "<ROUTER_HTTP_PORT>"}]
        depends_on: ["orchestrator", "redis", "identity_server"]
        outbound:
          model_pro_endpoints: ["<MODEL_PRO_ENDPOINT_1>"]
          model_free_endpoints: ["<MODEL_FREE_ENDPOINT_1>"]
      - name: "orchestrator"
        component: "cwi-orchestrator"
        kind: "workflow_engine"
        listens: [{proto: "http", port: "<ORCHESTRATOR_HTTP_PORT>"}]
        depends_on: ["orchestrator_db", "redis", "pack_service", "vector_shard_1", "vector_shard_2"]
      - name: "coordination_layer"
        component: "cwi-coordination-layer"
        kind: "coordination_plane"
      - name: "pack_service"
        component: "cwi-pack-service"
        kind: "pack_service"
        listens: [{proto: "http", port: "<PACK_SERVICE_HTTP_PORT>"}]
        mounts:
          - host_path: "<PACKS_DIR>"
            container_path: "/packs"
            mode: "ro|rw"
        deployment_modes:
          - "api_service (cwi-pack-service image; supports ingest/metadata)"
          - "static_nginx (OPS/cwi-ops/compose/pack-service/nginx.conf)"
        serves:
          - "/packs/index.json"
          - "/packs/{pack_id}/pack.json"
          - "/packs/{pack_id}/anchor.txt"
          - "/packs/{pack_id}/{hash}/pack.json"
          - "/packs/{pack_id}/latest/anchor.txt"
          - "/artifacts/reports/index.json"
          - "/artifacts/docs/index.json"
      - name: "identity_server"
        component: "cwi-identity-server"
        kind: "idp"
      - name: "identity_admin"
        component: "cwi-identity-admin"
        kind: "admin_surface"
        placement_confidence: "optional"
      - name: "teams_bot"
        component: "cwi-teams-bot"
        kind: "approvals_surface"
      - name: "consent_ingest"
        component: "cwi-consent-ingest"
        kind: "etl_job"
      - name: "redis"
        kind: "state_store"
      - name: "orchestrator_db"
        component: "postgres"
        kind: "state_store"
      - name: "identity_db"
        component: "postgres"
        kind: "state_store"
      - name: "coordination_db"
        component: "postgres"
        kind: "state_store"
      - name: "graph_db"
        component: "graph-db"
        kind: "optional_evidence_store"
      - name: "model_worker_health"
        component: "model-worker-health"
        kind: "probe"

    pack_adoption:
      script: "/opt/cwi/bin/pack_mirror_one.sh"
      verify_script: "/opt/cwi/bin/verify_pack_anchor.py"
      source_bucket: "cwi-packs-published"
      minio_alias: "teacher"
      live_root: "/srv/cwi/packs"
      staging_root: "/srv/cwi/packs_staging"
      versions_root: "/srv/cwi/packs_versions"
      index_file: "/srv/cwi/packs/index.json"
      ledger_event: "not_emitted_by_script"

  teacher:
    zone: internal-02
    role: "authoritative_publisher"
    services:
      - name: "teacher_node"
        component: "cwi-teacher-node"
        kind: "publisher_toolchain"
      - name: "minio"
        kind: "artifact_store"
        placement_confidence: "optional"

  teacher_client:
    zone: client-restricted
    role: "validator"
    placement_confidence: "optional"
    services:
      - name: "teacher_node"
        component: "cwi-teacher-node"
        kind: "validator_toolchain"

  vector_shard_1:
    zone: internal-02
    role: "vector_db_shard"
    services:
      - name: "qdrant"
        kind: "vector_db"
        engine: "qdrant"
        collection: "<VECTOR_COLLECTION>"
        vector_dimension: "<VECTOR_EMBEDDING_DIMENSION>"
        distance_metric: "cosine"

  vector_shard_2:
    zone: internal-02
    role: "vector_db_shard"
    services:
      - name: "qdrant"
        kind: "vector_db"
        engine: "qdrant"
        collection: "<VECTOR_COLLECTION>"
        vector_dimension: "<VECTOR_EMBEDDING_DIMENSION>"
        distance_metric: "cosine"

model_endpoints:
  pro:
    zone: internal-02
    endpoints: ["<MODEL_PRO_ENDPOINT_1>"]
  free:
    zone: internal-02
    endpoints: ["<MODEL_FREE_ENDPOINT_1>"]
  orchestrator:
    base_url: "<MODEL_BASE_URL>"

invariants:
  pack_trust:
    canonicalization: "JCS-style (sorted keys, separators ',:' UTF-8)"
    hash: "sha256"
    fail_closed: true
  approval_gating:
    required_for_risk: ["medium", "high", "unknown"]
    idempotent_completion: true
    fail_closed_if_ledger_down: true
  provenance:
    required_event_types: ["ASK", "ACTION", "TELEMETRY", "PACK_PUBLISHED", "PACK_ADOPTED"]
    ledger_backend: "postgres"
    fail_closed_when_unavailable_for: ["ACTION"]
  kill_switch:
    non_bypassable: true
    scope: ["ACTION"]
  trace_contract:
    required_fields: ["sources", "confidence", "policy_mode", "model", "pack_hash"]

verification:
  - id: "gateway_routes_packs_index"
    type: "http"
    request: "GET https://<GATEWAY_FQDN>/packs/index.json"
    expect: ["status=200"]
  - id: "gateway_routes_artifacts_reports"
    type: "http"
    request: "GET https://<GATEWAY_FQDN>/artifacts/reports/index.json"
    expect: ["status=200"]
  - id: "pack_service_fail_closed_invalid_pack"
    type: "http"
    request: "GET https://<GATEWAY_FQDN>/packs/does-not-exist/latest/"
    expect: ["status=404"]
  - id: "gateway_ready"
    type: "http"
    request: "GET https://<GATEWAY_FQDN>/readyz"
    expect: ["status=200"]
  - id: "pack_mirror_timer"
    type: "host"
    request: "systemctl list-timers | grep cwi-pack-mirror"
    expect: ["timer_present=true"]
  - id: "qdrant_collection_exists_shard_1"
    type: "http"
    request: "GET http://<VECTOR_SHARD_1_HOST>:<VECTOR_SHARD_HTTP_PORT>/collections/<VECTOR_COLLECTION>"
    expect: ["status=200"]
  - id: "qdrant_collection_exists_shard_2"
    type: "http"
    request: "GET http://<VECTOR_SHARD_2_HOST>:<VECTOR_SHARD_HTTP_PORT>/collections/<VECTOR_COLLECTION>"
    expect: ["status=200"]
```

---

## Appendix B -- Verification checklist (testable steps)

Replace placeholders with real values from your environment overlays.

### B.1 Gateway + pack serving
- curl -fsS https://<GATEWAY_FQDN>/packs/index.json
- Negative test (fail closed): curl -i https://<GATEWAY_FQDN>/packs/does-not-exist/latest/ -> expect 404
- Artifacts index (if enabled): curl -fsS https://<GATEWAY_FQDN>/artifacts/reports/index.json

### B.2 Pack adoption receipts (mirror)
- On Core: /opt/cwi/bin/pack_mirror_one.sh enterprise
- Verify index.json exists at /srv/cwi/packs/index.json

### B.3 Pins visible in orchestrator (future)
- Not implemented; placeholder for Phase 5.

### B.4 Vector shard symmetry (spot check)
- curl -fsS http://<VECTOR_SHARD_1_HOST>:<VECTOR_SHARD_HTTP_PORT>/collections/<VECTOR_COLLECTION>
- curl -fsS http://<VECTOR_SHARD_2_HOST>:<VECTOR_SHARD_HTTP_PORT>/collections/<VECTOR_COLLECTION>
- Compare: vector size matches VECTOR_EMBEDDING_DIMENSION, distance = Cosine

### B.5 Teacher MinIO health (if used)
- curl -s -o /dev/null -w "%{http_code}\n" http://<MINIO_ENDPOINT>/minio/health/live

---

## Appendix C -- Glossary

- Teacher: authoritative publishing boundary for knowledge packs (canonicalize + anchor + publish).
- Core mirror adoption: deterministic pack adoption boundary that verifies anchors before updating live packs.
- Pack-service: immutable serving boundary for adopted pack bytes (static) or API service in ingest mode.
- Pins: operational governance controls selecting which pack versions orchestrator may consume (not implemented yet).
- Critic: non-blocking integrity monitor verifying pack hashes and cross-shard drift (planned).
