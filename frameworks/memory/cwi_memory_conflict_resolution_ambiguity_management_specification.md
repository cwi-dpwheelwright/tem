# CWI Memory Conflict Resolution / Ambiguity Management Specification

**Document ID:** CWI-SPEC-MEM-CONFLICT-001  
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

Conflict Resolution / Ambiguity Management defines how CWI handles memory sources that disagree when no safe single truth exists yet.

Core question:

```text
What do we do when memories contradict?
```

This specification ensures CWI does not silently pick a convenient answer, merge incompatible facts, or use conflicted memory for unsafe responses or tool execution.

---

## 2. Product Principle

CWI must be honest about uncertainty.

When memory contradicts:

- CWI must detect the contradiction
- CWI must label unresolved conflict
- CWI must avoid pretending one claim is certain
- CWI must ask for clarification when needed
- CWI must require appropriate authority to resolve scoped conflicts
- CWI must preserve competing claims and provenance

The product promise is:

```text
CWI knows when memory disagrees and will not invent certainty.
```

---

## 3. Scope

### 3.1 In Scope

This specification applies to conflicts involving:

- user identity memory
- user preferences
- project status
- project ownership
- collaborators/relationships
- open loops and tasks
- source-derived facts
- connector facts
- approved pack facts
- action-relevant memory
- temporal contradictions

### 3.2 Out Of Scope

This specification does not define:

- exact conflict scoring code
- database schema
- UI component design
- natural-language model prompts
- external approval workflow mechanics

---

## 4. Conflict Detection

CWI must open a conflict when:

- two or more memory claims occupy the same or equivalent slot
- the claims assert incompatible values
- both claims are above the minimum confidence threshold
- both claims are policy-eligible to be considered
- no source authority rule clearly selects a winner

Examples:

| Claim A | Claim B | Expected |
|---|---|---|
| Project is paused | Project is active | Open conflict |
| Chad owns dashboards | Christian owns dashboards | Open conflict or clarify ownership scope |
| User prefers email summaries | User prefers no email summaries | Latest explicit user correction may win |
| Meeting is Tuesday | Meeting is Wednesday | Conflict unless calendar source is authoritative |

---

## 5. Conflict States

| State | Meaning |
|---|---|
| `open` | Conflict exists and no winner is selected. |
| `needs_user_clarification` | Authenticated user can resolve or narrow it. |
| `needs_admin_review` | Requires authorized tenant/platform operator. |
| `policy_blocked` | Conflict cannot be resolved or used because policy blocks it. |
| `resolved` | A winner or scoped interpretation has been selected. |
| `historical_only` | Conflict retained for audit/history but excluded from current active context. |

---

## 6. Required Runtime Behavior

When a conflict is detected, CWI must:

1. Create or update a conflict group.
2. Link competing memory records.
3. Preserve source/provenance for each claim.
4. Determine whether the conflict blocks active context.
5. Determine whether user clarification is enough.
6. Determine whether admin/operator review is required.
7. Exclude or label conflicted claims in current answers.
8. Block unsafe action execution that depends on unresolved conflict.
9. Emit trace/ledger event.

---

## 7. User-Facing Behavior

If conflicted memory is relevant, CWI should say so clearly.

Example:

```text
I have conflicting memory about this.

One source says Chad owns the dashboard work.
Another source says Christian owns final dashboard validation.

I should not treat either as the only current truth yet. I can answer from non-conflicting project context, or you can clarify who owns which part.
```

If the conflict blocks the answer:

```text
I need clarification before I can answer safely. I have conflicting memory about which project is active.
```

If the conflict does not block the answer:

```text
I can answer the non-conflicting part. One ownership detail is still unresolved.
```

---

## 8. Response Rules

CWI must not:

- fabricate a winner
- hide relevant conflict only in trace
- use one side of conflict for action execution
- present both sides as simultaneously true if they are incompatible
- resolve conflict across the wrong user, tenant, project, or domain scope
- expose sensitive competing claims to unauthorized users

CWI should:

- state conflict plainly
- list competing claims when allowed
- ask a targeted clarification question
- continue with safe non-conflicting context when possible
- cite source/provenance categories
- show confidence limitations

---

## 9. Resolution Authority

Conflicts can be resolved by:

- authenticated user for their own user-owned memory
- tenant admin for tenant-scoped memory
- operator/admin for platform/governance memory
- approved authority rule
- signed pack/pin authority
- explicit source correction
- approved workflow/ticket

Resolution must record:

- resolver identity or system authority
- resolution type
- winning claim or merged interpretation
- reason
- timestamp
- scope
- trace ID
- ledger event

---

## 10. Conflict Resolution Outcomes

| Outcome | Meaning |
|---|---|
| `select_claim` | One claim becomes active. |
| `merge_claims` | Claims are compatible when scoped or combined. |
| `split_scope` | Claims apply to different projects/domains/time windows. |
| `mark_historical` | One or more claims are retained only historically. |
| `retract_claim` | A claim is withdrawn. |
| `needs_more_evidence` | No safe resolution yet. |
| `policy_block` | Cannot resolve/use due to policy. |

---

## 11. Admin / Debug Requirements

Authorized admin/debug views should show:

- open conflict count
- conflict severity
- affected user/tenant/project/domain
- competing claims
- source summaries
- confidence
- authority class
- recommended resolution
- blocked answers/actions
- resolution history
- trace and ledger links

Sensitive content must be redacted unless the viewer is authorized.

---

## 12. Governance Rules

Hard rules:

- unresolved conflicts must not be silently resolved
- conflicted memory must not drive write/sensitive actions
- conflict resolution must be scoped
- conflict resolution must be auditable
- sensitive conflicts must not leak private facts
- user-owned memory conflicts should be user-resolvable when safe
- tenant/platform memory conflicts require appropriate authority

---

## 13. Acceptance Tests

### 13.1 Direct Contradiction

Setup:

- Source A says project status is "paused"
- Source B says project status is "active"

Expected:

- conflict group opens
- no silent winner
- current answer labels conflict or asks clarification

### 13.2 Authority Winner

Setup:

- older user preference says "short answers"
- authenticated user now says "use detailed answers for specs"

Expected:

- explicit correction wins within scope
- old memory becomes historical
- trace shows why

### 13.3 Ambiguous Person

Prompt:

```text
call john
```

Setup:

- memory contains multiple people named John

Expected:

- clarification
- no execution

### 13.4 Sensitive Conflict

Setup:

- conflict includes sensitive personal information

Expected:

- response acknowledges restriction without exposing sensitive details
- admin/debug access respects authorization

### 13.5 Resolved Conflict

Setup:

- admin resolves competing ownership claims

Expected:

- active context uses resolved value
- trace shows resolution provenance
- historical claims remain inspectable

---

## 14. Definition Of Done

Conflict Resolution / Ambiguity Management is complete when:

- same-slot contradictions are detected
- unresolved conflicts are visible and traceable
- CWI does not invent certainty
- users can clarify user-owned conflicts
- authorized admins can resolve scoped conflicts
- conflicted memory is excluded from unsafe answers/actions
- sensitive conflicts are redacted appropriately
- resolution history is auditable
- tests prove no silent conflict winner

---

## 15. Rollback / Disable Requirements

If conflict handling is unreliable:

- disable conflicted memory from active context
- require clarification for affected answers
- block action execution from conflicted memory
- preserve conflict groups for investigation
- show admin-visible warning

Rollback must not delete competing memory claims.

---

## 16. Refinement Addendum - Conflict Classes And Safe Partial Answers

This review identified a gap between ambiguity and contradiction. The runtime must not treat every unclear input as a memory conflict, and it must not hide real contradictions behind generic clarification.

Required additions:

- distinguish ambiguous input, ambiguous entity, stale memory, policy-restricted memory, and direct contradiction
- direct contradictions must form a conflict group
- ambiguous input should ask a clarification question without creating a conflict group unless competing memory claims already exist
- sensitive conflicts must be redacted in user-facing answers and fully visible only to authorized debug/admin roles
- safe partial answers are allowed when the non-conflicted facts are useful and the conflict is clearly stated
- action execution must be blocked when the action depends on unresolved conflicted memory
- resolved conflicts must keep the rejected claim historically inspectable with resolution provenance

Conflict severity classes:

| Severity | Meaning | Required Behavior |
|---|---|---|
| Low | Non-actionable preference or wording mismatch. | Explain uncertainty or ask preference. |
| Medium | Affects answer quality or current context. | Do not choose silently; ask or show conflict. |
| High | Affects people, responsibilities, money, health, security, or external action. | Block action and require explicit resolution. |
| Critical | Could leak sensitive data, bypass policy, or execute unsafe action. | Fail closed and escalate to authorized review. |

Minimum acceptance coverage:

- "call john" asks clarification when multiple Johns exist
- contradictory project owner memory does not silently choose a winner
- safe non-conflicted facts can still be answered
- unresolved high-severity conflict blocks action execution
- resolved conflict shows why the selected claim is active

---

## 17. Approval Gate

Decision requested:

```text
Approve or deny Conflict Resolution / Ambiguity Management as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
