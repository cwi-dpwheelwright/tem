# CWI Memory Contextual Scope / Relevance Filtering Specification

**Document ID:** CWI-SPEC-MEM-RELEVANCE-001  
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

Contextual Scope / Relevance Filtering defines how CWI selects only the memories relevant to the current task, user, domain, or situation.

Core question:

```text
Which memories belong in this response?
```

This specification prevents CWI from dumping unrelated memory into answers or injecting private context where it does not belong.

---

## 2. Product Principle

Memory should be helpful, not noisy or invasive.

CWI must include memory only when it is:

- relevant
- current enough
- policy-allowed
- scoped to the authenticated user/tenant
- appropriate for the prompt intent
- not blocked by sensitivity, conflict, or temporal rules

The product promise is:

```text
CWI uses the right memory for the right response and leaves the rest out.
```

---

## 3. Required Filtering Dimensions

CWI must filter memory by:

- authenticated user
- tenant
- active project/workspace
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

Filtering must happen before response generation.

---

## 4. Scope Types

| Scope type | Example |
|---|---|
| `user_private` | User preferences, imported personal chat history |
| `tenant` | Tenant-wide policies, approved shared documents |
| `project` | CWI MCP workstream, dashboard project |
| `domain` | Work, home, finance, operations, health |
| `conversation` | Current chat context |
| `source_specific` | A named upload or document |
| `connector_specific` | Smartsheet, Nextcloud, email, calendar |
| `time_window` | Last 24 hours, this week, as of a date |
| `action_scope` | Read-only vs write/sensitive action |

---

## 5. Relevance Tiers

| Tier | Meaning | Response behavior |
|---|---|---|
| `required` | Needed to answer correctly. | Include if allowed. |
| `supporting` | Helps answer. | Include briefly if useful. |
| `background` | Useful for continuity/tone. | Include sparingly. |
| `excluded` | Not relevant enough. | Do not inject. |
| `blocked` | Relevant but not allowed. | Do not inject; trace reason. |

---

## 6. Required Runtime Behavior

For every prompt, CWI must:

1. Determine prompt intent.
2. Determine user/tenant/session context.
3. Determine project/domain/source scope if present.
4. Retrieve candidate memory only within eligible scope.
5. Score or classify relevance.
6. Exclude stale, superseded, retracted, conflicted, or blocked memory.
7. Include only required/supporting memory needed for the response.
8. Record included and excluded memory summary in trace/debug.

---

## 7. User-Facing Behavior

### 7.1 Broad Memory Query

Prompt:

```text
what do you know about me?
```

Expected:

- concise categorized summary
- current active user memory
- no unrelated raw transcript dump

### 7.2 Work-Only Query

Prompt:

```text
what do you know about my work life?
```

Expected:

- work memory only
- home/personal memory omitted unless directly relevant and permitted

### 7.3 Home-Only Query

Prompt:

```text
what do you know about my home life?
```

Expected:

- home/personal context only if permitted
- work context omitted unless directly relevant

### 7.4 General Knowledge Query

Prompt:

```text
what is the capital of Texas?
```

Expected:

- direct answer
- personal memory not injected

### 7.5 Source-Specific Query

Prompt:

```text
what did the latest chat history upload say about my projects?
```

Expected:

- restrict memory to that source family/version when authorized

---

## 8. Exclusion Transparency

Normal answers should not list every excluded memory.

Trace/debug should show:

- memory searched
- memory included
- memory excluded
- exclusion reasons
- blocked memory count
- stale/historical memory count
- conflict-blocked memory count

If user asks "why did you use that memory?" CWI should provide a readable explanation.

---

## 9. Pack Interaction

Packs may influence tone, domain framing, and approved behavior only when relevant.

Rules:

- selected pack must not expose another user's memory
- domain pack must not force irrelevant memory into answer
- runtime/system packs must not become user-selectable memory scopes
- content/domain packs may influence relevance only when prompt is in that domain
- memory scope rules outrank pack styling

---

## 10. Action / MCP Interaction

For action-oriented prompts:

- memory must be scoped to the action
- vague memory is insufficient for execution
- low-confidence or conflicted memory requires clarification or approval
- sensitive write actions must not proceed from broad memory alone
- action trace must show memory used in execution decision

Example:

```text
User: Email Chad and tell him we are good.
Expected: clarify which Chad or draft for approval if memory is ambiguous.
```

---

## 11. Admin / Debug Requirements

Authorized views should expose:

- relevance tier
- included memory items
- excluded memory summary
- blocked memory summary
- source families searched
- retrieval filters applied
- selected scope
- active user/tenant/project context
- sensitivity and policy filters
- trace IDs

---

## 12. Governance Rules

Hard rules:

- relevance filtering must happen before model response generation
- irrelevant private memory must not be injected
- sensitive memory requires stricter relevance
- cross-user memory is blocked by default
- cross-tenant memory is blocked unless explicitly governed
- historical memory requires historical intent or explicit user request
- memory must not override current prompt intent
- memory must not override system/runtime policy

---

## 13. Acceptance Tests

### 13.1 Work-Only Query

Prompt:

```text
what do you know about my work life?
```

Expected:

- work memory only
- personal/home memory excluded

### 13.2 Home-Only Query

Prompt:

```text
what do you know about my home life?
```

Expected:

- home memory only if permitted
- work context omitted unless relevant

### 13.3 General Knowledge Query

Prompt:

```text
what is oceaneering?
```

Expected:

- general knowledge route
- personal memory not injected

### 13.4 User Isolation

Setup:

- User A has imported memory
- User B has no memory

Prompt:

```text
what projects am I working on?
```

Expected:

- User B receives own memory or empty state only
- no leakage

### 13.5 Action Ambiguity

Prompt:

```text
send it
```

Expected:

- clarification
- no memory-driven guess
- no execution

### 13.6 Source-Specific Query

Prompt:

```text
what did upload X say?
```

Expected:

- source-restricted answer if authorized
- no unrelated memory

---

## 14. Definition Of Done

Contextual Scope / Relevance Filtering is complete when:

- memory injection is scoped before response generation
- ordinary factual answers do not include personal memory
- work/home/project/domain queries filter correctly
- included/excluded memory is traceable
- cross-user and cross-tenant leakage tests pass
- action prompts cannot execute from vague memory context
- packs cannot override memory scope
- feature can be disabled safely

---

## 15. Rollback / Disable Requirements

If relevance filtering becomes unreliable:

- disable memory injection into prompts
- continue storing/reconciling memory if safe
- answer without memory unless explicitly source-specific and safe
- show admin warning
- preserve trace/debug data for investigation

---

## 16. Refinement Addendum - Relevance Gates And Non-Memory Prompts

This review identified a risk that memory retrieval could be over-applied to ordinary prompts. Relevance filtering must protect simple questions, public knowledge, code, summaries, and proactive response templates from unnecessary personal-memory injection.

Required additions:

- general knowledge prompts should not use personal memory unless the user asks for personal context
- code generation should not use personal memory unless the code task references the user's own project, repo, or prior work
- summarization should summarize the provided material first; memory is only included when the user asks for personalized interpretation
- proactive modes may use memory only after scope and freshness checks
- MCP/tool prompts may use memory for disambiguation, but vague action prompts still require clarification
- relevance should be explainable through reason codes such as direct_match, domain_match, source_requested, excluded_general_knowledge, excluded_low_relevance, excluded_policy, excluded_conflict, and excluded_stale
- memory injection should be capped to the smallest set needed for the response
- sources in the UI should group memory sources by type and count rather than exposing noisy raw source lists

Minimum acceptance coverage:

- "what is the capital of Texas?" answers directly without personal memory
- "what have I been working on?" retrieves user memory
- "summarize this text" does not add unrelated user history
- "make a plan for my CWI memory rollout" can use relevant CWI memory
- "send it" asks clarification and does not infer recipient from memory alone
- source display groups memory sources cleanly

---

## 17. Approval Gate

Decision requested:

```text
Approve or deny Contextual Scope / Relevance Filtering as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
