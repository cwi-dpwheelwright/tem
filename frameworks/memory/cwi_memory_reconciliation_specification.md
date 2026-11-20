# CWI Memory Reconciliation Specification

**Document ID:** CWI-SPEC-MEM-RECONCILIATION-001  
**Related documents:**  
- `features/memory/cwi_platform_memory_reconciliation_spec_for_codex.md`  
- `features/memory/cwi_memory_core_attributes_specification.md`  
**Version:** 1.0  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Draft for approval  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

Memory Reconciliation defines how CWI handles duplicate, updated, replaced, deleted, retracted, or superseded memory.

Core question:

```text
What changed when new memory arrived?
```

This specification ensures CWI does not blindly append new memory and does not blindly overwrite old memory. Every new memory candidate must be compared against existing governed memory and classified before it can influence the active context used in responses.

---

## 2. Product Principle

CWI memory must behave like governed source-aware working knowledge.

When new context arrives:

- old context should not disappear without provenance
- duplicate context should not multiply
- corrected context should supersede old active memory
- retracted context should stop influencing current answers
- partial imports should not falsely delete missing facts
- complete replacement snapshots should retire only the memory they explicitly cover
- users and admins should be able to ask what changed and why

The product promise is:

```text
CWI can explain how memory changed, what is now current, and what became historical.
```

---

## 3. Scope

### 3.1 In Scope

Memory Reconciliation applies to memory candidates from:

- chat history import
- user instruction
- operator/admin correction
- document ingest
- connector records
- knowledge packs
- graph projections
- prior approved memory capsules
- ledger/provenance events
- approved assistant-response memory

### 3.2 Out Of Scope

This specification does not define:

- database tables
- vector index design
- graph schema implementation
- exact reconciliation scoring code
- UI component implementation
- model prompt details

Those belong in implementation plans after approval.

---

## 4. Required Reconciliation Outcomes

Every incoming memory candidate must receive one explicit outcome.

| Outcome | Meaning | Active Context effect |
|---|---|---|
| `new_memory` | No equivalent memory exists yet. | Candidate may become active if allowed. |
| `duplicate_noop` | Equivalent memory already exists. | No duplicate active memory is created. |
| `additive_memory` | Candidate adds compatible detail. | Candidate can be active alongside existing memory. |
| `refined_memory` | Candidate clarifies or narrows existing memory. | Refined version becomes preferred active memory. |
| `updated_memory` | Candidate changes current value for same memory slot. | Prior active memory becomes historical/superseded. |
| `superseded_memory` | Existing active memory is replaced by newer governed memory. | Old memory excluded from current answers. |
| `retracted_memory` | Prior memory is explicitly withdrawn/corrected. | Retracted memory excluded from current answers. |
| `deleted_memory` | Memory removed by retention/privacy workflow. | Memory unavailable except allowed audit residue. |
| `conflict_opened` | Candidate contradicts existing memory and no safe winner exists. | Active use is blocked or labeled. |
| `ignored_memory` | Candidate is unsafe, unauthorized, irrelevant, or low confidence. | Candidate does not become active. |

---

## 5. Memory Slot Matching

Reconciliation must compare memory within scoped slots.

A memory slot is the comparable place a claim occupies:

```text
tenant + user/project/entity + subject + predicate + scope + effective time
```

Examples:

| Memory | Slot behavior |
|---|---|
| "Christian prefers Markdown specs" | Single current preference slot. |
| "Demitrius owns orchestrator work" | Role/ownership slot for project scope. |
| "CWI is in MCP proof mode" | Current project phase/status slot. |
| "Chad is working on Smartsheet" | Additive collaborator/project relationship. |
| "The project was paused in March" | Historical project status slot. |

Reconciliation must not compare unrelated slots. A work preference should not replace a home preference. A project status should not replace a user identity fact.

---

## 6. Source Authority Rules

Recency alone must not decide reconciliation.

Source authority should be considered:

| Source type | Typical authority |
|---|---|
| authenticated current user correction | High for user-owned memory |
| tenant admin/operator correction | High for tenant/project memory |
| signed knowledge pack or pin | High for governed platform facts |
| connector record | High for source-specific external records |
| document ingest | Medium/high depending on source trust |
| chat history import | Medium unless user-approved and source-specific |
| assistant response | Low unless explicitly accepted/grounded |
| inferred graph projection | Derived; should reference base evidence |

Rules:

- newer casual memory must not override older authoritative memory automatically
- explicit corrections should override prior same-scope memory when authorized
- source authority decisions must be traceable
- low-authority memory may still be additive if not conflicting
- policy can block unsafe or sensitive memory regardless of authority

---

## 7. Required Runtime Behavior

When memory arrives, CWI must:

1. Normalize the incoming memory candidate.
2. Identify source family, source version, actor, tenant, user, and scope.
3. Extract comparable claims where possible.
4. Resolve candidate memory slots.
5. Compare against existing active and historical memory in the same scope.
6. Determine reconciliation outcome.
7. Apply policy and source authority.
8. Update active context only if allowed.
9. Preserve old memory as historical, superseded, retracted, or deleted according to policy.
10. Emit trace/provenance and ledger events.
11. Refresh derived retrieval views.

Memory must not become active without passing this process.

---

## 8. User-Facing Behavior

### 8.1 When User Asks What Changed

Prompt examples:

```text
what changed when new memory arrived?
what changed in my memory?
what did the latest import update?
what changed since the last upload?
```

Expected answer:

- summarize added memory
- summarize updated memory
- summarize superseded memory
- summarize retracted memory
- identify unresolved conflicts
- state what is active now
- include confidence and trace/source references

Example:

```text
Three memory changes were applied.

Added:
- You are preparing CWI for beta testing.

Updated:
- Current priority changed from MCP proof mode to memory activation validation.

Superseded:
- Older memory saying imported chat history was unavailable at runtime is now historical.

Open conflict:
- One source says Chad owns dashboard delivery; another says Christian owns final dashboard validation. I need clarification before treating either as the single current owner.

Active context now reflects the updated priority.
```

### 8.2 When No Changes Were Applied

Expected answer:

```text
No active memory changes were applied. The latest source matched memory I already had, so it was treated as a duplicate.
```

### 8.3 When Memory Was Ignored

Expected answer:

```text
The new source did not update active memory because it was outside the current scope or did not contain memory-worthy claims.
```

If policy blocked it:

```text
The new source was not added to active memory because policy blocked it.
```

---

## 9. Admin / Debug Requirements

Authorized admin/debug views must show:

- incoming source family/version
- source type
- actor
- tenant/user/project scope
- candidate memory count
- reconciliation outcome count
- prior memory record
- new memory record
- source authority class
- confidence
- policy decision
- why memory was accepted, ignored, superseded, retracted, or conflicted
- trace ID
- request ID
- ledger event ID
- active context refresh status

Admin/debug surfaces must not expose secrets, raw credentials, or unauthorized private content.

---

## 10. Trace And Provenance Requirements

Every reconciliation event must be traceable.

Trace should include:

- memory candidate ID
- resulting memory record ID
- prior memory record ID if applicable
- reconciliation outcome
- source family/version
- source type
- source authority
- scope
- confidence
- reason
- conflict group ID when created
- ledger event reference

User-facing answers should stay concise; detailed reconciliation metadata belongs in Glass Box/admin/debug views.

---

## 11. Governance Rules

Hard rules:

- Duplicate imports must not create duplicate active memory.
- Superseded memory must not be used in current answers.
- Retracted memory must not be used in current answers.
- Deleted memory must follow retention/privacy policy.
- Old memory must remain historically auditable unless purged by policy.
- User-specific memory must not affect other users.
- Tenant-specific memory must not cross tenants.
- Unsafe memory must fail closed.
- Model output must not become memory unless accepted under policy.
- Reconciliation must not bypass memory policy, safety policy, or identity policy.

---

## 12. Acceptance Tests

### 12.1 Duplicate Import

Setup:

- Import the same chat history twice for the same user.

Expected:

- second import returns duplicate/no-op outcome
- active memory count does not double
- trace shows duplicate handling

### 12.2 Updated Priority

Setup:

- existing memory says current priority is "web toggle"
- new governed source says current priority is "memory activation"

Expected:

- active context uses "memory activation"
- old value becomes historical/superseded
- "what changed" explains the update

### 12.3 Retraction

Setup:

- memory says "Chad owns dashboard delivery"
- later authorized source retracts that assignment

Expected:

- assignment no longer appears in active context
- historical trace remains
- "what changed" includes retraction

### 12.4 Partial Import

Setup:

- existing memory has five project facts
- partial import includes two of them

Expected:

- missing three facts are not automatically deleted
- import scope is recorded as partial

### 12.5 Complete Replacement Snapshot

Setup:

- source explicitly declares full replacement for a scoped source family
- prior records in that scope are absent from replacement

Expected:

- absent prior records can be retired/superseded
- records outside declared scope are untouched

### 12.6 Cross-User Isolation

Setup:

- User A imports updated memory
- User B asks what changed

Expected:

- User B cannot see User A memory changes

---

## 13. Definition Of Done

Memory Reconciliation is complete when:

- every memory candidate receives a reconciliation outcome
- duplicate memory does not duplicate active context
- updated memory supersedes prior active memory
- retracted memory is excluded from current answers
- partial imports do not cause false deletion
- full replacement snapshots retire only declared-scope records
- old memory remains historically inspectable
- user and tenant isolation are proven
- reconciliation events appear in trace/debug/ledger
- users can ask what changed and receive a clear answer
- feature can be disabled safely without deleting stored memory

---

## 14. Rollback / Disable Requirements

If reconciliation is unreliable:

- disable memory mutation into active context
- continue storing raw source intake if policy allows
- preserve existing active memory
- stop using newly reconciled memory in responses
- show admin-visible warning
- keep ledger events for investigation

Rollback must not physically delete memory unless a privacy/retention workflow requires it.

---

## 15. Refinement Addendum - Source Modes And Change Receipts

This review identified one implementation risk: reconciliation can be interpreted too narrowly as duplicate detection. It must also define how each import or update is allowed to affect previous memory.

Required additions:

- every memory intake must declare or infer a source mode: append-only, partial update, complete scoped replacement, correction, retraction, reindex-only, or retention purge
- complete replacement is only allowed inside an explicit source family, user, tenant, domain, and time scope
- partial imports must never delete absent memories
- reindex-only runs must not change active memory truth
- retries must be idempotent and must not create duplicate active memory
- every reconciliation run must produce a change receipt that can answer "what changed when new memory arrived?"
- the change receipt must distinguish added, unchanged, updated, superseded, retracted, deleted, quarantined, and rejected memory
- the change receipt must be available to runtime answers, admin/debug views, and ledger/audit review

Minimum user-facing answer for a change query:

```text
Since the last activation, I found 3 new memories, updated 2 current memories, and moved 1 older memory to history. Nothing was deleted outside the approved import scope.
```

Minimum acceptance coverage:

- importing the same file twice does not duplicate active memory
- partial upload does not retire absent records
- complete replacement only affects the declared scope
- retraction removes memory from active context but preserves provenance
- "what changed?" produces a clear change summary with trace

---

## 16. Approval Gate

Decision requested:

```text
Approve or deny Memory Reconciliation as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
