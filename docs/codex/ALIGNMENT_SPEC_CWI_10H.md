# CWI Platform - Codex Alignment Specification (CWI 2025.10H / "10H")

**Document purpose:** A single, comprehensive "alignment" spec you can hand to Codex so it can generate code, plans, and changes that stay inside CWI's architectural invariants, governance model, and operational boundaries.

**Release baseline:** **CWI 2025.10H ("10H")**
**Spec version:** 2026-03-04
**Classification:** Internal - Sanitized (no secrets; no credentials; use placeholders)

---

## 0) How to use this with Codex (copy/paste guidance)

### 0.1 Paste as "Project Instructions"
If your Codex environment supports a persistent project prompt / instructions field, paste Sections 1-6 verbatim.

### 0.2 Keep this file as the "single entrypoint"
Recommended repo placement:
- `docs/codex/ALIGNMENT_SPEC_CWI_10H.md` (or)
- `/.codex/ALIGNMENT.md`

Codex should treat the documents indexed in Section 9 as the authoritative source of truth.

---

## 1) Goal, scope, and constraints Codex MUST respect

### 1.1 Goal
Help engineers ship CWI features and fixes without drifting from the 10H architecture and the already-validated governance + pack lifecycle (Phases 1-8).

### 1.2 Scope (what Codex is expected to touch)
Codex may be asked to work on:
- **Core runtime**: router, orchestrator (MCP), pack-service, identity
- **Ingress**: gateway reverse proxy, Cloudflare tunnel configuration, hardening
- **Knowledge plane**: teacher publishing pipeline, MinIO artifact custody, shard ingestion, critic
- **Evidence plane**: Qdrant vector shards (2 node topology), optional graph DB
- **Clients**: web UI(s), admin console, iOS/CarPlay, Android/Auto, Teams bot
- **Ops**: docker compose bundles, CI gates, runbooks, observability, ORR artifacts
- **Contracts-first artifacts**: OpenAPI, JSON Schemas, fixtures, gold tests
- **Backlog/iteration work items**: implementing items that already have ACs (acceptance criteria)

### 1.3 Constraints / hard rules (non-negotiable)
Codex must never produce changes that:
- break **deterministic pack trust** (canonicalization + sha256 anchor verification)
- break the **frozen API/trace contract** used by clients, tests, and evidence
- allow risky actions to execute without policy gating + approvals
- make the provenance ledger optional, bypassable, or non-verifiable
- weaken kill-switch semantics or add bypass routes
- introduce runtime "mocks" or "stubs" in production paths
- leak secrets (tokens, keys, real customer identifiers, internal endpoints)

If a request conflicts with any invariant above, Codex should refuse the change and propose a compliant alternative.

---

## 2) CWI in one page (mental model)

CWI is a policy-governed orchestration layer that sits above a base LLM and turns it into an auditable system:

- **Gateway** terminates inbound traffic and routes to internal services.
- **Router** authenticates/authorizes, rate-limits, and forwards to orchestration.
- **Orchestrator (MCP)** runs the ASK/ACTION workflows:
  - retrieve evidence from vector shards / graph db,
  - call model endpoints (Pro / Free),
  - enforce policies and approvals,
  - emit provenance records (append-only ledger),
  - enforce kill-switch.
- **Pack-service** serves static `pack.json` + `anchor.txt` byte-for-byte as published.
- **Teacher** deterministically publishes packs (canonical JSON + sha256 anchor), ships evidence bundles, and holds immutable custody via MinIO.
- **Vector shards** (2x Qdrant) store derived, rebuildable memory.
- **Pins + Pin Resolver** enforce cryptographic governance over which pack versions can be used at runtime.
- **Critic** continuously verifies integrity/drift (non-blocking).

---

## 3) 10H invariants Codex MUST preserve (these are release blockers)

> Treat these as "physics." If code violates them, it is not CWI.

### 3.1 Invariant A - Pack trust model (clients verify before render)
- Pack artifacts are verified by clients via:
  - Canonical JSON (RFC 8785 / JCS)
  - `sha256(canonical(pack.json)) == anchor.txt`
- Clients MUST NOT render/cache pack-driven UI unless verification succeeds.
- Pack-service MUST NOT transform JSON (no reserialization); it must serve bytes exactly as published.

### 3.2 Invariant B - Frozen API + trace contract (responses are explainable)
Every ASK/ACTION response includes trace fields (minimum):
- `trace.sources[]`
- `trace.confidence`
- `trace.policy_mode`
- `trace.model`
- `trace.pack_hash`

### 3.3 Invariant C - Approval tickets for risky actions
If an action is not low risk or outside safe bounds:
- return an **ApprovalTicket**
- do not execute until approved (idempotent completion)

### 3.4 Invariant D - Provenance ledger is mandatory (append-only + hash-chained)
- ASK/ACTION/TELEMETRY events must append to a tamper-evident ledger.
- Deployments are invalid if provenance is disabled or can silently fail.

### 3.5 Invariant E - Kill-switch (immediate, non-bypassable)
- Kill-switch must prevent action execution immediately.
- There must be no "alternate route" that bypasses it.

### 3.6 Invariant F - Contracts-first + gold tests (no drift)
- OpenAPI + JSON Schemas + fixtures are the source of truth.
- Gold tests (deterministic) are merge/release gates.
- Contract changes require fixtures + tests updates in the same change.

---

## 4) Operational knowledge-pack + governance lifecycle (Phases 1-8)

This section encodes what has already been proven in the network. Codex must not re-architect away from it.

### 4.1 Role separation (authority boundaries)
- **Teacher** is the authoritative publisher of packs.
- **MinIO** is immutable artifact + evidence custody.
- **Core** adopts/mirrors verified pack versions.
- **Pack-service** serves packs read-only (immutable serving boundary).
- **Pins + Pin Resolver** control runtime selection (promotion/demotion) cryptographically.
- **Vector shards** are derived memory (rebuildable) and must remain consistent.
- **Critic** verifies drift/integrity without blocking operations.

### 4.2 Key technical baselines validated
- Vector DB topology: **two Qdrant shards**.
- Vector dimension baseline: **1536**.
- Distance metric baseline: **Cosine**.
- Collection name baseline: **cwi_kb_primary**.

### 4.3 Phase achievements (Codex must assume these are "done" and build on top)
**Phase 1 - Base infrastructure:** hardened Ubuntu + Docker/Compose, OPNsense boundary, Tailscale overlay, observability primitives, and ledger/evidence discipline.

**Phase 2 - Vector foundation:** both shards operational and symmetric (Qdrant v1.16.3).

**Phase 3 - Publication -> ingestion -> retrieval pipeline:** deterministic anchor generation, versioned publication (filesystem + MinIO), dual-shard ingestion, evidence capture with sha256 receipts, deterministic UUIDv5 point IDs.

**Phase 3.5 - Critic activated:** autonomous background verifier (pack sha256 verification + cross-shard drift sampling) scheduled every 10 minutes via systemd; least-privilege MinIO identity.

**Phase 4 - Operational pack boundary:** Core mirror adoption with anchor verification; `PACK_ADOPTED` receipts written to ledger; pack-service serves read-only `/packs/*`; pins mounted read-only in orchestrator; fail-closed 404 on invalid pack/version; gateway exposure validated.

**Phase 5 - Governance enforcement:** Teacher signs pins (ed25519); Core verifies; deterministic resolution priority `experiment -> approved -> latest_stable`; centralized pin resolver (`/resolve/<pack_id>`) becomes the only runtime path (orchestrator points `PACKS_BASE_URL` at resolver); all resolve events written to ledger; latency baselines captured.

**Phase 7 - Multi-region replication model:** simulated Region-B core stack runs alongside Region-A with isolated mounts and non-conflicting ports; determinism validated; controlled divergence + clean recovery validated; evidence bundles uploaded to Teacher MinIO + ledger.

**Phase 8 - Enterprise hardening:** governance enforced, deterministic pack integrity gates proven, multi-region resolver alignment proven, gateway network segmentation enforced (public gateway blocked from MinIO + Qdrant), audit-ready deployment validation produced.

---

## 5) Current deployment context (sanitized but concrete)

### 5.1 Current nodes (VPN/Tailnet IPs)
- `public-gateway-01` - `100.124.140.66`
- `internal-teacher-01` - `100.87.205.59`
- `internal-mcp-01` - `100.120.208.51`
- `vector-shard-1` - `100.115.118.16`
- `vector-shard-2` - `100.91.142.103`

### 5.2 Apps known "up" (inventory snapshot)
- gateway, router, orchestrator, identity, multiple web UIs, pack-service
- redis, postgres, vector-shard, graph-db
- model-worker-health
- teacher-node, teacher-minio
- teams bot (health may be warming)

> Codex should treat these as the current "shape" of a working environment. Cleanup and refactors must preserve readiness endpoints.

---

## 6) Component map + build order (how Codex should reason about dependencies)

### 6.1 Build order principle
Codex should not implement features "top-down" from UI first unless the contracts and runtime behaviors exist.
Preferred sequencing:
1) **Contracts** (OpenAPI + Schemas + fixtures)
2) **Runtime core behaviors** (orchestrator/router/pack-service)
3) **Gold tests + regression suites**
4) **UIs + clients**
5) **Ops/runbooks + ORR evidence**

### 6.2 Component "spine"
Minimum runtime spine for "system ready":
- gateway -> router -> orchestrator
- pack-service + pin-resolver (governed pack selection)
- postgres + redis
- vector shards (for grounded mode)
- model endpoints (Pro/free)
- ledger + kill-switch enforcement

### 6.3 Layering rules (applies to .NET services in particular)
- Enforce strict layers: **API -> Application -> Domain -> DataAccess**
- No direct access from API to DataAccess.
- Shared contracts live in a shared assembly/package; no copy/paste drift.
- No stubs/mocks in production runtime. Stubs are allowed only in test compose stacks.

---

## 7) Runtime flows Codex must preserve (gold-testable)

### 7.1 Pack publication -> adoption -> runtime resolution
1) Teacher canonicalizes `pack.json` (RFC 8785) -> computes sha256 anchor -> publishes versioned artifacts (filesystem + MinIO).
2) Core mirror pulls published version -> verifies anchor -> swaps live dir only if verified.
3) Core writes `PACK_ADOPTED` receipt to ledger.
4) Pack-service serves `/packs/index.json` and versioned pack paths read-only; invalid paths fail-closed (404).
5) Pins (signed) control promotion/demotion; Pin Resolver resolves pack_id -> effective hash deterministically.
6) Orchestrator reads packs only through Pin Resolver / governed boundary.

### 7.2 Ask flow (grounded response)
Client -> gateway `/api/ask` -> router -> orchestrator:
- resolves effective pack via resolver
- retrieves evidence from vector shards (and optional graph DB)
- calls model endpoint
- returns answer + trace (sources/confidence/policy/mode/model/pack_hash)
- appends ASK event to ledger

### 7.3 Action flow (approval-gated)
Client -> gateway `/api/action` -> router -> orchestrator:
- classify risk / apply policy
- if medium/high risk: return ApprovalTicket; do not execute
- upon approval: execute via connector/executor
- emit provenance + audit to ledger
- kill-switch can stop execution immediately

### 7.4 Telemetry ingest flow (industrial)
Edge-agent buffers locally -> posts telemetry windows to `/api/ingest/telemetry`
- ingest must be idempotent and deduplicated
- failures must not silently drop; buffer/retry required

---

## 8) Operational runbooks Codex must honor (do not break ingress / readiness)

### 8.1 Public gateway go-live (Cloudflare Tunnel)
- Cloudflare Tunnel is the only ingress (CGNAT compatible).
- Gateway routes:
  - `/` -> UI
  - `/api/*` -> router
  - `/packs/*` -> pack-service
  - `/jwks*` -> identity JWKS
  - `/healthz` and `/readyz` must remain stable

### 8.2 Cleanup discipline (stop -> verify -> remove)
When removing containers or refactoring compose:
- snapshot `docker inspect` for all running containers
- stop services first, verify `/readyz`, then remove
- never delete first

---

## 9) "All-content" document index (Codex should read these first)

This alignment spec is synthesized from the following project artifacts (all in `/mnt/data` in this environment):

### 9.1 Architecture / baseline / invariants
- `cwi-component-tech-stack-product-owner_v40.md` - authoritative component list, invariants, test gates
- `cwi-application-dependency-flow.md` - dependency graph + request flows (10H)
- `CWI_Mission_Goals_Roadmap_Architecture_Outline_2026-01-05.md` - mission, roadmap, 10H architecture outline, patent-alignment evidence plan
- `architecture-diagrams-for-patents.txt` - patent package diagrams (1-14)

### 9.2 Engineering + ops playbooks
- `CWI_Software_Engineering_Playbook_v2.docx` - build/run from zero, repo layout, test gates, deploy plan
- `cwi-software-engineering-playbook.docx` - additional build/run + repo conventions

### 9.3 Network / knowledge infrastructure phases (status + evidence language)
- `CWI Network Topology Features - Phase 1.txt`
- `CWI Network Topology Features - Phase 2.txt`
- `CWI Network Topology Features - Phase 3.txt`
- `CWI Network Topology Features - Phase 3.5.txt`
- `CWI Network Topology Features - Phase 4.txt`
- `CWI Network Topology Features - Phase 5.txt`
- `CWI Network Topology Features - Phase 7.txt`
- `CWI Network Topology Features - Phase 8.txt`

### 9.4 Current deployment inventory + placement notes
- `current-application-stack.md` - node/IP/service inventory
- `cwi-apps.txt` - "apps up" snapshot
- `CWI_Knowledge_Packs_Ingestion_Server_Placement_Findings.md` - targeted placement findings + open UNKNOWNs

### 9.5 Runbooks / operational procedures
- `CWI_Public_Gateway_GoLive_Runbook.md` - Cloudflare tunnel + DNS + UFW hardening
- `Demitrius_CWI_Phase1-9_Container_Cleanup_Guide.md` - safe cleanup plan and rollback discipline

### 9.6 Backlog / iteration planning artifacts
- `cwi-platform-backlog-generated-v33.csv` - full generated backlog (Application/DependsOn/ACs)
- `iteration-01.csv` ... `iteration-10.csv` - iteration scopes with ACs and status tracking
- `cwi-component-tech-stack-developer_v40.csv` - developer stack matrix (build order, tests, deps)

> Codex must treat the ACs in iteration/backlog CSVs as authoritative and implement to those criteria.

---

## 10) "Codex rules of engagement" (how Codex should respond to tasks)

### 10.1 When asked to implement a feature
Codex should:
1) Identify the component(s) involved and its dependencies.
2) Locate or create the contract(s) (OpenAPI / JSON Schema) + fixtures.
3) Implement the runtime behavior fail-closed with deterministic outputs.
4) Add/extend unit + integration tests.
5) Add/extend gold tests to prevent drift.
6) Update runbooks/ORR checklists if the change affects ops.
7) Provide a verification plan (curl scripts, compose smoke, evidence hashes).

### 10.2 When asked to change an invariant
Codex should not do it casually:
- Require an ADR and explicit sign-off path.
- Provide blast-radius analysis (clients, tests, ledger evidence).
- Provide backwards-compat plan (versioned contracts).

### 10.3 When asked to "just make it work"
Codex should avoid shortcuts that create future drift:
- No runtime mocks/stubs.
- No disabling verification ("temporary" bypass) in production paths.
- No changing pack bytes in transit.
- No loosening approval gates.

---

## 11) Known gaps / UNKNOWNs Codex must call out explicitly
These are explicitly flagged as unknown in current docs and must be verified before implementing assumptions:
- Which component performs document ETL indexing (teacher-node vs cwi-etl vs other).
- Where the provenance ledger is physically stored (Postgres vs separate service).
- Where the observability stack is hosted in the current inventory (Prometheus/Grafana not listed in current stack file).
- Where model-worker endpoints are deployed (not listed in current inventory table).

Codex must label any plan depending on these items as ASSUMPTION until verified.

---

## 12) Patent-alignment "provable parity" (engineering discipline, not legal advice)

CWI maintains an internal portfolio of 14 patent packages. Engineering alignment requires provable parity artifacts:
- frozen contracts
- fixtures
- deterministic gold tests
- crosswalk mapping patent -> components -> contracts/tests

Priority parity artifacts referenced in the roadmap:
- Patent #1: routing decision record + survival/governance/radio/task contracts
- Patent #4: ledger event schema + continuity + signature verification scaffolds
- Patent #6: sealed runtime bundle/activation + kill-switch evidence
- Patent #5: deterministic publish/rollback semantics + provenance records

Codex may be asked to implement these artifacts and must do so in a contracts-first, test-backed way.

---

## Appendix A - Quick checks Codex can always suggest (testable)

### A.1 Gateway readiness
```bash
curl -s https://<DOMAIN>/healthz
curl -s https://<DOMAIN>/readyz | head
curl -s https://<DOMAIN>/api/health | head
curl -s https://<DOMAIN>/packs/ | head
```

### A.2 Pack verification
```bash
curl -s https://<DOMAIN>/packs/<pack_id>/pack.json > pack.json
curl -s https://<DOMAIN>/packs/<pack_id>/anchor.txt > anchor.txt
python - <<'PY'
import hashlib, json, sys
from rfc8785 import canonicalize  # if available; otherwise use project tool
pack = json.load(open("pack.json","rb"))
canon = canonicalize(pack)
h = hashlib.sha256(canon).hexdigest()
print("computed:", h)
print("anchor  :", open("anchor.txt","r").read().strip())
PY
```

### A.3 Pin resolution
```bash
curl -s http://<PIN_RESOLVER_HOST>:8090/resolve/<pack_id> | jq
```

---

**End of alignment spec.**
