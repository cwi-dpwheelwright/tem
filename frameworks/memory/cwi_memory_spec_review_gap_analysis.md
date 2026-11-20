# CWI Memory Specifications Review And Gap Analysis

**Document ID:** CWI-SPEC-MEM-REVIEW-GAPS-001  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Review artifact  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document records the review of the generated memory specification documents under `features/memory/`.

The goal is to make sure the memory requirements are forward-looking, internally consistent, and actionable before implementation starts. This is a requirements review, not an implementation design and not a code handoff.

---

## 2. Files Reviewed

- `cwi_platform_memory_reconciliation_spec_for_codex.md`
- `cwi_memory_core_attributes_specification.md`
- `cwi_memory_reconciliation_specification.md`
- `cwi_memory_conflict_resolution_ambiguity_management_specification.md`
- `cwi_memory_active_context_specification.md`
- `cwi_memory_temporal_awareness_specification.md`
- `cwi_memory_contextual_scope_relevance_filtering_specification.md`

---

## 3. Overall Assessment

The generated specification set is directionally strong. It correctly separates the five memory attributes and keeps the requirements focused on behavior, governance, traceability, and validation rather than code snippets.

The main risk is not missing content. The main risk is independent implementation. If the five attributes are built separately, CWI could end up with memory that reconciles correctly but retrieves stale context, or memory that filters relevance correctly but ignores conflicts.

The specs now need to be treated as one runtime contract:

```text
intake
-> reconciliation
-> conflict detection
-> temporal validation
-> active context assembly
-> relevance filtering
-> memory policy
-> answer or action decision
-> trace and ledger
```

---

## 4. Refinements Applied

### 4.1 Overview Clarification

Updated `cwi_memory_core_attributes_specification.md` to clarify that it is the cross-attribute overview and that the standalone files are the approval documents for their attribute-specific behavior.

### 4.2 Reconciliation Addendum

Updated `cwi_memory_reconciliation_specification.md` to require:

- explicit source modes
- idempotent imports
- partial import safety
- complete replacement scoping
- reindex-only no-truth-change behavior
- user-facing change receipts

### 4.3 Conflict Resolution Addendum

Updated `cwi_memory_conflict_resolution_ambiguity_management_specification.md` to require:

- distinction between ambiguity, stale memory, policy restriction, and direct contradiction
- conflict severity classes
- safe partial-answer rules
- action blocking for unresolved high-risk conflicts

### 4.4 Active Context Addendum

Updated `cwi_memory_active_context_specification.md` to require:

- explicit active context priority order
- bounded context budget
- memory trace reason codes
- immediate exclusion after memory clear
- no cross-user, cross-tenant, browser, pack, or session leakage

### 4.5 Temporal Awareness Addendum

Updated `cwi_memory_temporal_awareness_specification.md` to require:

- runtime current date, time, and timezone injection
- relative-time normalization
- distinction between import time, event time, activation time, effective time, superseded time, and retraction time
- freshness thresholds for time-sensitive answers and actions

### 4.6 Relevance Filtering Addendum

Updated `cwi_memory_contextual_scope_relevance_filtering_specification.md` to require:

- non-memory behavior for general knowledge prompts
- bounded memory injection
- specialized handling for code, summaries, proactive modes, and MCP/tool prompts
- traceable relevance reason codes
- grouped source display expectations

---

## 5. Remaining Gaps And Product Decisions

The following decisions should be resolved before the memory runtime is considered ready for broad use.

### 5.1 Exact Authority Hierarchy

The specs define authority principles, but product and governance still need to approve the exact hierarchy for conflicts.

Decision needed:

```text
When user correction, imported chat history, connector data, admin correction, pinned packs, and policy artifacts disagree, which source wins by domain?
```

Recommended default:

1. safety, security, legal, and governance policy
2. user-authored explicit correction inside that user's scope
3. approved admin/operator correction inside role scope
4. connector/source-of-record data inside its domain
5. pinned/generated memory pack artifacts
6. imported chat history
7. assistant-derived memory only after explicit acceptance

### 5.2 Memory Editing UX

The specs define retrieval and clear behavior, but not the full user-facing editing experience.

Decision needed:

```text
Should Phase 1 support view-only memory, clear-only memory, or user edit/approve/reject memory records?
```

Recommended Phase 1:

- view memory summary
- show source/provenance
- clear imported memory
- clear all user memory
- defer record-level editing to Phase 2

### 5.3 Assistant-Derived Memory Acceptance

The specs correctly avoid treating model output as truth automatically. A future acceptance path is still needed.

Decision needed:

```text
Can CWI propose new memory from a conversation, and what user action is required before it becomes active?
```

Recommended default:

- assistant suggestions remain pending
- user must explicitly approve
- approved memory receives source type `user_approved_assistant_suggestion`
- rejected suggestions are not retrievable

### 5.4 Retention And Expiration By Memory Type

The specs require temporal handling, but retention windows are not yet defined by memory class.

Decision needed:

```text
How long should preferences, projects, tasks, decisions, relationships, imports, and historical records be retained?
```

Recommended default:

- user preferences: until changed or cleared
- active projects/tasks: until closed, superseded, or cleared
- decisions: retained as historical unless cleared by policy
- relationships: until updated, cleared, or marked stale
- raw imports: shortest practical retention, with derived memory preferred
- secrets/credentials: never stored

### 5.5 Admin Role Matrix

The specs reference authorized admin/debug visibility, but the exact role matrix remains open.

Decision needed:

```text
Which roles can view activation status, view memory summaries, view raw source excerpts, resolve conflicts, purge memory, and export audit data?
```

Recommended default:

- normal user: own memory summary, sources, clear controls
- tenant admin: tenant activation status and non-sensitive aggregate diagnostics
- platform admin: operational diagnostics without raw private content unless break-glass is approved
- auditor: trace and ledger metadata, not raw sensitive content by default

### 5.6 Cross-Device And Browser Identity

The user specifically clarified that memory belongs to the authenticated user, not the browser user.

Decision needed:

```text
What identity key is authoritative for memory: authenticated user ID, tenant ID plus user ID, account email, or external identity provider subject?
```

Recommended default:

- tenant ID plus authenticated user ID is authoritative
- browser/session/device IDs are diagnostic only
- pack selection must not change memory ownership

### 5.7 Memory Versus Current Conversation Context

The specs should keep durable memory separate from ephemeral chat context.

Decision needed:

```text
When does current conversation context become durable memory?
```

Recommended default:

- current conversation can influence the immediate response
- durable memory requires explicit import, explicit user save, or approved memory acceptance workflow
- traces should distinguish `conversation_context_used` from `memory_used`

### 5.8 Memory Evaluation Harness

The specs define acceptance tests, but a consolidated gold suite should be approved before implementation.

Decision needed:

```text
What exact prompts, expected answer elements, and negative assertions define memory readiness?
```

Recommended coverage:

- no memory empty state
- superadmin imported memory visible only to superadmin
- dpwheelwright cannot see superadmin memory
- what do you know about me
- work-life only
- home-life only
- projects
- key people
- open loops
- show sources
- conflicting memories
- stale memories
- cleared memory no longer influences answers
- general knowledge prompt does not use memory

### 5.9 Source Display Contract

The source display now needs to align with the UI direction: grouped pills by source type, not noisy raw lists.

Decision needed:

```text
What source group labels and counts should appear in ordinary answers versus debug views?
```

Recommended default:

- ordinary answer: compact grouped source pills such as `Memory 4`, `Web 2`, `Graph 1`
- expanded source panel: source title, type, URL or source ID, confidence, and trace ID where authorized
- debug/admin: full source IDs, memory IDs, claim IDs, source hashes, and ledger references

---

## 6. Cross-Spec Acceptance Criteria

Memory activation should not be accepted until all five attributes pass together.

Required end-to-end acceptance:

- imported memory becomes available only to the authenticated user who imported it
- memory survives page reload and new session for that same authenticated user
- another authenticated user receives no leakage
- duplicate import does not duplicate active memory
- updated import changes active memory and preserves history
- contradicted memory asks for clarification or blocks action
- stale memory is labeled or excluded
- future-effective memory is not applied early
- general knowledge questions avoid personal memory
- memory-backed answers show confidence, trace, and grouped source indicators
- clearing imported memory removes imported memory from runtime answers
- clearing all user memory returns the user to fresh-slate behavior

---

## 7. Recommended Next Work Items

1. Convert this specification set into a single memory readiness checklist.
2. Create a gold prompt suite for no-memory, imported-memory, cross-user isolation, conflict, stale, and clear-state scenarios.
3. Define the final authority hierarchy by domain.
4. Define Phase 1 user memory controls: view, clear imported memory, clear all user memory.
5. Define admin/debug role matrix.
6. Define memory trace and source pill display contract.
7. Implement only after product approves the five standalone specs plus this gap review.

---

## 8. Approval Note

The five standalone specs are now stronger than the first draft. The remaining gaps are not blockers for writing code experiments, but they are blockers for declaring the memory runtime production-ready.

Recommended decision:

```text
Approve the refined specification set for implementation planning.
Separately approve or reject the remaining product decisions in Section 5.
```
