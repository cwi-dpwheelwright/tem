# CWI Platform Memory Reconciliation and Active Context Specification for Codex

**Document ID:** CWI-SPEC-MEM-PLATFORM-RECONCILIATION-002  
**Replaces / generalizes:** `CWI-SPEC-MEM-CHAT-HISTORY-RECONCILIATION-001`  
**Version:** 2.0  
**Date:** 2026-05-02  
**Audience:** Codex, CWI platform engineers, QA automation, architecture review  
**Status:** Draft for implementation  
**Classification:** Internal – Sanitized  

---

## 1. BLUF

The prior chat-history reconciliation spec was directionally correct but too narrow. The required behavior is not a **Chat History Upload** feature; it is a platform-wide **Memory Reconciliation Plane** for CWI.

CWI memory must behave as a governed, source-aware, provenance-preserving active context system. New context should not blindly append to old context, and it should not blindly overwrite old context. Instead, every relevant source update should reconcile against the existing memory state, decide whether it adds, duplicates, refines, supersedes, contradicts, retracts, or scopes prior memory, then update the **active memory view** used in normal answers while retaining historical lineage.

Recommended implementation name:

> **CWI Memory Reconciliation and Active Context Engine**

Recommended behavior name:

> **Source-aware belief revision with supersession, truth maintenance, and temporal active views**

Human analogy:

> Humans update memories through **memory reconsolidation**: remembered context can be reactivated, modified by newer information, and stabilized again. CWI should not imitate human memory unreliability, but the analogy is useful: the system retrieves related memory, updates it with newer evidence, and keeps the prior state available as history.

---

## 2. Scope correction

### 2.1 What changed from the chat-history-only spec

The previous spec treated Chat History Upload as the primary source. This version treats chat history as only one **source adapter** feeding a larger memory system.

Old mental model:

```text
Chat history upload V1 -> chat history upload V2 -> replace relevant chat-history facts
```

Correct platform mental model:

```text
Any governed source update -> normalize -> extract claims -> reconcile against memory slots
-> update active context -> retain provenance/history -> filter retrieval/respond with trace
```

### 2.2 Source types in scope

The memory engine must support at least these source families:

| Source family | Example | Memory role |
|---|---|---|
| `chat_history_upload` | Uploaded GPT/chat export | User conversation history, project state, preferences, prior decisions |
| `user_instruction` | Current user says “prefer Markdown specs” | High-priority user preference or task instruction |
| `operator_update` | Admin resolves conflict or marks a fact current | Explicit governance override |
| `knowledge_pack` | Published pack artifacts and pins | Governed product/domain capabilities and approved runtime context |
| `document_ingest` | Docs processed by ETL/connectors | Evidence-backed factual/project memory |
| `graph_projection` | AGE/GraphRAG entities and edges | Canonical entity relationships and derived structure |
| `vector_index` | Qdrant chunks/points | Retrieval substrate only, not source of truth |
| `connector_record` | Drive/Outlook/Smartsheet/etc. records | External workspace facts and artifacts |
| `telemetry_summary` | Edge/industrial time-window summary | Operational state and trend memory |
| `approval_ticket` | Approval state, decisions, execution result | Governance memory for actions |
| `provenance_event` | ASK/ACTION/TELEMETRY/PACK events | Audit trail and source lineage |
| `assistant_response` | Prior answer emitted by model | Not automatically authoritative; can become memory only if accepted/grounded |
| `memory_capsule` | User-bound portable memory bundle | Personal/enterprise scoped active memory package |

### 2.3 Non-goal correction

The memory plane must not become a hidden model-memory feature. It must be implemented in CWI-managed storage, retrieval, reconciliation, trace, and governance systems.

---

## 3. Problem framing

### 3.1 Goal

Implement a platform-wide memory system that can ingest, reconcile, and serve current active context from multiple evolving sources while preserving provenance, history, conflict state, and governance traceability.

### 3.2 Core question

When new context arrives from any source, what should happen to relevant or related existing memory?

The answer:

```text
Relevant old memory is not erased.
Relevant old memory is compared against the new source.
If the new source is better current evidence, old memory is superseded in the active view.
If the new source is additive, both facts remain active.
If the new source conflicts and no authority rule resolves it, the system opens a conflict.
If the new source retracts a fact, the old fact is retired but remains historically auditable.
```

### 3.3 Constraints

- Must preserve CWI provenance, trace, and audit expectations.
- Must not let stale vector chunks contaminate current answers.
- Must support complete snapshots, append-only updates, explicit patches, reindex-only changes, and runtime user preference changes.
- Must distinguish memory source authority from recency.
- Must support tenant/user/project scoping.
- Must support explainable “why did the answer change?” queries.
- Must support historical lookup without making historical facts appear current.
- Must fail closed for unsafe, unverified, or policy-conflicted memory mutations.
- Must be deterministic enough for gold tests.
- Must be sanitized in fixtures: no secrets, live endpoints, customer identifiers, or sensitive raw content.

### 3.4 Success criteria

A successful implementation satisfies all of the following:

1. **Active context changes when newer authoritative memory arrives.**
2. **Old context remains historical, not current.**
3. **Duplicate imports do not create duplicate active memory.**
4. **Partial imports do not falsely delete older memory.**
5. **Complete replacement snapshots can retire records that are truly absent.**
6. **Conflicts are labeled instead of guessed.**
7. **Source authority and scope are enforced.**
8. **Vector/graph retrieval obeys memory status filters.**
9. **Responses include memory trace fields when memory influenced the answer.**
10. **Operators can inspect lineage, conflicts, and reconciliation decisions.**
11. **Gold tests can prove V1-to-V2 answer changes and stale-context suppression.**

---

## 4. What this is called

### 4.1 Recommended CWI product/engineering term

Use this name in code, docs, tickets, and tests:

> **CWI Memory Reconciliation and Active Context Engine**

Use this phrase for the core behavior:

> **Source-aware belief revision with supersession and temporal truth maintenance**

### 4.2 Human/cognitive analogy

The closest human-memory analogy is:

> **Memory reconsolidation**

In practical terms: remembered context can be reactivated, updated by new information, and stabilized again. CWI should implement this deliberately and audibly, not biologically or implicitly.

### 4.3 AI / knowledge-base terms

| Term | Meaning for CWI |
|---|---|
| **Belief revision** | Updating the active set of accepted claims when new information arrives. |
| **Truth maintenance** | Maintaining consistency by tracking justifications and dependencies for claims. |
| **Reason maintenance** | Tracking why a claim is believed, which assumptions support it, and what derived facts depend on it. |
| **Knowledge-base revision** | Adding, replacing, retracting, and reconciling structured knowledge. |
| **Belief merging** | Combining claims from multiple sources with different authority/confidence. |

### 4.4 Data/platform terms

| Term | Meaning for CWI |
|---|---|
| **Event sourcing** | Store immutable source/memory events; derive current state from them. |
| **Temporal versioning** | Track `valid_from`, `valid_to`, and current/historical state. |
| **Materialized active view** | The computed current memory used by default response generation. |
| **Record reconciliation** | Decide whether incoming facts duplicate, update, conflict with, or extend existing facts. |
| **Entity resolution** | Decide whether two mentions refer to the same entity/user/project/service/component. |
| **Tombstone** | A non-deleting record that marks a fact as retracted/retired under retention rules. |
| **Source family** | A stream of related source versions, such as a pack, chat history export, connector object, or user preference profile. |

---

## 5. Core principles

### 5.1 Memory is an active view over immutable provenance

The immutable history answers:

```text
What did CWI receive, observe, emit, or decide?
```

The active view answers:

```text
What should CWI treat as current context right now?
```

Never collapse these into one table or one vector index.

### 5.2 Replacement means scoped supersession, not deletion

When memory changes, the old fact should usually become `SUPERSEDED`, `RETRACTED`, `STALE_SOURCE`, or `HISTORICAL`, not physically deleted.

Physical deletion is reserved for explicit retention/privacy purge workflows.

### 5.3 Source authority beats naive recency

A newer casual statement does not automatically override an older official contract. A newer explicit user preference may override an older user preference. A signed governance pin may override an unsigned local artifact. Source class matters.

### 5.4 Vectors are retrieval infrastructure, not truth

Vector records are derived from source records. They may help find evidence, but they do not decide which fact is current.

### 5.5 The model does not own memory

Model outputs are not automatically memory. A model answer can become a memory source only if the system records it as grounded, accepted, or operator/user-confirmed under policy.

### 5.6 Memory needs scoped slots

A fact can replace another fact only if it occupies the same memory slot.

A memory slot is:

```text
tenant + user/project/capsule + subject + predicate + scope + cardinality + effective time
```

Examples:

```text
tenant:cwi / project:cwi-platform / subject:cwi.phase.7 / predicate:status / scope:network-topology / cardinality:single
tenant:cwi / user:<hash> / subject:user.output_preference / predicate:artifact_type / scope:codex_specs / cardinality:multi
tenant:cwi / project:cwi-platform / subject:fixture-pack / predicate:active_hash / scope:region-a / cardinality:single
```

### 5.7 Historical context is first-class

The system must support questions like:

- “What changed?”
- “Why did your answer change?”
- “What did the prior upload say?”
- “Which source superseded that?”
- “Show the lineage of this memory claim.”

### 5.8 Conflict honesty is mandatory

If memory sources conflict and the reconciliation engine cannot safely choose a winner, the answer should say the memory is conflicted.

### 5.9 Memory mutation is governed

Memory changes that affect actions, governance, identity, ownership, safety, or external systems may require approval, provenance logging, or quarantine.

### 5.10 Fail closed on unsafe ambiguity

If a memory update would cause an unsafe action, sensitive disclosure, cross-tenant leak, or governance bypass, the system must reject or quarantine the update until reviewed.

---

## 6. Memory architecture

### 6.1 Layered model

```text
L0 Raw source artifacts
L1 Normalized source records
L2 Extracted candidate claims
L3 Entity and slot resolution
L4 Reconciliation decisions and justifications
L5 Active memory view
L6 Derived retrieval indexes: vector + graph
L7 Context assembly for ASK/ACTION
L8 Response trace and provenance
```

### 6.2 L0 — Raw source artifacts

Stores immutable inputs and source metadata:

- uploaded files,
- pack artifacts,
- connector payloads,
- telemetry windows,
- operator updates,
- user preference events,
- assistant response receipts,
- provenance ledger events,
- memory capsule imports.

Required properties:

```text
source_id
source_family_id
source_type
source_version
canonical_sha256
raw_sha256
tenant_id
user_id_hash or project_id
imported_at
source_authority_class
retention_class
sensitivity_label
status
```

### 6.3 L1 — Normalized source records

Break raw artifacts into stable records:

- chat messages,
- document chunks,
- pack manifests,
- pin records,
- graph edges,
- connector objects,
- telemetry summary windows,
- approval decisions,
- user preference statements.

This layer supports deterministic diffing.

### 6.4 L2 — Extracted candidate claims

A candidate claim is a structured assertion extracted from normalized records.

Example:

```json
{
  "subject_key": "cwi.phase.7",
  "predicate_key": "status",
  "scope_key": "network-topology",
  "cardinality": "single",
  "value_json": { "status": "complete" },
  "evidence_refs": ["source_record:<id>"],
  "extraction_confidence": 0.96
}
```

### 6.5 L3 — Entity and slot resolution

The engine decides whether candidate claims refer to existing entities and slots.

Examples:

| Incoming mention | Canonical entity |
|---|---|
| `Phase Seven` | `cwi.phase.7` |
| `Region B router` | `cwi.region-b.router` |
| `fixture pack` | `cwi.pack.fixture-pack` |
| `ChatGPT history export` | `source_family.chat_history_upload.<hash>` |
| `my preference for specs` | `user.<hash>.preference.spec_output` |

### 6.6 L4 — Reconciliation decisions and justifications

Each incoming claim receives a decision:

```text
ADD
DUPLICATE
SUPPORT
REFINE
SUPERSEDE
CONTRADICT
RETRACT
SPLIT
MERGE
QUARANTINE
IGNORE_LOW_CONFIDENCE
```

Each decision must have:

```text
decision_id
decision_type
old_claim_id(s)
new_claim_id
source_import_id
reason_code
confidence
authority_score
recency_score
scope_score
policy_result
ledger_event_id
```

### 6.7 L5 — Active memory view

The active memory view is the default state used by response generation.

It includes:

```text
ACTIVE
ACTIVE_SCOPED
ACTIVE_WITH_CONFLICT_NOTE
ACTIVE_LOW_CONFIDENCE_ALLOWED_BY_POLICY
```

It excludes by default:

```text
SUPERSEDED
RETRACTED
DUPLICATE
STALE_SOURCE
CONFLICTED_UNRESOLVED
QUARANTINED
PURGED
```

### 6.8 L6 — Derived retrieval indexes

These indexes support search but do not decide truth:

- Qdrant/vector shards,
- GraphDB/AGE graph projections,
- materialized search tables,
- cache layers.

They must carry payload fields that allow filtering by:

```text
tenant_id
user_id_hash/project_id
source_id
source_family_id
source_version
claim_id
claim_status
record_status
sensitivity_label
retention_state
```

### 6.9 L7 — Context assembly

Before calling the model, the context builder must:

1. determine intent: current, historical, debug, action, policy, preference, source-specific;
2. retrieve active claims first;
3. retrieve supporting evidence only from allowed source/status classes;
4. exclude stale/superseded chunks unless historical intent is explicit;
5. include memory trace metadata;
6. state unresolved conflicts.

### 6.10 L8 — Response trace and provenance

Every memory-influenced answer should include trace fields showing:

```json
{
  "trace": {
    "memory": {
      "active_claim_ids": [],
      "source_ids": [],
      "source_family_ids": [],
      "source_versions": [],
      "reconciliation_decision_ids": [],
      "conflict_group_ids": [],
      "historical_claim_ids": [],
      "memory_policy_mode": "active_current",
      "memory_engine_version": "2.0"
    }
  }
}
```

---

## 7. Source authority model

### 7.1 Authority is contextual

A source can be authoritative for one domain and weak for another.

Examples:

| Source | Strong authority over | Weak authority over |
|---|---|---|
| Signed pack pin | active pack hash/version | user personal preference |
| User current instruction | personal preference, current task style | official CWI infrastructure facts |
| Official CWI playbook | architecture and invariants | today’s transient runtime health |
| Telemetry window | recent observed operational metric | product roadmap decision |
| Chat history | prior conversation context | official contract if contradicted by signed spec |
| Assistant response | prior output history | factual truth unless grounded/accepted |
| Operator update | conflict resolution, admin override | anything outside operator role scope |

### 7.2 Default authority priority

Default priority should be configurable, but P0 can start with:

```text
1. Legal/privacy/retention deletion directive
2. Safety/security/governance policy
3. Signed governance artifact / pack pin / approved contract
4. Operator/admin explicit resolution within role scope
5. Official CWI docs / schemas / gold-test fixtures
6. Current user instruction for that user/session/task
7. Connector/document source with stable provenance
8. Chat history upload / historical conversation export
9. Telemetry summary, scoped to time window
10. Prior assistant answer, if grounded and accepted
11. Model prior / ungrounded inference
```

### 7.3 Recency score

Recency only applies after source family, scope, and authority are considered.

Recommended recency fields:

```text
event_time              # when the underlying event occurred, if known
source_updated_at        # when the source says it changed
observed_at              # when CWI observed it
imported_at              # when CWI imported it
effective_from           # when CWI should treat it as valid
effective_to             # when it stops being valid
```

Do not treat `imported_at` as real-world event time unless no better timestamp exists and the trace says so.

---

## 8. Memory slots and replacement semantics

### 8.1 Slot definition

A memory slot is the replacement boundary:

```text
tenant_id
memory_owner_scope       # user / project / org / tenant / region / node / capsule
subject_key
predicate_key
scope_key
cardinality
effective_interval
```

### 8.2 Cardinality rules

| Cardinality | Meaning | Replacement behavior |
|---|---|---|
| `single` | Only one active value allowed in scope | New winning same-slot claim supersedes old |
| `multi` | Multiple active values can coexist | New claim adds unless explicitly retracting/refining |
| `temporal_series` | Values change over time | Add time-bounded observation; current depends on interval |
| `ranked` | Ordered preferences | Update rank list using explicit preference logic |
| `set_with_tombstones` | Collection with removals | Add/remove through tombstones, preserve history |
| `derived` | Computed from other claims | Recompute when dependencies change |

### 8.3 Same-slot replacement

If incoming claim has same slot and different value:

```text
if new claim wins:
  old ACTIVE -> SUPERSEDED
  new -> ACTIVE
  edge new SUPERSEDES old
elif old claim wins:
  new -> QUARANTINED or CONFLICTED_UNRESOLVED
else:
  conflict group opens
```

### 8.4 Additive extension

If incoming claim is related but not same-slot:

```text
old remains ACTIVE
new becomes ACTIVE
edge SUPPORTS/REFINES may be added
```

### 8.5 Explicit correction

If source says:

```text
Correction:
Update:
Replace X with Y:
Ignore the previous:
The earlier statement was wrong:
```

Then apply a correction boost, subject to authority and policy.

### 8.6 Absence handling

Absence is not deletion unless the source mode proves it.

| Import mode | Missing old facts |
|---|---|
| `append_only` | Preserve |
| `complete_snapshot_replace_family` | Mark missing source records stale; retire claims only when coverage is proven |
| `patch` | Retire only with explicit delete/retract operation |
| `reindex_only` | Preserve |
| `operator_retract` | Retire scoped claim |
| `retention_purge` | Tombstone or purge according to policy |

### 8.7 Historical retention

A superseded claim remains available to historical/debug asks:

```text
Current: 10H
Previous: 10G
Reason: newer same-slot official baseline claim superseded prior baseline claim
```

---

## 9. Global source modes

### 9.1 `append_only`

For partial additions.

Examples:

- new chat messages,
- new document,
- new telemetry summary,
- new approval event.

Behavior:

```text
Add new records.
Deduplicate exact repeats.
Do not retire absent prior facts.
Reconcile same-slot claims only if explicitly current/corrective.
```

### 9.2 `complete_snapshot_replace_family`

For complete updated snapshot of same source family.

Examples:

- full updated chat export,
- full connector object sync,
- full pack index,
- complete preference profile export.

Behavior:

```text
Compare V2 to V1.
Mark absent source records STALE_SOURCE if coverage is proven.
Retire claims whose only evidence was removed and covered.
Add/update/supersede changed claims.
Preserve history.
```

### 9.3 `patch`

For explicit updates.

Examples:

- JSON Patch,
- admin conflict resolution,
- preference update,
- deletion directive.

Behavior:

```text
Validate base hash/preconditions.
Apply patch.
Reject if base mismatch unless force-approved.
Ledger all changes.
```

### 9.4 `reindex_only`

For changed embeddings, graph projection, parser version, or retrieval metadata without source content changes.

Behavior:

```text
Do not change source truth.
May change derived indexes.
May update extracted claims only if extraction version changes and reconciliation passes.
```

### 9.5 `policy_override`

For governance/security/admin decisions.

Behavior:

```text
Apply only inside role scope.
Require provenance.
May require approval ticket.
Can force conflict resolution or quarantine.
```

### 9.6 `retention_purge`

For privacy/legal deletion.

Behavior:

```text
Apply retention policy.
Tombstone or physically purge according to policy.
Invalidate derived indexes.
Write audit event without exposing purged content.
```

---

## 10. Claim status model

### 10.1 Statuses

| Status | Default current answer usage | Meaning |
|---|---:|---|
| `ACTIVE` | Yes | Current accepted claim |
| `ACTIVE_SCOPED` | Yes, with scope | Current only inside a bounded scope |
| `ACTIVE_WITH_CONFLICT_NOTE` | Yes, with warning | Current best claim, but conflict exists |
| `LOW_CONFIDENCE` | Usually no | Candidate exists but not trusted enough |
| `DUPLICATE` | No | Same as another claim |
| `SUPERSEDED` | No | Replaced by newer/better claim |
| `RETRACTED` | No | Explicitly withdrawn |
| `STALE_SOURCE` | No | Source no longer current |
| `CONFLICTED_UNRESOLVED` | No default | No safe winner |
| `QUARANTINED` | No | Policy/validation failed |
| `HISTORICAL` | No default | Valid only as history |
| `TOMBSTONED` | No | Retired under retention or delete semantics |
| `PURGED` | No | Content removed; metadata only if allowed |

### 10.2 Status transitions

```text
LOW_CONFIDENCE -> ACTIVE                         if confirmed
ACTIVE -> SUPERSEDED                             if winning same-slot claim arrives
ACTIVE -> RETRACTED                              if explicit retraction arrives
ACTIVE -> CONFLICTED_UNRESOLVED                  if unresolved contradiction appears
CONFLICTED_UNRESOLVED -> ACTIVE                  if conflict resolved
SUPERSEDED -> ACTIVE                             if later source restores value
ACTIVE -> STALE_SOURCE                           if complete snapshot removes only evidence
ACTIVE -> TOMBSTONED/PURGED                      if retention/deletion policy requires
```

---

## 11. Reconciliation algorithm

### 11.1 High-level pipeline

```text
1. Receive memory source update.
2. Authenticate/authorize source.
3. Canonicalize and hash.
4. Resolve source family.
5. Determine import/update mode.
6. Parse and normalize source records.
7. Diff against prior source versions.
8. Extract candidate claims.
9. Resolve entities and slots.
10. Score authority, recency, confidence, scope, sensitivity, and policy.
11. Decide add/duplicate/refine/supersede/conflict/retract/quarantine.
12. Update immutable claim/edge/decision history.
13. Refresh active memory view.
14. Update derived vector/graph indexes.
15. Emit provenance and ledger events.
16. Return import/reconciliation summary.
```

### 11.2 Pseudocode

```python
def ingest_memory_source(source_payload, actor, requested_mode=None):
    authorize_memory_import(actor, source_payload)

    canonical_bytes = canonicalize_source(source_payload)
    canonical_sha = sha256(canonical_bytes)

    duplicate = find_source_version_by_hash(
        tenant_id=actor.tenant_id,
        owner_scope=source_payload.owner_scope,
        canonical_sha=canonical_sha
    )
    if duplicate:
        return MemoryImportResult(
            status="NOOP_DUPLICATE",
            source_version_id=duplicate.source_version_id
        )

    source_family = resolve_source_family(source_payload, actor)
    prior_version = latest_successful_source_version(source_family)
    mode = choose_update_mode(source_payload, prior_version, requested_mode)

    source_version = create_source_version(
        source_family=source_family,
        mode=mode,
        canonical_sha=canonical_sha
    )

    normalized_records = parse_and_normalize(source_payload)
    record_delta = diff_records(normalized_records, prior_version, mode)

    candidate_claims = extract_candidate_claims(normalized_records)
    decisions = []

    with transaction():
        store_records(source_version, normalized_records, record_delta)

        for candidate in candidate_claims:
            entity = resolve_entity(candidate)
            slot = resolve_memory_slot(candidate, entity)
            existing_claims = find_claims_for_slot(slot)

            decision = reconcile_candidate_claim(
                candidate=candidate,
                existing_claims=existing_claims,
                source_version=source_version,
                mode=mode,
                actor=actor
            )

            apply_reconciliation_decision(decision)
            decisions.append(decision)

        if mode == "complete_snapshot_replace_family":
            retire_missing_claims_if_coverage_proven(
                source_version=source_version,
                prior_version=prior_version,
                record_delta=record_delta
            )

        refresh_active_memory_view(source_family.owner_scope)

        emit_memory_ledger_events(source_version, decisions)

    update_derived_indexes(source_version, decisions)

    return build_memory_import_summary(source_version, record_delta, decisions)
```

### 11.3 Decision scoring

Recommended score components:

```text
source_authority_score
explicit_correction_score
slot_match_score
scope_match_score
entity_match_score
recency_score
evidence_quality_score
extraction_confidence
policy_allow_score
sensitivity_risk_score
```

Example weighted decision:

```text
winner_score =
  0.30 * source_authority_score +
  0.20 * explicit_correction_score +
  0.15 * slot_match_score +
  0.10 * evidence_quality_score +
  0.10 * recency_score +
  0.10 * extraction_confidence +
  0.05 * scope_match_score
```

Do not hard-code these weights permanently. Store them in policy/config and record the version used.

### 11.4 Conflict detection

Open a conflict group when:

```text
same slot
different normalized value
similar effective interval
both above confidence threshold
no source authority rule picks a clear winner
```

Conflict group fields:

```text
conflict_group_id
slot_key
claim_ids
opened_by_decision_id
status
severity
recommended_resolution
created_at
resolved_at
resolved_by_actor
```

### 11.5 Derived claim invalidation

When an active base claim changes, derived claims must be recomputed or invalidated.

Example:

```text
Base claim: Phase 7 status = complete
Derived claim: Platform is multi-region-ready
```

If the base claim changes, the derived claim must be reviewed or recalculated.

---

## 12. Data model proposal

### 12.1 `memory_source_families`

```sql
CREATE TABLE memory_source_families (
  source_family_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  source_type TEXT NOT NULL,
  canonical_family_key TEXT NOT NULL,
  authority_class TEXT NOT NULL,
  default_update_mode TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'ACTIVE',
  metadata_json JSONB NOT NULL DEFAULT '{}',
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);

CREATE UNIQUE INDEX idx_memory_source_family_key
  ON memory_source_families (tenant_id, owner_scope_type, owner_scope_id, source_type, canonical_family_key);
```

### 12.2 `memory_source_versions`

```sql
CREATE TABLE memory_source_versions (
  source_version_id UUID PRIMARY KEY,
  source_family_id UUID NOT NULL REFERENCES memory_source_families(source_family_id),
  tenant_id TEXT NOT NULL,
  imported_by_actor_id TEXT NULL,
  source_version_label TEXT NULL,
  update_mode TEXT NOT NULL,
  raw_sha256 TEXT NOT NULL,
  canonical_sha256 TEXT NOT NULL,
  parser_version TEXT NOT NULL,
  extractor_version TEXT NOT NULL,
  imported_at TIMESTAMPTZ NOT NULL,
  source_observed_at TIMESTAMPTZ NULL,
  prior_source_version_id UUID NULL,
  status TEXT NOT NULL,
  status_reason TEXT NULL,
  sensitivity_label TEXT NOT NULL DEFAULT 'internal',
  retention_class TEXT NOT NULL DEFAULT 'standard',
  metadata_json JSONB NOT NULL DEFAULT '{}'
);

CREATE UNIQUE INDEX idx_memory_source_version_hash
  ON memory_source_versions (tenant_id, canonical_sha256);
```

### 12.3 `memory_source_records`

```sql
CREATE TABLE memory_source_records (
  source_record_id UUID PRIMARY KEY,
  source_version_id UUID NOT NULL REFERENCES memory_source_versions(source_version_id),
  source_family_id UUID NOT NULL REFERENCES memory_source_families(source_family_id),
  tenant_id TEXT NOT NULL,
  record_type TEXT NOT NULL,
  external_record_id TEXT NULL,
  record_fingerprint TEXT NOT NULL,
  content_sha256 TEXT NOT NULL,
  normalized_text TEXT NULL,
  value_json JSONB NULL,
  event_time TIMESTAMPTZ NULL,
  status TEXT NOT NULL DEFAULT 'ACTIVE_IN_SOURCE',
  sensitivity_label TEXT NOT NULL DEFAULT 'internal',
  metadata_json JSONB NOT NULL DEFAULT '{}',
  created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_memory_record_family_fingerprint
  ON memory_source_records (tenant_id, source_family_id, record_fingerprint);
```

### 12.4 `memory_entities`

```sql
CREATE TABLE memory_entities (
  entity_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  entity_key TEXT NOT NULL,
  entity_type TEXT NOT NULL,
  display_name TEXT NULL,
  aliases_json JSONB NOT NULL DEFAULT '[]',
  confidence NUMERIC(5,4) NOT NULL DEFAULT 1.0000,
  status TEXT NOT NULL DEFAULT 'ACTIVE',
  metadata_json JSONB NOT NULL DEFAULT '{}',
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);

CREATE UNIQUE INDEX idx_memory_entity_key
  ON memory_entities (tenant_id, owner_scope_type, owner_scope_id, entity_key);
```

### 12.5 `memory_claims`

```sql
CREATE TABLE memory_claims (
  claim_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  source_family_id UUID NOT NULL REFERENCES memory_source_families(source_family_id),
  created_source_version_id UUID NOT NULL REFERENCES memory_source_versions(source_version_id),
  entity_id UUID NULL REFERENCES memory_entities(entity_id),
  subject_key TEXT NOT NULL,
  predicate_key TEXT NOT NULL,
  scope_key TEXT NOT NULL DEFAULT 'global',
  slot_key TEXT NOT NULL,
  cardinality TEXT NOT NULL DEFAULT 'single',
  value_json JSONB NOT NULL,
  normalized_value TEXT NOT NULL,
  status TEXT NOT NULL,
  confidence NUMERIC(5,4) NOT NULL,
  source_authority_score NUMERIC(5,4) NOT NULL,
  extraction_confidence NUMERIC(5,4) NOT NULL,
  effective_from TIMESTAMPTZ NULL,
  effective_to TIMESTAMPTZ NULL,
  superseded_by_claim_id UUID NULL,
  conflict_group_id UUID NULL,
  evidence_json JSONB NOT NULL,
  sensitivity_label TEXT NOT NULL DEFAULT 'internal',
  retention_class TEXT NOT NULL DEFAULT 'standard',
  metadata_json JSONB NOT NULL DEFAULT '{}',
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_memory_claims_active_slot
  ON memory_claims (tenant_id, owner_scope_type, owner_scope_id, slot_key, status);

CREATE INDEX idx_memory_claims_subject_predicate
  ON memory_claims (tenant_id, subject_key, predicate_key, status);
```

### 12.6 `memory_claim_edges`

```sql
CREATE TABLE memory_claim_edges (
  edge_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  from_claim_id UUID NOT NULL REFERENCES memory_claims(claim_id),
  to_claim_id UUID NOT NULL REFERENCES memory_claims(claim_id),
  relationship TEXT NOT NULL,
  reason_code TEXT NOT NULL,
  confidence NUMERIC(5,4) NOT NULL,
  source_version_id UUID NOT NULL REFERENCES memory_source_versions(source_version_id),
  created_at TIMESTAMPTZ NOT NULL
);
```

Allowed relationships:

```text
DUPLICATES
SUPPORTS
REFINES
SUPERSEDES
CONTRADICTS
RETRACTS
SPLITS_FROM
MERGES_INTO
DERIVED_FROM
DEPENDS_ON
INVALIDATES
```

### 12.7 `memory_reconciliation_decisions`

```sql
CREATE TABLE memory_reconciliation_decisions (
  decision_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  source_version_id UUID NOT NULL REFERENCES memory_source_versions(source_version_id),
  decision_type TEXT NOT NULL,
  slot_key TEXT NULL,
  candidate_claim_json JSONB NOT NULL,
  old_claim_ids JSONB NOT NULL DEFAULT '[]',
  new_claim_id UUID NULL REFERENCES memory_claims(claim_id),
  conflict_group_id UUID NULL,
  reason_code TEXT NOT NULL,
  score_json JSONB NOT NULL,
  policy_result_json JSONB NOT NULL,
  ledger_event_id TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL
);
```

### 12.8 `memory_active_view`

```sql
CREATE TABLE memory_active_view (
  active_view_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  slot_key TEXT NOT NULL,
  claim_id UUID NOT NULL REFERENCES memory_claims(claim_id),
  value_json JSONB NOT NULL,
  normalized_value TEXT NOT NULL,
  status TEXT NOT NULL,
  confidence NUMERIC(5,4) NOT NULL,
  source_family_id UUID NOT NULL,
  source_version_id UUID NOT NULL,
  effective_from TIMESTAMPTZ NULL,
  effective_to TIMESTAMPTZ NULL,
  refreshed_at TIMESTAMPTZ NOT NULL,
  memory_engine_version TEXT NOT NULL,
  metadata_json JSONB NOT NULL DEFAULT '{}'
);

CREATE UNIQUE INDEX idx_memory_active_view_slot
  ON memory_active_view (tenant_id, owner_scope_type, owner_scope_id, slot_key);
```

### 12.9 `memory_conflict_groups`

```sql
CREATE TABLE memory_conflict_groups (
  conflict_group_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  slot_key TEXT NOT NULL,
  status TEXT NOT NULL,
  severity TEXT NOT NULL,
  claim_ids JSONB NOT NULL,
  opened_by_decision_id UUID NULL,
  resolved_by_decision_id UUID NULL,
  resolution_reason TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  resolved_at TIMESTAMPTZ NULL
);
```

### 12.10 `memory_capsules`

```sql
CREATE TABLE memory_capsules (
  capsule_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  owner_scope_type TEXT NOT NULL,
  owner_scope_id TEXT NOT NULL,
  capsule_type TEXT NOT NULL,
  capsule_version TEXT NOT NULL,
  capsule_hash TEXT NOT NULL,
  status TEXT NOT NULL,
  encryption_state TEXT NOT NULL,
  portability_policy TEXT NOT NULL,
  retention_policy TEXT NOT NULL,
  metadata_json JSONB NOT NULL DEFAULT '{}',
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);
```

### 12.11 `memory_vector_bindings`

```sql
CREATE TABLE memory_vector_bindings (
  binding_id UUID PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  vector_collection TEXT NOT NULL,
  vector_point_id TEXT NOT NULL,
  source_record_id UUID NULL REFERENCES memory_source_records(source_record_id),
  claim_id UUID NULL REFERENCES memory_claims(claim_id),
  source_version_id UUID NOT NULL REFERENCES memory_source_versions(source_version_id),
  claim_status_at_index_time TEXT NULL,
  payload_sha256 TEXT NOT NULL,
  embedding_model TEXT NOT NULL,
  embedding_dimension INTEGER NOT NULL,
  indexed_at TIMESTAMPTZ NOT NULL,
  status TEXT NOT NULL DEFAULT 'ACTIVE_INDEX'
);
```

---

## 13. API contract proposal

### 13.1 Generic memory import

```http
POST /api/memory/import
```

Request:

```json
{
  "source_type": "chat_history_upload",
  "source_family_id": "optional-stable-family-id",
  "owner_scope": {
    "type": "project",
    "id": "cwi-platform"
  },
  "update_mode": "complete_snapshot_replace_family",
  "content_type": "text/markdown",
  "filename": "cwi_chat_history_v2.md",
  "content_base64": "<base64>",
  "metadata": {
    "snapshot_type": "complete",
    "test_run_id": "smoke-001"
  }
}
```

Response:

```json
{
  "source_version_id": "<uuid>",
  "source_family_id": "<uuid>",
  "status": "COMPLETED",
  "canonical_sha256": "<sha256>",
  "prior_source_version_id": "<uuid-or-null>",
  "summary": {
    "records_added": 12,
    "records_unchanged": 90,
    "records_stale": 5,
    "claims_added": 7,
    "claims_superseded": 3,
    "claims_conflicted": 1,
    "claims_retracted": 0,
    "active_view_updates": 6
  },
  "trace_id": "<trace-id>"
}
```

### 13.2 Memory source diff

```http
GET /api/memory/sources/{source_version_id}/diff
```

Response:

```json
{
  "source_version_id": "<uuid>",
  "prior_source_version_id": "<uuid>",
  "deltas": [
    {
      "delta_type": "SUPERSEDED",
      "slot_key": "project:cwi.phase.7.status:network-topology",
      "before": "starting",
      "after": "complete",
      "old_claim_id": "<uuid>",
      "new_claim_id": "<uuid>",
      "reason_code": "same_slot_newer_authoritative_update"
    }
  ]
}
```

### 13.3 Active memory query

```http
GET /api/memory/active?owner_scope_type=project&owner_scope_id=cwi-platform
```

Response:

```json
{
  "claims": [
    {
      "claim_id": "<uuid>",
      "slot_key": "project:cwi.phase.7.status:network-topology",
      "subject_key": "cwi.phase.7",
      "predicate_key": "status",
      "scope_key": "network-topology",
      "value": "complete",
      "confidence": 0.98,
      "source_version_id": "<uuid>",
      "source_family_id": "<uuid>"
    }
  ]
}
```

### 13.4 Historical lineage query

```http
GET /api/memory/claims/{claim_id}/history
```

Response:

```json
{
  "claim_id": "<uuid>",
  "lineage": [
    {
      "claim_id": "<uuid-old>",
      "status": "SUPERSEDED",
      "value": "starting",
      "source_version_id": "<source-v1>",
      "valid_to": "<timestamp>"
    },
    {
      "claim_id": "<uuid-new>",
      "status": "ACTIVE",
      "value": "complete",
      "source_version_id": "<source-v2>",
      "valid_from": "<timestamp>"
    }
  ]
}
```

### 13.5 Conflict resolution

```http
POST /api/memory/conflicts/{conflict_group_id}/resolve
```

Request:

```json
{
  "resolution": "select_claim",
  "winning_claim_id": "<uuid>",
  "reason": "operator verified against approved architecture spec",
  "metadata": {
    "approval_ticket_id": "<optional-ticket>"
  }
}
```

### 13.6 Reindex

```http
POST /api/memory/reindex
```

Request:

```json
{
  "owner_scope": {
    "type": "project",
    "id": "cwi-platform"
  },
  "source_family_id": "<optional>",
  "index_targets": ["vector", "graph"],
  "reason": "embedding model update",
  "update_mode": "reindex_only"
}
```

---

## 14. Retrieval and response behavior

### 14.1 Intent classes

The response engine must classify memory intent:

| Intent | Use active memory? | Use historical memory? | Notes |
|---|---:|---:|---|
| `current_state` | Yes | No default | Normal answer path |
| `historical_compare` | Yes | Yes | “What changed?” |
| `debug_trace` | Yes | Yes | Include decisions/lineage |
| `source_specific` | Scoped | Scoped | “Only use upload V1” |
| `preference_update` | Yes | Prior only for conflict | Mutates preference memory |
| `action` | Yes | Only if relevant | Must enforce policy |
| `retention_delete` | N/A | N/A | Governed purge/tombstone flow |

### 14.2 Current-state answer rules

For normal questions:

```text
Use active claims first.
Use only current/allowed evidence records.
Exclude superseded/stale vector chunks.
Do not mention old facts unless relevant to explain conflict.
Include memory trace metadata.
```

### 14.3 Historical answer rules

For historical questions:

```text
Include old and new facts.
Label statuses.
Name source families/versions.
Explain why active answer changed.
Do not present old facts as current.
```

### 14.4 Conflict answer rules

If conflict unresolved:

```text
State the conflict.
List competing claims if allowed.
Do not fabricate a winner.
Ask for or route to conflict resolution if needed.
```

### 14.5 Action answer rules

If memory affects an ACTION:

```text
Use only active claims.
If active memory is conflicted or low confidence, require approval or deny.
Append provenance.
Respect kill-switch and policy.
```

---

## 15. Preventing stale vector and graph contamination

### 15.1 Required vector payload fields

Every vector point must include:

```json
{
  "tenant_id": "<tenant>",
  "owner_scope_type": "project",
  "owner_scope_id": "cwi-platform",
  "source_family_id": "<uuid>",
  "source_version_id": "<uuid>",
  "source_record_id": "<uuid>",
  "claim_ids": ["<uuid>"],
  "claim_statuses": ["ACTIVE"],
  "record_status": "ACTIVE_IN_SOURCE",
  "sensitivity_label": "internal",
  "retention_state": "standard"
}
```

### 15.2 Retrieval filter

Default current-state retrieval must apply:

```text
claim_status IN ('ACTIVE', 'ACTIVE_SCOPED', 'ACTIVE_WITH_CONFLICT_NOTE')
record_status NOT IN ('STALE_SOURCE', 'RETRACTED', 'PURGED')
source_version.status IN ('ACTIVE', 'CURRENT', 'APPROVED')
intent != historical
```

### 15.3 Historical retrieval exception

Historical intent may include stale/superseded records only if labeled:

```json
{
  "historical_context": true,
  "claim_status": "SUPERSEDED",
  "current_replacement_claim_id": "<uuid>"
}
```

### 15.4 Graph projection invalidation

Graph edges derived from superseded claims must be:

```text
recomputed,
marked stale,
or excluded by active graph query filters.
```

---

## 16. Memory capsules

### 16.1 Definition

A memory capsule is a scoped, portable, governed memory package for a user, project, tenant, or node.

It may include:

- active memory claims,
- historical lineage,
- preferences,
- source references,
- sensitivity labels,
- encryption metadata,
- retention policy,
- portability policy,
- capsule hash.

### 16.2 Why capsules matter

The platform roadmap includes portable intelligence and user-bound memory. Capsules provide a way to move memory without relying on hidden model state.

### 16.3 Capsule rules

```text
Capsules must be encrypted or explicitly marked plaintext for test fixtures.
Capsules must be bound to identity/tenant scope.
Capsules must preserve provenance.
Capsule import must reconcile like any other source.
Capsule export must respect retention/deletion policies.
Capsule activation must not bypass source authority or governance.
```

### 16.4 Capsule import behavior

```text
memory_capsule import -> source family -> source version -> normalized capsule records
-> candidate claims -> reconciliation -> active view update
```

### 16.5 Capsule portability

Portability should preserve:

```text
claim IDs or stable claim fingerprints
source family references
supersession lineage
conflict groups
sensitivity labels
retention state
capsule signature/hash
```

---

## 17. Integration with CWI runtime

### 17.1 Direct ask path

Memory should be integrated behind the orchestrator/context assembly boundary:

```text
User -> cwi-gpt-web-ui -> cwi-gateway -> cwi-router -> cwi-orchestrator
-> memory/evidence/model systems -> response
```

### 17.2 Component responsibilities

| Component | Responsibility |
|---|---|
| `cwi-gpt-web-ui` | Shows answers, trace, memory conflict warnings, source labels |
| `cwi-gateway` | Routes `/api/*` and `/packs/*`; no memory decisions |
| `cwi-router` | Auth/rate/tier routing; may pass memory scope headers |
| `cwi-orchestrator` | Owns memory query, reconciliation calls, context assembly, trace |
| `cwi-pack-service` | Serves immutable pack artifacts; source family for pack memory |
| `teacher-node` | Publishes/validates packs and artifacts; source of governed pack truth |
| `cwi-etl` / `cwi-consent-ingest` | Normalizes/chunks/feeds document and graph memory |
| `graphdb` | Derived relationship/retrieval memory |
| `vector-shard-1/2` | Derived semantic retrieval memory |
| `provenance ledger` | Immutable memory mutation and response audit |
| `cwi-gold-tests` | Release gate for memory behavior |

### 17.3 Orchestrator context builder contract

The orchestrator should call:

```text
MemoryContextService.GetContext(request, intent, owner_scope)
```

Return:

```json
{
  "active_claims": [],
  "supporting_evidence": [],
  "historical_evidence": [],
  "conflicts": [],
  "excluded_summary": {
    "superseded_claims": 3,
    "stale_records": 8,
    "quarantined_records": 1
  },
  "trace_extension": {}
}
```

---

## 18. Governance and provenance events

### 18.1 Required events

```text
MEMORY_SOURCE_IMPORTED
MEMORY_SOURCE_PARSE_COMPLETED
MEMORY_RECORD_DELTA_COMPUTED
MEMORY_CLAIM_EXTRACTED
MEMORY_CLAIM_ADDED
MEMORY_CLAIM_DUPLICATED
MEMORY_CLAIM_REFINED
MEMORY_CLAIM_SUPERSEDED
MEMORY_CLAIM_RETRACTED
MEMORY_CLAIM_QUARANTINED
MEMORY_CONFLICT_OPENED
MEMORY_CONFLICT_RESOLVED
MEMORY_ACTIVE_VIEW_REFRESHED
MEMORY_DERIVED_INDEX_UPDATED
MEMORY_CAPSULE_IMPORTED
MEMORY_CAPSULE_EXPORTED
MEMORY_RETENTION_TOMBSTONED
MEMORY_RETENTION_PURGED
MEMORY_RECONCILIATION_FINALIZED
```

### 18.2 Event shape

```json
{
  "event_type": "MEMORY_CLAIM_SUPERSEDED",
  "tenant_id": "<TENANT_ID>",
  "owner_scope_type": "project",
  "owner_scope_id": "cwi-platform",
  "source_version_id": "<SOURCE_VERSION_ID>",
  "old_claim_id": "<OLD_CLAIM_ID>",
  "new_claim_id": "<NEW_CLAIM_ID>",
  "slot_key": "project:cwi.phase.7.status:network-topology",
  "reason_code": "same_slot_newer_authoritative_update",
  "confidence": 0.97,
  "policy_mode": "governed_memory",
  "timestamp": "<UTC_TIMESTAMP>",
  "previous_hash": "<LEDGER_PREVIOUS_HASH>",
  "event_hash": "<LEDGER_EVENT_HASH>"
}
```

### 18.3 Fail-closed rules

Fail closed when:

```text
ledger append required and unavailable,
source authorization fails,
cross-tenant scope mismatch,
memory update would affect ACTION risk and conflict is unresolved,
retention/deletion policy cannot be enforced,
pack/pin validation fails,
source signature required but invalid,
active view refresh fails after memory mutation.
```

---

## 19. Security and privacy requirements

1. Every memory record is tenant- and owner-scoped.
2. Cross-tenant memory access must be impossible through schema, service checks, tests, and indexes.
3. Raw content should not be logged.
4. Source hashes should support deduplication without exposing sensitive text.
5. Sensitive source content should carry labels.
6. Memory retrieval must enforce sensitivity policy before context assembly.
7. Memory capsules must be encrypted or explicitly marked as sanitized test fixtures.
8. Retention and deletion workflows must invalidate derived vector/graph records.
9. Admin/operator overrides must be role-scoped and audited.
10. Model outputs must not become memory without a policy-approved acceptance path.
11. Test fixtures must not include secrets, tokens, real internal endpoints, or customer identifiers.

---

## 20. Implementation options

### Option A — Source-version filtering only

**Description:** For each source family, default to latest/current source versions and filter older source chunks.

| Dimension | Rating |
|---|---|
| Time | Low |
| Cost | Low |
| Risk | Medium |
| Complexity | Low |

Pros:

- Fast P0.
- Stops stale chunk leakage.
- Easy smoke tests.

Cons:

- Weak claim-level lineage.
- Cannot reason across sources well.
- Risks dropping still-valid older facts from partial updates.

Use for:

- Immediate smoke testing.
- First Codex pass.
- Minimal V1/V2 answer-change proof.

### Option B — Claim-level active memory view

**Description:** Extract claims, reconcile by memory slot, maintain active/historical statuses.

| Dimension | Rating |
|---|---|
| Time | Medium |
| Cost | Medium |
| Risk | Low/Medium |
| Complexity | Medium |

Pros:

- Correct default behavior.
- Handles replacement vs extension.
- Supports traceable historical explanations.
- Good fit for gold tests.

Cons:

- Requires entity/slot normalization.
- Requires conflict logic.

Use for:

- Recommended production target.
- Codex P1/P2.
- General CWI memory correctness.

### Option C — Graph-backed truth maintenance and memory capsules

**Description:** Store claims/entities/justifications in a graph-backed memory plane with dependency tracking, derived claim invalidation, and portable capsules.

| Dimension | Rating |
|---|---|
| Time | High |
| Cost | High |
| Risk | Medium |
| Complexity | High |

Pros:

- Best long-term capability.
- Supports dependency-aware reasoning.
- Fits graph/evidence architecture.
- Supports portable memory capsules.

Cons:

- More engineering.
- Needs robust operator tooling.
- Requires careful privacy controls.

Use for:

- Phase 9+ memory capsule architecture.
- Enterprise/personal memory convergence.
- Advanced conflict/lineage reasoning.

### Recommendation

Implement:

```text
P0: Option A source-version filtering
P1/P2: Option B claim-level active memory view
P3+: Option C graph-backed truth maintenance + capsules
```

Design P0/P1 interfaces so Option C can be added without reworking APIs.

---

## 21. Codex implementation tasks

### 21.1 P0 — Generalize chat-history spec into platform memory module

Create or update:

```text
src/backend/services/cwi-orchestrator/Memory/
  MemoryImportService.cs
  MemorySourceFamilyResolver.cs
  MemorySourceParserRegistry.cs
  MemoryRecordDiffService.cs
  MemoryClaimExtractor.cs
  MemoryReconciliationService.cs
  ActiveMemoryViewService.cs
  MemoryContextService.cs
  MemoryTraceService.cs
```

Schemas:

```text
contracts/json-schema/
  memory_source_import.schema.json
  memory_source_version.schema.json
  memory_source_record.schema.json
  memory_claim.schema.json
  memory_reconciliation_decision.schema.json
  memory_active_context.schema.json
  memory_trace_extension.schema.json
  memory_conflict_group.schema.json
  memory_capsule.schema.json
```

Acceptance criteria:

```text
[ ] Chat-history upload still works as one source_type.
[ ] At least one non-chat source_type fixture exists.
[ ] Duplicate import returns NOOP_DUPLICATE.
[ ] Active memory query excludes superseded/stale records.
[ ] Trace extension includes memory source/claim IDs.
```

### 21.2 P1 — Source adapters

Implement parser adapters for:

```text
chat_history_upload
user_instruction
operator_update
knowledge_pack
document_ingest
provenance_event
```

Optional P2 adapters:

```text
connector_record
telemetry_summary
graph_projection
memory_capsule
assistant_response_acceptance
```

Acceptance criteria:

```text
[ ] Each adapter emits normalized records.
[ ] Each adapter emits candidate claims or explicitly declares non-claim source.
[ ] Each adapter carries source authority and sensitivity metadata.
```

### 21.3 P1 — Claim slot reconciliation

Implement:

```text
slot key generation
same-slot replacement
multi-cardinality additive behavior
explicit correction detection
conflict groups
source authority scoring
recency scoring
active view refresh
```

Acceptance criteria:

```text
[ ] V2 active fact replaces V1 active fact for same slot.
[ ] Additive facts remain active together.
[ ] Conflicts are not silently resolved.
[ ] Historical query can show old and new facts.
```

### 21.4 P2 — Retrieval filtering and context assembly

Implement:

```text
MemoryContextService
active claim retrieval
historical intent switch
vector payload filters
graph active filters
excluded-memory summary
trace extension
```

Acceptance criteria:

```text
[ ] Superseded vector chunks do not appear in current-state prompts.
[ ] Historical prompts can include superseded context with labels.
[ ] Response traces include memory fields.
```

### 21.5 P2 — Governance and ledger

Implement:

```text
memory ledger events
policy checks for sensitive mutation
admin conflict resolution
retention tombstone/purge events
fail-closed behavior
```

Acceptance criteria:

```text
[ ] Memory mutation emits provenance.
[ ] Unlogged required mutation fails closed.
[ ] Conflict resolution requires scoped authority.
```

### 21.6 P3 — Admin/debug UI

Implement UI surfaces:

```text
memory source list
source diff viewer
active memory viewer
claim lineage viewer
conflict queue
capsule viewer/exporter
retention/deletion console
```

Acceptance criteria:

```text
[ ] Operator can see why answer changed.
[ ] Operator can resolve a conflict.
[ ] Operator can inspect active vs historical memory.
```

---

## 22. Smoke and gold test design

### 22.1 Fixture structure

```text
fixtures/platform-memory/
  sources/
    chat_history/v1.md
    chat_history/v2.md
    user_preferences/v1.json
    user_preferences/v2.json
    knowledge_pack/v1/pack.json
    knowledge_pack/v2/pack.json
    document_ingest/v1.md
    document_ingest/v2.md
    telemetry_summary/window_001.json
  expected/
    expected_active_memory_after_v1.json
    expected_active_memory_after_v2.json
    expected_deltas.json
    expected_conflicts.json
  prompts/
    current_state_prompts.json
    historical_prompts.json
    conflict_prompts.json
    source_specific_prompts.json
```

### 22.2 Required test cases

| Test area | V1 | V2 / update | Expected behavior |
|---|---|---|---|
| Chat canary | `QA_CANARY_V1_ALPHA` | `QA_CANARY_V2_BRAVO` | V2 active; V1 historical |
| Current baseline | `10G current` | `10H current; 10G superseded` | 10H active |
| User preference | “prefer ZIP artifacts” | “for specs prefer Markdown” | Scope-specific preference update |
| Pack hash | pack hash A | signed pin points to hash B | hash B active if verified |
| Document fact | service port old | service port new in official doc | official doc supersedes old same-slot fact |
| Telemetry | normal status | degraded status for time window | temporal active within window |
| Connector record | item status open | item status closed | closed active if same external record |
| Model answer | assistant said X | official source says Y | official source wins |
| Conflict | source A says model A | source B says model B | conflict unless authority resolves |
| Partial upload | missing older fact | append-only source | preserve older fact |
| Full snapshot | missing older fact | complete snapshot | mark stale if coverage proven |
| Duplicate import | same file twice | same hash | NOOP_DUPLICATE |
| Stale vector leakage | old chunk retrieves | V2 supersedes old | old chunk excluded in current answer |
| Historical query | asks “what changed” | after V2 | old and new shown with labels |
| Retention purge | deletion request | purge/tombstone | active and derived indexes invalidated |

### 22.3 Current-state prompt examples

```json
[
  {
    "id": "mem-current-canary",
    "prompt": "What is the current QA canary in memory?",
    "must_include": ["QA_CANARY_V2_BRAVO"],
    "must_not_include": ["QA_CANARY_V1_ALPHA"]
  },
  {
    "id": "mem-current-baseline",
    "prompt": "What is the current CWI baseline?",
    "must_include": ["10H"],
    "must_not_include": ["10G is current"]
  },
  {
    "id": "mem-current-preference",
    "prompt": "For Codex specs, what file type should you produce by default?",
    "must_include": ["Markdown"],
    "must_not_include": ["ZIP only"]
  }
]
```

### 22.4 Historical prompt examples

```json
[
  {
    "id": "mem-history-canary",
    "prompt": "What changed in the QA canary between memory updates?",
    "must_include": ["QA_CANARY_V1_ALPHA", "QA_CANARY_V2_BRAVO", "superseded"]
  },
  {
    "id": "mem-history-baseline",
    "prompt": "What did memory previously say about the CWI baseline, and what is active now?",
    "must_include": ["10G", "10H", "current"]
  }
]
```

### 22.5 Metrics

| Metric | Target | Meaning |
|---|---:|---|
| Current-answer update pass rate | >= 95% | Active memory updates change answers |
| Stale leakage rate | 0 critical, <= 5% minor | Old facts not presented as current |
| Historical recall pass rate | >= 90% | Old facts remain inspectable |
| Idempotency pass rate | 100% | Same source does not duplicate |
| Conflict honesty | 100% | Unresolved conflicts labeled |
| Trace completeness | 100% | Memory traces present |
| Cross-source authority pass rate | >= 95% | Higher authority source wins |
| Retention invalidation pass rate | 100% | Purged/tombstoned data not retrieved |

---

## 23. Example expected behavior

### 23.1 Current preference update

V1:

```text
The user prefers ZIP output for generated artifacts.
```

V2:

```text
For Codex specifications, the user prefers Markdown documents.
```

Expected active memory:

```text
ZIP artifact preference may remain active for test packs or bundles.
Markdown spec preference becomes active for Codex specification documents.
```

Do not globally replace all artifact preferences unless the source explicitly says the preference is global.

### 23.2 Official source overrides chat history

Chat history says:

```text
Phase 7 is starting.
```

Official phase report says:

```text
Phase 7 is complete.
```

Expected answer:

```text
Phase 7 is complete.
```

Historical answer:

```text
Earlier chat-history memory described Phase 7 as starting. The official phase report superseded that same-slot status with complete.
```

### 23.3 Assistant output does not become truth automatically

Prior assistant response says:

```text
The active pack hash is abc.
```

Signed pack pin says:

```text
The active pack hash is def.
```

Expected answer:

```text
The active pack hash is def.
```

The assistant response is historical output, not authoritative truth.

### 23.4 Temporal telemetry memory

Telemetry window says:

```text
Shard 2 degraded from 10:00 to 10:15.
```

Expected:

```text
During that interval, the degraded status is active.
After recovery evidence, the degraded status becomes historical.
```

---

## 24. Codex acceptance checklist

```text
[ ] Renames/generalizes chat-history memory to platform memory.
[ ] Keeps chat-history upload as source_type=chat_history_upload.
[ ] Adds generic memory source/import schemas.
[ ] Adds source-family and source-version handling.
[ ] Adds normalized records and deterministic hashing.
[ ] Adds claim extraction interface.
[ ] Adds memory slot generation.
[ ] Adds active/historical claim statuses.
[ ] Adds same-slot supersession.
[ ] Adds additive multi-cardinality behavior.
[ ] Adds conflict groups.
[ ] Adds active memory view query.
[ ] Adds current vs historical retrieval modes.
[ ] Adds vector/graph stale-source filters.
[ ] Adds memory trace extension.
[ ] Adds ledger/provenance events.
[ ] Adds at least 3 source adapters: chat_history_upload, user_instruction, document_ingest.
[ ] Adds tests for V1→V2 answer change.
[ ] Adds tests for cross-source authority.
[ ] Adds tests for stale vector suppression.
[ ] Adds tests for duplicate import idempotency.
[ ] Adds tests for historical comparison.
[ ] Adds tests for unresolved conflict honesty.
[ ] Adds tests for source-scoped answers.
[ ] Adds sanitized fixtures only.
```

---

## 25. Suggested Codex prompt

```text
Implement the CWI Memory Reconciliation and Active Context Engine according to docs/specs/cwi_platform_memory_reconciliation_spec_for_codex.md.

Do not implement this as a chat-history-only feature. Chat History Upload is only one source adapter.

Prioritize:
1. generic memory source/import model,
2. deterministic hashing and idempotency,
3. source_family_id/source_version_id handling,
4. memory claim model with slot keys and statuses,
5. active memory view,
6. same-slot supersession and additive multi-cardinality behavior,
7. conflict groups,
8. current vs historical retrieval,
9. vector/graph stale-source filtering,
10. trace extension with active claim and source version IDs,
11. ledger/provenance events,
12. gold tests proving active answers change after newer authoritative memory.

Do not rely on hidden model memory.
Do not permanently delete superseded records except under explicit retention/purge policy.
Do not allow superseded vector chunks to contaminate current-state answers.
Do not treat model answers as truth unless grounded and accepted.
Use sanitized fixtures only.
Produce a short implementation note stating which acceptance criteria are complete.
```

---

## 26. Implementation sequence

### Phase M0 — Smoke-testable baseline

```text
Generic source import
Canonical hash idempotency
Source family/version model
Latest-source filtering
Memory trace stub
Chat-history adapter preserved
```

### Phase M1 — Active memory claims

```text
Claim extraction interface
Slot key normalization
Same-slot supersession
Active memory view
Historical lineage
Conflict groups
```

### Phase M2 — Multi-source authority

```text
Source authority policy
User preference adapter
Document ingest adapter
Pack/pin adapter
Operator override adapter
Cross-source tests
```

### Phase M3 — Retrieval integration

```text
Vector payload filters
Graph active filters
Context builder
Historical intent switch
Excluded memory summary
```

### Phase M4 — Governance and capsules

```text
Memory capsule import/export
Retention tombstone/purge
Admin conflict UI
Capsule encryption/signature hooks
Advanced truth maintenance
```

---

## 27. Failure modes and mitigations

| Failure mode | Impact | Mitigation |
|---|---|---|
| Memory remains chat-history-only | Other sources continue to conflict/stale-leak | Generic source adapter architecture |
| V2 blindly appends | Old and new facts both appear current | Claim slot reconciliation + active view |
| V2 blindly overwrites | Useful history disappears | Immutable source history + lineage |
| Partial update false-deletes facts | Correct facts vanish | Append-safe default unless complete snapshot proven |
| Stale vector chunks leak | Wrong current answers | Active claim/status filters in retrieval |
| Graph edges remain stale | Derived wrong relationships | Graph invalidation/rebuild rules |
| Assistant answer becomes truth | Self-reinforcing hallucination | Assistant output low authority unless accepted |
| Conflict over-resolved | False certainty | Conflict groups and explicit ambiguity |
| Source authority misconfigured | Wrong source wins | Policy config + tests + operator review |
| Cross-tenant leakage | Security incident | Tenant scoping in schema/service/tests |
| Retention purge misses vector | Privacy violation | Purge invalidates derived indexes |
| Ledger append fails | Untraceable mutation | Fail closed or durable WAL by policy |
| Memory trace omitted | Debug/audit failure | Trace schema and gold tests |
| Model prompt includes superseded evidence | Stale answer | Context builder gates evidence by status |

---

## 28. Claim ledger

| Claim | Type | Evidence / Verification | Confidence |
|---|---|---|---|
| Chat History Upload should be one source adapter, not the entire memory architecture. | DERIVED | The same replacement/supersession problem applies to packs, docs, preferences, graph, telemetry, and connector records. | High |
| CWI memory must preserve provenance and traceability. | CWI-SOURCE / TESTABLE | CWI 10H invariants require trace and provenance; verify response trace and ledger tests. | High |
| Vectors should be derived retrieval infrastructure, not source of truth. | CWI-SOURCE / DERIVED | Phase docs classify vector shards as derived/rebuildable; verify source records can rebuild vector payloads. | High |
| Active memory view should exclude superseded/stale claims by default. | DERIVED | Required to stop stale context leakage in current-state answers. | High |
| Historical queries should still access superseded facts. | DERIVED | Required for “why did answer change?” and auditability. | High |
| Source authority must be contextual. | DERIVED | Different source classes govern different domains; a signed pack pin should not decide a user style preference. | High |
| Human analogy is memory reconsolidation. | CITED EXTERNALLY | Cognitive literature describes retrieved memories becoming updateable before restabilization. | Medium/High |
| AI/logical framing is belief revision and truth maintenance. | CITED EXTERNALLY | Belief revision and TMS literature describe updating beliefs and maintaining justifications/consistency. | High |
| Proposed scoring weights are defaults, not authoritative. | ASSUMPTION | Must be tuned against CWI fixtures and operator feedback. | Medium |
| Proposed API paths are draft contracts. | ASSUMPTION | Must be aligned with repo OpenAPI conventions before merge. | Medium |

---

## 29. Two-pass self-audit

### Pass 1 — Scope audit

- Generalized memory beyond chat history.
- Preserved chat-history upload as one adapter.
- Covered packs, docs, graph, vector, connector, telemetry, preferences, provenance, assistant responses, and capsules.
- Kept CWI invariants: trace, provenance, governance, fail-closed behavior.
- Added data model, APIs, Codex tasks, tests, and failure modes.

### Pass 2 — Risk audit

Key remaining risks:

1. **Claim extraction quality may be uneven.**  
   Mitigation: low-confidence quarantine, source evidence references, operator review, gold fixtures.

2. **Authority scoring can become policy-heavy.**  
   Mitigation: start with explicit authority classes and rule-based decisions before weighted scoring.

3. **Vector/graph filters may be missed by one retrieval path.**  
   Mitigation: centralize context assembly in `MemoryContextService`; add negative tests.

4. **Retention/purge may not fully invalidate derived indexes.**  
   Mitigation: require derived-index invalidation events and verification tests.

5. **Historical answers may confuse old vs current facts.**  
   Mitigation: require labels and prompt tests for historical vs current intent.

---

## 30. Final implementation principle

```text
Memory is not a pile of old context.
Memory is a governed active view over immutable source history.

New information does not erase the past.
New information updates the current view when it proves a better current fact.
Old information remains auditable unless retention policy removes it.
Every change has a reason, a source, a scope, and a trace.
```

---

## Appendix A — Minimal P0 schema set

Codex can start with only:

```text
memory_source_families
memory_source_versions
memory_source_records
memory_claims
memory_reconciliation_decisions
memory_active_view
memory_conflict_groups
```

Then add:

```text
memory_entities
memory_claim_edges
memory_capsules
memory_vector_bindings
retention tombstones
```

---

## Appendix B — Minimal P0 source adapters

```text
chat_history_upload
user_instruction
document_ingest
operator_update
```

Pack/pin and telemetry can follow immediately after P0 if current repo wiring makes them easy.

---

## Appendix C — Minimal P0 tests

```text
1. Duplicate import idempotency
2. V1->V2 same-slot supersession
3. Additive multi-cardinality fact
4. Partial upload preserves old fact
5. Complete snapshot retires missing fact
6. Conflict without winner is labeled
7. Active memory excludes superseded claim
8. Historical query includes superseded claim
9. Memory trace includes source_version_id and claim_id
10. Vector payload filter excludes stale source
```
