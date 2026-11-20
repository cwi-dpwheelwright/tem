# CWI Active Context Specification

**Document ID:** CWI-SPEC-MEM-ACTIVE-CONTEXT-001  
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

Active Context defines the current working memory view used in ordinary CWI answers.

Core question:

```text
What should the assistant treat as current right now?
```

This specification ensures CWI does not answer from the full memory archive by default. It must answer from the current, policy-allowed, user-scoped, relevant memory view.

---

## 2. Product Principle

Active Context is not everything CWI has ever seen. Active Context is the subset of memory CWI is allowed to treat as current and relevant now.

The product promise is:

```text
CWI answers from the current governed memory view, not stale history or unrelated private context.
```

---

## 3. In Scope

Active Context may include:

- user identity summary
- user preferences
- current projects
- current priorities
- collaborators and relationships
- open loops
- recent decisions
- relevant documents
- connector-derived current facts
- current operational status
- current governance/approval state

Active Context must be scoped by:

- user
- tenant
- project/workspace
- domain
- source authority
- memory policy
- temporal validity
- conflict state
- current prompt intent

---

## 4. Excluded From Active Context

Active Context must exclude:

- superseded facts
- retracted facts
- deleted/purged facts
- stale facts when current verification is required
- historical facts unless asked
- unresolved conflicts when they block certainty
- unrelated personal details
- memory from other users
- memory from other tenants
- memory blocked by policy
- raw imported chat history unless specifically needed and allowed

---

## 5. Required Runtime Behavior

When answering a normal prompt, CWI must:

1. Determine whether memory is relevant to the prompt.
2. Determine current user and tenant.
3. Retrieve only active memory candidates in scope.
4. Exclude superseded, retracted, deleted, expired, or blocked memory.
5. Exclude unresolved conflicts unless the answer is about the conflict.
6. Apply contextual relevance filtering.
7. Assemble concise active context.
8. Answer with memory trace when memory influenced the response.

---

## 6. User-Facing Behavior

### 6.1 Memory Available

Prompt:

```text
what do you know about me?
```

Expected answer shape:

```text
Here is what I currently know from approved memory:

Work:
- You are building CWI as a governed AI platform.
- Your current focus is memory activation and MCP proof-mode quality.

Collaborators:
- Demitrius is tied to engineering implementation.
- Chad is tied to Smartsheet/Grafana feedback and observability work.

Open loops:
- Validate imported memory retrieval.
- Improve read-only MCP result quality.

Confidence: ...
Sources: memory, imported chat history, graph relationships
Trace: ...
```

### 6.2 No Memory

Expected answer:

```text
I do not have stored memory about you yet. You can import chat history or connect an approved memory source.
```

### 6.3 Partial Memory

Expected answer:

```text
I have limited stored memory so far. Here is what I know...
```

### 6.4 Restricted Memory

Expected answer:

```text
I have stored information, but I am not permitted to use it in this context.
```

---

## 7. Active Context Refresh Triggers

Active Context must refresh when:

- memory import completes
- new source version arrives
- reconciliation changes active records
- conflict is resolved
- memory is retracted
- memory is deleted/purged
- user/tenant scope changes
- policy changes
- temporal validity changes
- connector sync changes current facts

---

## 8. Admin / Debug Requirements

Authorized admin/debug views should show:

- active context for selected user/tenant
- active memory count
- included memory categories
- why each item is active
- source/provenance
- confidence
- freshness
- sensitivity level
- excluded memory summary
- stale/historical memory count
- conflict count
- last active context refresh

No sensitive raw content should be exposed without authorization.

---

## 9. Governance Rules

Hard rules:

- Active Context must be user/tenant isolated.
- Active Context must be explainable.
- Active Context must be revocable.
- Active Context must not override the current prompt.
- Active Context must not override system/runtime policy.
- Active Context must not drive tool execution when confidence/conflict policy blocks it.
- Active Context must be safely disableable.

---

## 10. Acceptance Tests

### 10.1 Current Profile Query

Setup:

- user has activated imported memory

Prompt:

```text
what do you know about me?
```

Expected:

- answer uses active memory for authenticated user
- includes confidence/source/trace

### 10.2 User Isolation

Setup:

- User A imports memory
- User B has no memory

Prompt:

```text
what do you know about me?
```

Expected:

- User A receives memory-backed answer
- User B receives empty state
- no leakage

### 10.3 Superseded Exclusion

Setup:

- old project status is superseded

Prompt:

```text
what is the current project status?
```

Expected:

- current status only
- old status excluded or labeled historical if mentioned

### 10.4 Empty State

Setup:

- no active memory exists

Expected:

- clear no-memory response

### 10.5 Restricted State

Setup:

- memory exists but policy blocks access

Expected:

- restricted-state response
- no sensitive detail leakage

---

## 11. Definition Of Done

Active Context is complete when:

- ordinary answers use current governed memory only
- user and tenant isolation is proven
- superseded/retracted/deleted memory is excluded
- no-memory, partial-memory, and restricted-memory states are clear
- trace shows memory influence
- active context can be inspected by authorized admins
- active context refreshes after memory changes
- feature can be disabled safely

---

## 12. Rollback / Disable Requirements

If Active Context becomes unreliable:

- disable runtime memory injection
- preserve stored memory records
- preserve reconciliation and import ledger
- answer from stateless/general context
- show memory unavailable state when relevant
- keep admin/debug access for investigation

---

## 13. Refinement Addendum - Active Context Priority And Budget

This review identified a risk that active context could become a hidden prompt dump. Active context must be a governed, minimal working view, not all available memory.

Required priority order for ordinary answers:

1. current user request and explicit instructions
2. deterministic/system truth and safety policy
3. approved active memory relevant to the current request
4. current conversation context
5. selected pack behavior when explicitly relevant
6. general model knowledge

Active context must not override the user's current request. If the current request conflicts with stored memory, CWI should use the current request for the current response and, when appropriate, ask whether memory should be updated.

Required additions:

- active context must have a bounded context budget
- memory summaries should be preferred over raw transcript chunks
- stale, superseded, retracted, deleted, quarantined, and unresolved conflicted memory must be excluded
- selected memory must carry source IDs, confidence, temporal basis, and relevance reason into trace
- memory use must be visible as used, skipped, unavailable, restricted, or not relevant
- clearing imported memory or all user memory must immediately remove it from active context
- the active context answer path must not leak memory across users, tenants, browsers, packs, or sessions

Minimum acceptance coverage:

- "what is oceaneering?" does not inject personal memory
- "what do you know about me?" uses active memory when available
- "ignore my memory and answer generally" suppresses memory for that answer
- clearing user memory removes it from active context
- response trace shows which active context items influenced the answer

---

## 14. Approval Gate

Decision requested:

```text
Approve or deny Active Context as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
