# CWI Memory Core Attributes Specification

**Document ID:** CWI-SPEC-MEM-CORE-ATTRIBUTES-001  
**Related document:** `features/memory/cwi_platform_memory_reconciliation_spec_for_codex.md`  
**Version:** 1.0  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Draft for approval  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document defines the five core attributes CWI memory must support for governed, user-owned, runtime-usable memory:

1. Memory Reconciliation
2. Conflict Resolution / Ambiguity Management
3. Active Context
4. Temporal Awareness / Time-Based Context
5. Contextual Scope / Relevance Filtering

The goal is to prevent CWI memory from behaving like a simple append-only note store. CWI must understand when memory changed, when memory disagrees, what memory is current, when memory was true, and which memories are relevant to the current response.

This document is intentionally requirements-focused. It defines behavior, user experience, governance expectations, validation scenarios, and acceptance criteria. It does not prescribe specific code, database schemas, libraries, or vector-indexing implementation details.

---

## 2. Relationship To Existing Memory Reconciliation Spec

The existing platform specification describes the broad Memory Reconciliation and Active Context Engine. This document turns that architecture into five explicit product and engineering attributes that must be implemented, tested, and validated independently.

The existing spec answers:

```text
How should CWI reconcile memory as a platform capability?
```

This spec answers:

```text
What are the five required memory behaviors users and operators must be able to trust?
```

Both documents should be treated as aligned. If a conflict appears, this document should be used to clarify the required behavior, while the existing document should be used for deeper platform architecture.

---

## 2.1 Standalone Attribute Specifications

This document is the cross-attribute overview. The standalone specifications under `features/memory/` are the approval documents for their specific behavior:

- `cwi_memory_reconciliation_specification.md`
- `cwi_memory_conflict_resolution_ambiguity_management_specification.md`
- `cwi_memory_active_context_specification.md`
- `cwi_memory_temporal_awareness_specification.md`
- `cwi_memory_contextual_scope_relevance_filtering_specification.md`

Engineering should not implement these as five isolated features. They are one memory runtime with five required decision layers. The correct user experience depends on the layers agreeing on source authority, temporal validity, active status, relevance, and trace output.

If the overview and a standalone specification disagree, use the stricter requirement until product and architecture explicitly approve a change.

---

## 3. Product Principle

CWI memory must behave like governed working knowledge, not hidden model memory.

That means:

- Memory must be source-aware.
- Memory must be user and tenant scoped.
- Memory must distinguish current facts from historical facts.
- Memory must not silently guess when sources disagree.
- Memory must only inject memory that belongs in the current response.
- Memory must preserve provenance and traceability.
- Memory must be explainable to the user and inspectable by authorized admins.

The core product promise is:

```text
CWI remembers what it is allowed to remember,
knows when that memory changed,
knows when that memory conflicts,
knows what is current,
knows when it was true,
and knows whether it belongs in the answer.
```

---

## 4. Required Runtime Order

Memory behavior must follow this conceptual order:

```text
source intake
-> normalize memory candidate
-> reconcile against existing memory
-> detect conflicts / ambiguity
-> evaluate temporal validity
-> compute active context
-> filter by scope and relevance
-> apply memory policy
-> assemble response context
-> answer with trace/provenance
```

Important rule:

Contextual relevance filtering must happen before memory is injected into the final answer. Memory should not be added first and then trusted to the model to ignore irrelevant details.

---

## 5. Common Terms

### Memory Candidate

A proposed memory extracted from an import, user instruction, connector record, document, graph event, operator update, or other governed source.

### Memory Record

A stored memory item with identity, source, scope, confidence, status, temporal fields, and provenance.

### Memory Claim

A normalized assertion that can be compared against other claims.

Examples:

- "Christian is building CWI."
- "Demitrius owns orchestrator implementation."
- "The preferred spec format is Markdown."
- "Project X is paused."

### Memory Slot

The place where comparable claims compete or accumulate.

A memory slot represents the combination of:

```text
tenant + user/project/entity + subject + predicate + scope + time
```

Example:

```text
tenant=CWI-ops
user=superadmin
subject=CWI
predicate=current_priority
scope=work
time=current
```

### Active Context

The current, policy-allowed, task-relevant memory view used in normal answers.

### Historical Memory

Memory that was once true, imported, observed, or asserted, but is not necessarily current.

### Conflict Group

A set of memory claims that cannot all safely be treated as true at the same time.

---

## 6. Core Attribute 1 - Memory Reconciliation

### 6.1 Definition

Memory Reconciliation handles duplicate, updated, replaced, deleted, retracted, or superseded memory.

Core question:

```text
What changed when new memory arrived?
```

### 6.2 Required Behavior

When new memory arrives, CWI must compare it against existing memory before making it active.

CWI must classify the incoming memory as one of the following:

| Reconciliation outcome | Meaning |
|---|---|
| `new_memory` | No equivalent memory exists yet. |
| `duplicate_noop` | Same meaning already exists; do not create duplicate active memory. |
| `additive_memory` | New memory adds detail without replacing old memory. |
| `refined_memory` | New memory clarifies or narrows old memory. |
| `updated_memory` | New memory changes the current value for the same slot. |
| `superseded_memory` | Old memory is no longer current because newer governed memory replaces it. |
| `retracted_memory` | A prior memory is explicitly withdrawn or corrected. |
| `deleted_memory` | A memory is removed from usable context by retention/privacy action. |
| `conflict_opened` | New memory contradicts existing memory and no safe winner exists. |
| `ignored_memory` | Candidate is out of scope, unsafe, unauthorized, too low confidence, or not memory-worthy. |

### 6.3 Source Handling

Reconciliation must support memory coming from multiple source types, including:

- chat history import
- user instruction
- operator/admin correction
- document ingest
- connector record
- knowledge pack
- graph projection
- prior approved memory capsule
- ledger/provenance event

The source type must be recorded and used in reconciliation decisions. Recency alone must not decide which memory wins.

### 6.4 User-Facing Behavior

When the user asks what changed after new memory arrives, CWI should answer with:

- newly added memory
- updated memory
- superseded memory
- retracted memory
- conflicts opened
- what is now active
- source/provenance summary

Example expected answer shape:

```text
Three memory changes were applied.

Added:
- You are preparing CWI for beta testing.

Updated:
- Current priority changed from "MCP proof mode" to "memory activation validation."

Superseded:
- Older note saying chat history was not runtime-accessible is now historical.

Open conflict:
- One source says the dashboard owner is Chad; another says Christian. I need confirmation before treating either as current.

Active context now reflects the updated priority. Trace: ...
```

### 6.5 Admin / Debug Behavior

Authorized admin/debug views must show:

- reconciliation outcome
- prior memory record
- new memory record
- source family/version
- source authority class
- actor or system that introduced the memory
- reason for accepting, ignoring, superseding, or opening conflict
- trace ID / ledger event

### 6.6 Governance Rules

- Duplicate imports must not inflate active memory.
- Old memory must not be physically deleted unless a retention/privacy workflow requires deletion.
- Superseded memory must remain historically auditable.
- User-specific memory must not affect other users.
- Tenant-specific memory must not cross tenant boundaries.
- Unsafe or unverified memory must not become active.
- Model-generated statements must not become memory unless accepted under policy.

### 6.7 Acceptance Tests

1. Duplicate import
   - Given the same chat history is imported twice
   - Expected: second import produces `duplicate_noop`; active memory count does not duplicate.

2. Updated project priority
   - Given old memory says priority is "web toggle"
   - And new governed memory says priority is "memory activation"
   - Expected: active context uses "memory activation"; old priority is historical.

3. Retraction
   - Given memory says a collaborator owns a task
   - And a later source retracts that assignment
   - Expected: assignment is removed from active context but remains traceable.

4. Partial import
   - Given a partial import omits older facts
   - Expected: omitted facts are not automatically deleted.

5. Complete replacement
   - Given a source declares itself a full replacement snapshot
   - Expected: missing prior records in the same declared scope can be retired.

### 6.8 Definition Of Done

Memory Reconciliation is done when:

- every incoming memory candidate receives a reconciliation outcome
- duplicate memory does not become duplicate active context
- updated memory supersedes old active memory
- retracted memory is not used in current answers
- historical lineage remains inspectable
- reconciliation events are traceable and logged

---

## 7. Core Attribute 2 - Conflict Resolution / Ambiguity Management

### 7.1 Definition

Conflict Resolution / Ambiguity Management handles cases where memory sources disagree and no safe single truth exists yet.

Core question:

```text
What do we do when memories contradict?
```

### 7.2 Required Behavior

CWI must detect conflict when:

- two or more memory claims occupy the same slot
- the claims assert incompatible values
- both claims are plausible enough to consider
- policy or authority rules do not clearly select a winner

CWI must not silently choose one conflicting memory unless a configured authority rule, user correction, admin action, or explicit source policy permits it.

### 7.3 Conflict States

| Conflict state | Meaning |
|---|---|
| `open` | Conflict exists and no winner is selected. |
| `needs_user_clarification` | User can resolve or narrow the conflict. |
| `needs_admin_review` | Requires authorized operator/admin resolution. |
| `policy_blocked` | Memory cannot be resolved or used due to policy. |
| `resolved` | A winner or merged interpretation has been selected. |
| `historical_only` | Conflict is retained only for audit/history, not active response context. |

### 7.4 User-Facing Behavior

If conflicting memory is relevant to an answer, CWI must be honest.

CWI should say:

- that memory conflicts
- what the competing claims are, if allowed
- what source types they came from, if allowed
- what clarification or approval is needed
- what it can answer safely without resolving the conflict

Example expected answer shape:

```text
I have conflicting memory about this.

One source says Chad owns the dashboard work.
Another source says Christian owns final dashboard validation.

I should not treat either as the only current truth yet. I can answer from the non-conflicting project context, or you can clarify who owns which part.
```

### 7.5 Response Rules

For unresolved conflicts:

- Do not fabricate certainty.
- Do not bury the conflict in trace only.
- Do not use one side for action execution.
- Do not inject both sides as if both are simultaneously true.
- Ask a clarification question when the conflict blocks the answer.
- Continue with safe non-conflicting context when possible.

### 7.6 Resolution Authority

Conflict resolution can come from:

- current authenticated user, for their own personal memory
- tenant admin, for tenant-scoped memory
- operator/admin, for platform/governance memory
- approved source authority rule
- signed pack/pin authority
- explicit correction event

Resolution must record:

- who or what resolved it
- when it was resolved
- which claim won or how claims were merged
- why the resolution was allowed
- trace/ledger reference

### 7.7 Admin / Debug Behavior

Admin/debug views should expose:

- open conflict count
- conflicts by user, tenant, project, domain, source type
- severity
- blocked response/action count
- competing claims
- source summaries
- recommended resolution
- resolution history

Sensitive raw memory should not be exposed unless the viewer is authorized.

### 7.8 Governance Rules

- Low-confidence conflict resolution must ask for clarification.
- Sensitive conflicts must not leak private facts.
- Conflicted memory must not be used for tool execution unless policy explicitly permits a safe read-only path.
- Conflict resolution must be scoped; resolving one user's memory must not resolve another user's memory.
- Resolution must be reversible or historically auditable.

### 7.9 Acceptance Tests

1. Direct contradiction
   - Source A says project status is "paused"
   - Source B says project status is "active"
   - Expected: open conflict; no silent winner.

2. Authority winner
   - User memory says one preference
   - Later explicit authenticated user correction says another
   - Expected: correction wins; old value becomes historical.

3. Ambiguous person reference
   - Memory contains multiple people named John
   - User asks "call John"
   - Expected: clarification, no execution.

4. Restricted conflict
   - Conflict includes sensitive personal data
   - Expected: response acknowledges access restriction without exposing raw sensitive details.

5. Resolved conflict
   - Admin resolves a conflict
   - Expected: active context uses resolved value and trace shows resolution provenance.

### 7.10 Definition Of Done

Conflict Resolution / Ambiguity Management is done when:

- same-slot contradictions are detected
- unresolved conflicts are visible and traceable
- CWI does not invent certainty
- users can clarify user-owned conflicts
- authorized admins can resolve scoped conflicts
- conflicted memory does not drive unsafe responses or actions

---

## 8. Core Attribute 3 - Active Context

### 8.1 Definition

Active Context defines the current working memory view used in ordinary answers.

Core question:

```text
What should the assistant treat as current right now?
```

### 8.2 Required Behavior

Active Context must be computed from memory records that are:

- current
- policy-allowed
- relevant to the user/tenant/session
- not superseded
- not retracted
- not deleted or purged
- not unresolved-conflicted in a way that blocks the answer
- temporally valid for the current question
- scoped to the current user, project, task, connector, or domain

Active Context is not the full memory store. It is the filtered, current, governed view that can safely influence the answer.

### 8.3 Default Answer Behavior

For ordinary questions, CWI should use Active Context only.

It should not include:

- superseded facts
- stale vector chunks
- historical facts unless asked
- unrelated personal details
- memory from other users
- memory from other tenants
- memory blocked by policy
- unresolved conflict claims as if true

### 8.4 Active Context Categories

Active Context should be able to include these categories when relevant:

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

### 8.5 User-Facing Behavior

When asked "what do you know about me?", CWI should answer from Active Context, not from the full historical archive.

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

If no Active Context exists:

```text
I do not have stored memory about you yet. You can import chat history or connect an approved memory source.
```

If memory exists but is blocked:

```text
I have stored information, but I am not permitted to use it in this context.
```

### 8.6 Active Context Refresh

Active Context must refresh when:

- memory import completes
- new source version arrives
- reconciliation changes active records
- conflict is resolved
- memory is retracted
- retention/deletion changes availability
- user/tenant scope changes
- policy changes
- temporal validity changes

### 8.7 Admin / Debug Behavior

Authorized views should show:

- active context for current user/tenant
- why each active item is active
- source/provenance
- confidence
- freshness
- sensitivity level
- excluded memory summary
- stale/historical memory count
- conflicted memory count

### 8.8 Governance Rules

- Active Context must be user/tenant isolated.
- Active Context must be explainable.
- Active Context must be revocable.
- Active Context must not override the current prompt.
- Active Context must not be used for execution if confidence or conflict policy blocks it.
- Active Context must be disabled safely through configuration.

### 8.9 Acceptance Tests

1. Current profile query
   - User asks "what do you know about me?"
   - Expected: answer uses active memory for that authenticated user.

2. User isolation
   - User A imports memory
   - User B asks "what do you know about me?"
   - Expected: User B cannot see User A memory.

3. Superseded exclusion
   - Old project status is superseded
   - User asks current project status
   - Expected: current status only, old status excluded or labeled historical if mentioned.

4. Empty state
   - No active memory exists
   - Expected: clear empty-state message.

5. Restricted state
   - Memory exists but policy blocks it
   - Expected: restricted-state message without sensitive details.

### 8.10 Definition Of Done

Active Context is done when:

- ordinary answers use current governed memory only
- user and tenant isolation is proven
- superseded/retracted/deleted memory is excluded
- no-memory and restricted-memory states are clear
- trace shows memory influence
- active context can be inspected by authorized admins

---

## 9. Core Attribute 4 - Temporal Awareness / Time-Based Context

### 9.1 Definition

Temporal Awareness tracks when memories were true, changed, expired, or became historical.

Core question:

```text
When was this true, and is it still true?
```

### 9.2 Required Behavior

CWI memory must distinguish:

- when a source was created
- when CWI received it
- when the memory became valid
- when the memory stopped being valid
- when CWI learned about the change
- whether the memory is current, historical, stale, expired, or superseded

These are different concepts and must not be collapsed into a single timestamp.

### 9.3 Temporal States

| Temporal state | Meaning |
|---|---|
| `current` | Valid now and eligible for Active Context. |
| `future_effective` | Known now but not active until a future time. |
| `historical` | Was true or asserted in the past; not current by default. |
| `superseded` | Replaced by a newer active memory. |
| `expired` | No longer valid after a known expiry. |
| `stale_unverified` | Too old or unrefreshed for confident current use. |
| `time_uncertain` | Time bounds are unknown or ambiguous. |

### 9.4 Time-Aware Question Types

CWI must handle:

| User question type | Expected memory behavior |
|---|---|
| "What is true now?" | Use current active memory. |
| "What changed?" | Compare current and prior memory. |
| "What did I believe before?" | Use historical memory with labels. |
| "As of last week..." | Use memory valid at that time if available. |
| "Is this still true?" | Check current status and latest source. |
| "When did this change?" | Use reconciliation and temporal lineage. |

### 9.5 User-Facing Behavior

CWI should explicitly label temporal uncertainty.

Example:

```text
As of the latest approved memory, the project is active.

Previously, it was marked as paused in an older import. That older status was superseded when the newer project update arrived.

I do not have a newer update after that source.
```

For stale memory:

```text
I have memory about this, but it may be stale. The latest source I have is from March 2026.
```

For historical lookup:

```text
At that time, the active priority was MCP proof mode. It later changed to memory activation validation.
```

### 9.6 Expiration And Decay

Some memories should expire or become stale:

- temporary tasks
- meetings
- short-lived operational status
- weather/current event summaries
- connector sync snapshots
- pending approval state

Other memories may remain durable:

- stable user preferences
- long-lived project relationships
- confirmed identity profile
- historical decisions
- governance receipts

Temporal behavior must be determined by memory type and policy, not by a single global TTL.

### 9.7 Admin / Debug Behavior

Admin/debug views should show:

- valid-from time
- valid-to time
- observed-at time
- imported-at time
- superseded-at time
- expiry/staleness policy
- current/historical status
- timeline of changes

### 9.8 Governance Rules

- Historical memory must not be presented as current.
- Stale memory must be labeled when used.
- Expired memory must not be used in current answers unless the user asks for historical context.
- Time-sensitive memory must not drive actions without current verification.
- User timezone and request date must be considered for relative date questions.

### 9.9 Acceptance Tests

1. Superseded timeline
   - Old memory says task status is "open"
   - New memory says task status is "closed"
   - User asks "when did this change?"
   - Expected: answer includes old status, new status, and change/source timing.

2. Current-only answer
   - User asks current status
   - Expected: only current memory is used by default.

3. Historical answer
   - User asks "what was true before?"
   - Expected: historical memory appears with historical label.

4. Stale memory warning
   - Latest memory is older than policy permits for current confidence
   - Expected: answer labels staleness or asks to refresh.

5. Future effective memory
   - Memory says preference changes next month
   - Expected: current answer does not apply it prematurely.

### 9.10 Definition Of Done

Temporal Awareness is done when:

- current and historical memory are distinct
- answers can explain when memory changed
- stale/expired memory is not silently treated as current
- as-of queries work
- time-sensitive actions require current enough memory
- traces include temporal basis for memory use

---

## 10. Core Attribute 5 - Contextual Scope / Relevance Filtering

### 10.1 Definition

Contextual Scope / Relevance Filtering selects only the memories relevant to the current task, user, domain, or situation.

Core question:

```text
Which memories belong in this response?
```

### 10.2 Required Behavior

CWI must filter memory before response generation based on:

- authenticated user
- tenant
- active project or workspace
- domain
- current prompt intent
- source type
- sensitivity policy
- temporal validity
- confidence
- conflict state
- task/action risk
- selected pack relevance
- connector/source relevance

Memory should be included only when it helps answer the current user request.

### 10.3 Scope Types

| Scope type | Example |
|---|---|
| `user_private` | User preferences, imported personal chat history |
| `tenant` | Tenant-wide policies, shared approved documents |
| `project` | CWI MCP workstream, dashboard project |
| `domain` | Finance, health, work, home, operations |
| `conversation` | Current chat context |
| `source_specific` | Only a named upload or document |
| `connector_specific` | Only Smartsheet, Nextcloud, calendar, email, etc. |
| `time_window` | Last 24 hours, this week, as of a date |
| `action_scope` | Memory allowed for read-only vs write-sensitive action |

### 10.4 Relevance Tiers

| Tier | Behavior |
|---|---|
| `required` | Needed to answer correctly. |
| `supporting` | Helps answer but is not essential. |
| `background` | Useful for tone or continuity, but should be brief. |
| `excluded` | Not relevant enough to include. |
| `blocked` | Relevant but not allowed by policy. |

### 10.5 User-Facing Behavior

CWI should not dump every known memory into ordinary answers.

For broad memory questions:

```text
User: What do you know about me?
Expected: concise categorized summary with relevant active memory.
```

For scoped memory questions:

```text
User: What do you know about my work life?
Expected: work memory only; personal/home memory omitted unless directly relevant and permitted.
```

For unrelated questions:

```text
User: What is the capital of Texas?
Expected: direct answer. Personal memory should not be injected.
```

For current task planning:

```text
User: Help me make a plan for this.
Expected: use memory relevant to current project/task only.
```

### 10.6 Exclusion Transparency

CWI does not need to list every excluded memory in normal answers. However, trace/debug should show:

- memory searched
- memory included
- memory excluded
- exclusion reasons
- blocked memory count
- stale/historical memory count
- conflict-blocked memory count

When the user explicitly asks "why did you use that memory?" or "show sources", CWI should provide a readable explanation.

### 10.7 Pack Interaction

Packs may shape tone, domain framing, and approved behavior, but packs must not override memory scoping.

Rules:

- A selected pack must not expose another user's memory.
- A domain pack must not force irrelevant memory into the answer.
- Runtime/system packs must not be user-selectable memory scopes.
- User-selected content/domain packs may influence relevance only when the prompt is actually in that domain.

### 10.8 Action / MCP Interaction

For action-oriented prompts:

- memory must be scoped to the action
- low-confidence or conflicted memory must require clarification or approval
- sensitive write actions must not proceed from broad memory alone
- action trace must show which memory influenced the action decision

Example:

```text
User: Email Chad and tell him we are good.
Expected: clarify which Chad or draft for approval if memory is ambiguous.
```

### 10.9 Admin / Debug Behavior

Admin/debug views should expose:

- relevance score or tier
- included memory items
- excluded memory summary
- policy-blocked memory
- source families searched
- retrieval filters applied
- selected scope
- active user/tenant/project context

### 10.10 Governance Rules

- Relevance filtering must happen before model response generation.
- Irrelevant private memory must not be injected into prompts.
- Sensitive memory requires stricter relevance.
- Cross-user memory must be blocked by default.
- Cross-tenant memory must be blocked always unless an explicit governed sharing model exists.
- Historical memory requires historical intent or explicit user request.

### 10.11 Acceptance Tests

1. Work-only query
   - User asks "what do you know about my work life?"
   - Expected: work memory only.

2. Home-only query
   - User asks "what do you know about my home life?"
   - Expected: home memory only if permitted; work context omitted unless relevant.

3. General knowledge query
   - User asks "what is oceaneering?"
   - Expected: general knowledge route; personal memory not injected.

4. User isolation
   - User A has imported memory
   - User B asks the same memory question
   - Expected: User B receives only their own memory or empty state.

5. Action ambiguity
   - User says "send it"
   - Expected: clarification, no memory-driven guess.

6. Source-specific query
   - User asks "what did upload X say?"
   - Expected: memory restricted to that source if authorized.

### 10.12 Definition Of Done

Contextual Scope / Relevance Filtering is done when:

- memory injection is scoped before response generation
- ordinary factual answers do not include personal memory
- work/home/project/domain queries filter correctly
- included/excluded memory is traceable
- cross-user and cross-tenant leakage tests pass
- action prompts cannot execute from vague memory context

---

## 11. Cross-Attribute Behavior Matrix

| Situation | Reconciliation | Conflict | Active Context | Temporal | Relevance |
|---|---|---|---|---|---|
| Duplicate upload | No-op | None | No change | Source event recorded | Not injected twice |
| New current priority | Update/supersede | None unless disagreement | New priority active | Old priority historical | Included for planning/briefing |
| Two owners for same task | Same-slot conflict | Open conflict | Excluded or labeled | Both time-stamped | Included only when task relevant |
| Old project status | Superseded | None | Excluded from current | Historical | Included for "what changed" |
| User asks general fact | None | None | Not needed | Not relevant | Personal memory excluded |
| User asks "what do you know about me" | Prior reconciled memory used | Conflicts labeled | Active profile used | Current first | Broad but user-scoped |
| User asks "as of last month" | Prior states considered | Labeled if unresolved | Historical view | As-of filter | Time-scoped |
| User asks "send it" | None | Ambiguity likely | Not enough | Current memory insufficient | Clarify |

---

## 12. Required User Experience States

### 12.1 Memory Available

When memory is available and relevant:

```text
CWI answers with concise memory-backed context, confidence, and source/provenance.
```

### 12.2 No Memory

When no memory exists:

```text
I do not have stored memory about you yet. You can import chat history or connect an approved memory source.
```

### 12.3 Partial Memory

When memory exists but is incomplete:

```text
I have limited stored memory so far. Here is what I know...
```

### 12.4 Restricted Memory

When memory exists but cannot be accessed:

```text
I have stored information, but I am not permitted to use it in this context.
```

### 12.5 Conflicting Memory

When memory conflicts:

```text
I have conflicting memory about this. I should not treat either version as certain yet.
```

### 12.6 Stale Memory

When memory may be outdated:

```text
I have memory about this, but it may be stale. The latest source I have is from...
```

---

## 13. Trace And Provenance Requirements

Every memory-influenced response must be traceable.

Trace/debug views should include:

- memory used: yes/no
- memory intent
- active context used
- source families searched
- memory records included
- memory records excluded
- exclusion reasons
- conflict groups encountered
- temporal status
- confidence
- provenance IDs
- request ID
- trace ID

User-facing answers should remain concise. Detailed trace should be available through Glass Box/admin/debug views.

---

## 14. Admin And Observability Requirements

Authorized admin/debug surfaces should support:

- active memory viewer
- memory timeline
- reconciliation summary
- conflict queue
- relevance explanation
- source family list
- user/tenant isolation check
- memory activation status
- memory retrieval success/failure rate
- memory excluded/blocked counts
- stale memory alerts

No admin view should expose secrets, credentials, raw tokens, or unauthorized private content.

---

## 15. Security And Governance Requirements

Hard requirements:

- Memory is user-owned unless explicitly tenant/project scoped.
- Tenant boundaries are mandatory.
- Cross-user memory sharing is disabled unless explicitly governed.
- Sensitive memory must be minimized and relevance-gated.
- Raw imported chat history should not be injected wholesale.
- Secrets, passwords, keys, and tokens must not become memory.
- Memory must not override the current prompt.
- Memory must not override system/runtime policy.
- Memory used in actions must meet stricter confidence and approval rules.
- Deletion/purge workflows must preserve required audit events while honoring privacy obligations.

---

## 16. User Acceptance Test Suite

### 16.1 Memory Reconciliation

Prompt:

```text
what changed when new memory arrived?
```

Expected:

- added/updated/superseded/retracted/conflicted summary
- current active context summary
- trace/provenance

### 16.2 Conflict Resolution

Prompt:

```text
what do you do when memories contradict?
```

Expected:

- states conflict clearly
- does not choose unsupported truth
- asks clarification or points to admin resolution

### 16.3 Active Context

Prompt:

```text
what do you know about me?
```

Expected:

- uses current active memory for authenticated user
- excludes historical/superseded memory unless relevant
- includes confidence and trace

### 16.4 Temporal Awareness

Prompt:

```text
when was this true, and is it still true?
```

Expected:

- current vs historical distinction
- source timing
- stale/expired labels if applicable

### 16.5 Relevance Filtering

Prompt:

```text
which memories are relevant to this response?
```

Expected:

- answers with included memory categories
- does not dump all memory
- references exclusion/trace availability

### 16.6 User Isolation

Setup:

- User A imports targeted chat history.
- User B does not.

Prompts:

```text
what do you know about me?
what projects am I working on?
who are the key people in my network?
```

Expected:

- User A receives imported memory-backed answers.
- User B receives their own memory or empty state only.
- No leakage between users.

---

## 17. Global Acceptance Criteria

This memory capability is acceptable only when:

- duplicate memory is not duplicated in Active Context
- updated memory replaces old current memory while preserving history
- retracted memory is excluded from current answers
- conflicting memory is labeled, not guessed
- Active Context is current, scoped, and policy-allowed
- historical memory is not presented as current
- stale memory is labeled
- irrelevant memory is excluded before response generation
- user and tenant isolation tests pass
- response traces show memory use and provenance
- admin/debug views can explain memory decisions
- memory can be disabled safely without corrupting stored records

---

## 18. Definition Of Done

The five memory attributes are complete when:

1. Memory Reconciliation can classify and apply new/duplicate/additive/updated/superseded/retracted/deleted/conflicted memory.
2. Conflict Resolution can detect, expose, block unsafe use of, and resolve contradictory memory.
3. Active Context provides the current governed memory view used in normal answers.
4. Temporal Awareness can distinguish current, historical, stale, expired, future-effective, and superseded memory.
5. Contextual Scope / Relevance Filtering includes only memory that belongs in the current response.
6. All five attributes are visible in trace/debug output.
7. User-facing responses are concise, honest, and source-backed.
8. Admin/debug views can explain why memory was used, ignored, blocked, or superseded.
9. Automated and manual user acceptance tests pass.
10. Feature can be disabled safely through configuration.

---

## 19. Rollback / Disable Requirements

If memory behavior becomes unreliable:

- disable runtime memory retrieval
- keep stored memory records intact
- stop injecting memory into prompts
- keep import/reconciliation ledger intact
- show clear user-facing memory unavailable state
- preserve admin/debug access for investigation

Rollback must not delete memory unless an explicit retention/privacy workflow is invoked.

---

## 20. Open Questions

These should be resolved during implementation planning:

1. Which conflict types can users resolve directly versus requiring admin approval?
2. Which memory types should expire automatically?
3. Should users be able to edit Active Context in Phase 1 or only view/clear it?
4. How should relevance tiers be shown in Glass Box without overwhelming normal users?
5. What is the minimum acceptable memory trace for consumer-facing beta testing?
6. Should stale memory warnings be shown inline or only when memory materially affects the answer?
7. How should source authority be communicated to non-technical users?

---

## 21. Approval Gate

Decision requested:

```text
Approve or deny this five-attribute memory specification as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```

Once approved, implementation work should be planned against the five attributes independently so each one can be validated without relying on vague "memory works" testing.
