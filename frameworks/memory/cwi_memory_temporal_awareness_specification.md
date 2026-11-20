# CWI Memory Temporal Awareness / Time-Based Context Specification

**Document ID:** CWI-SPEC-MEM-TEMPORAL-001  
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

Temporal Awareness / Time-Based Context tracks when memories were true, changed, expired, or became historical.

Core question:

```text
When was this true, and is it still true?
```

This specification ensures CWI can distinguish current memory from historical memory and can answer questions about when context changed.

---

## 2. Product Principle

Memory without time awareness becomes stale context.

CWI must know:

- when a source was created
- when CWI received it
- when a memory became valid
- when it stopped being valid
- when CWI learned about a change
- whether it is current, historical, stale, expired, future-effective, or superseded

The product promise is:

```text
CWI can explain when memory was true and whether it is still true now.
```

---

## 3. Required Temporal Concepts

Temporal fields must distinguish:

| Concept | Meaning |
|---|---|
| `source_created_at` | When the original source says it was created. |
| `observed_at` | When CWI observed or received the source. |
| `valid_from` | When the memory became true/applicable. |
| `valid_to` | When the memory stopped being true/applicable. |
| `learned_at` | When CWI learned or inferred the memory. |
| `superseded_at` | When a newer memory replaced it. |
| `expired_at` | When policy says memory is no longer current enough. |

These must not be collapsed into a single timestamp.

---

## 4. Temporal States

| State | Meaning | Active Context behavior |
|---|---|---|
| `current` | Valid now. | Eligible. |
| `future_effective` | Known now but active later. | Not active yet. |
| `historical` | Was true/asserted in past. | Excluded unless historical intent. |
| `superseded` | Replaced by newer active memory. | Excluded from current answers. |
| `expired` | No longer valid after expiry. | Excluded unless historical. |
| `stale_unverified` | Too old for confident current use. | Warn, refresh, or exclude by policy. |
| `time_uncertain` | Time bounds unknown. | Use cautiously and label uncertainty. |

---

## 5. Time-Aware Question Types

| User question | Expected behavior |
|---|---|
| "What is true now?" | Use current active memory. |
| "What changed?" | Compare current and prior memory. |
| "What did I believe before?" | Use historical memory with labels. |
| "As of last week..." | Use memory valid during that time window. |
| "Is this still true?" | Check current status and latest source. |
| "When did this change?" | Use reconciliation lineage and source timing. |

---

## 6. Required Runtime Behavior

When memory is used, CWI must:

1. Determine the temporal intent of the prompt.
2. Determine current date/time and user timezone.
3. Filter memory by valid time for current-state answers.
4. Include historical memory only for historical/as-of/change questions.
5. Label stale or uncertain memory.
6. Prevent expired memory from driving current answers/actions.
7. Preserve memory timeline in trace/debug.

---

## 7. User-Facing Behavior

### 7.1 Current State

```text
As of the latest approved memory, the project is active.
```

### 7.2 Superseded Memory

```text
Previously, it was marked as paused in an older import. That older status was superseded when the newer project update arrived.
```

### 7.3 Stale Memory

```text
I have memory about this, but it may be stale. The latest source I have is from March 2026.
```

### 7.4 Historical Lookup

```text
At that time, the active priority was MCP proof mode. It later changed to memory activation validation.
```

### 7.5 Time Uncertainty

```text
I found memory about this, but the source does not clearly say when it became true.
```

---

## 8. Expiration And Staleness Policy

Some memory types should expire or become stale quickly:

- meetings
- temporary tasks
- short-lived operational status
- weather/current event summaries
- connector sync snapshots
- pending approval state

Some memory types may remain durable:

- stable user preferences
- long-lived project relationships
- confirmed identity profile
- historical decisions
- governance receipts

Temporal behavior must be determined by memory type and policy, not one global TTL.

---

## 9. Action / MCP Rules

For actions:

- time-sensitive memory must be current enough
- stale memory should require refresh, clarification, or approval
- historical memory must not drive current action execution
- conflicted temporal memory must block or clarify

Example:

```text
User: Email Chad about today's meeting.
Expected: CWI must verify current meeting context rather than use stale memory.
```

---

## 10. Admin / Debug Requirements

Authorized views should show:

- valid-from time
- valid-to time
- observed-at time
- imported-at time
- learned-at time
- superseded-at time
- expiry/staleness policy
- current/historical/stale status
- timeline of changes
- source lineage
- trace and ledger events

---

## 11. Governance Rules

Hard rules:

- historical memory must not be presented as current
- stale memory must be labeled when used
- expired memory must not be used in current answers unless historical intent exists
- time-sensitive memory must not drive actions without current verification
- user timezone and current request date must be considered
- temporal lineage must be auditable

---

## 12. Acceptance Tests

### 12.1 Superseded Timeline

Setup:

- old memory says task status is "open"
- new memory says task status is "closed"

Prompt:

```text
when did this change?
```

Expected:

- old status
- new status
- change timing/source
- active current status

### 12.2 Current-Only Answer

Prompt:

```text
what is the current status?
```

Expected:

- current memory only
- historical memory excluded unless needed as context

### 12.3 Historical Answer

Prompt:

```text
what was true before?
```

Expected:

- historical memory included with labels

### 12.4 Stale Memory Warning

Setup:

- latest memory is older than policy permits

Expected:

- answer labels staleness or asks to refresh

### 12.5 Future Effective Memory

Setup:

- memory says preference changes next month

Expected:

- current answer does not apply future preference prematurely

---

## 13. Definition Of Done

Temporal Awareness is complete when:

- current and historical memory are distinct
- answers can explain when memory changed
- stale/expired memory is not silently treated as current
- as-of queries work
- future-effective memory does not apply prematurely
- time-sensitive actions require current enough memory
- traces include temporal basis for memory use
- admin/debug timeline is available

---

## 14. Rollback / Disable Requirements

If temporal handling becomes unreliable:

- default memory retrieval to current-only verified records
- block stale time-sensitive memory from actions
- label uncertain memory
- preserve timestamps and timeline data for investigation

---

## 15. Refinement Addendum - Time Authority And Current-Date Handling

This review identified a recurring CWI risk: stale date assumptions and relative-time confusion. Temporal memory must depend on an explicit runtime clock and source timestamps rather than model assumptions.

Required additions:

- every runtime memory evaluation must receive current date, time, and timezone
- relative phrases such as today, yesterday, tomorrow, last night, next week, current, latest, and right now must be normalized before memory retrieval
- imported_at is not automatically the event time
- source_created_at, source_observed_at, effective_from, effective_until, superseded_at, retracted_at, and activated_at must be distinguished when available
- if real-world event time is unknown, the answer must say the timing is based on import or activation evidence
- future-effective memory must not become active early
- expired memory must not be used as current unless the user asks for history
- time-sensitive actions require memory freshness thresholds appropriate to the domain

Minimum acceptance coverage:

- "what changed today?" uses the user's timezone and current date
- "what was true before?" returns historical memory with time labels
- "what is current right now?" excludes future-effective and expired memory
- a memory imported today but describing last year is not treated as a new real-world event
- trace shows the temporal basis for the answer

---

## 16. Approval Gate

Decision requested:

```text
Approve or deny Temporal Awareness / Time-Based Context as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
