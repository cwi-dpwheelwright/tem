# CWI Comprehension And Expression Design Specification

**Document ID:** CWI-SPEC-COMP-EXPR-DESIGN-001  
**Related document:** `features/comprehension-expression/cwi_comprehension_expression_objectives_specification.md`  
**Version:** 1.0  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Draft for approval  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document defines the behavioral design for Context Awareness, Clarity Filtering, and Adaptive Tone.

It explains how these capabilities should fit into the CWI response pipeline, what decisions they are allowed to make, what decisions they must not override, and how the final response should be validated.

This document is requirements-focused and intentionally avoids implementation code.

---

## 2. Design Principle

Comprehension and expression are final-answer quality controls, not routing replacements.

They should improve the answer after the system has already determined:

- what the user is asking
- which response mode applies
- whether evidence, memory, or web retrieval is allowed or required
- whether MCP/tool execution is blocked, read-only, or approval-gated
- whether the answer must clarify instead of proceed

The expression layer must never make an unsafe or uncertain system sound confident.

---

## 3. Conceptual Runtime Placement

Required flow:

```text
normalize prompt
-> deterministic and identity checks
-> intent routing
-> response mode selection
-> memory/retrieval/tool policy
-> communication core
-> context awareness
-> clarity filtering
-> adaptive tone
-> response refinement
-> final answer with trace
```

Important:

- context awareness may enrich the response frame
- clarity filtering may simplify and remove ambiguity
- adaptive tone may adjust voice and phrasing
- none of these may change route, fabricate evidence, bypass policy, or execute tools

---

## 4. Context Awareness Design

### 4.1 Inputs

Context Awareness should evaluate:

- normalized prompt
- route and intent
- response mode
- selected pack
- authenticated user and tenant
- active conversation state
- memory availability and policy state
- retrieval state
- MCP/tool state
- trace metadata
- current date/time/timezone when relevant
- user-specified tone or format request

### 4.2 Context States

The system should classify context into one or more states:

| State | Meaning | Response Impact |
|---|---|---|
| `simple_direct` | User asks a straightforward question. | Answer directly. |
| `proactive_report` | User asks for briefing, risk, change, or plan. | Use structured proactive template. |
| `memory_profile` | User asks about themselves, projects, relationships, or history. | Use governed memory if available. |
| `current_or_latest` | User asks for current, latest, today, right now. | Use date/time and retrieval/tool rules. |
| `source_required` | User asks for sources or evidence. | Include source-backed answer or explain unavailable retrieval. |
| `action_candidate` | User asks CWI to do something. | Apply MCP/tool policy and approval gating. |
| `ambiguous` | The request lacks enough information. | Ask targeted clarification. |
| `sensitive` | The topic is privacy, safety, security, legal, financial, medical, or personal. | Use careful tone and explicit limitations. |

### 4.3 Design Rules

Context Awareness must:

- prefer the user's explicit current request over stored context
- keep memory user-scoped and tenant-scoped
- respect "do not use memory" or equivalent user instructions for the current answer
- distinguish no memory, partial memory, restricted memory, and irrelevant memory
- not over-interpret broad topic prompts as entity lookups
- not let enterprise/domain packs force policy fallback for general knowledge
- not trigger web/source use unless toggle, policy, currentness, or user request requires it

### 4.4 Output Contract

Context Awareness should produce an internal decision summary suitable for trace/debug:

- context state
- context inputs considered
- context inputs excluded
- reason for exclusion
- whether memory was used
- whether retrieval was used
- whether tool state influenced answer
- whether clarification was required

The user-facing answer should expose only useful context, not internal mechanics.

---

## 5. Clarity Filtering Design

### 5.1 Inputs

Clarity Filtering should evaluate the drafted answer after response mode selection.

It should consider:

- final answer draft
- response mode requirements
- prompt wording
- detected ambiguity
- evidence confidence
- required source display
- policy/approval status
- user's requested format

### 5.2 Clarity Checks

The final answer must pass these checks:

- Answer-first: the answer appears before explanation unless the mode requires otherwise.
- Mode-fit: the structure matches the selected response mode.
- No boilerplate: generic governance text is removed unless governance is the answer.
- No hidden ambiguity: assumptions and uncertainty are visible when material.
- No vague references: pronouns and vague entities are clarified when needed.
- No contradiction: the answer does not contain unresolved internal disagreement.
- No overlong preamble: background does not obscure the answer.
- Next action visible: when a next action is required, it is easy to find.
- Source readability: citations and source groups are readable and not noisy.

### 5.3 Ambiguity Handling

Clarity Filtering must not erase ambiguity by pretending the answer is known.

If ambiguity remains:

- ask the smallest useful clarification question
- preserve any safe partial answer if helpful
- do not execute tools or actions
- do not choose between conflicting identities, recipients, files, dates, or instructions without authority

### 5.4 Response Mode Guardrails

Clarity Filtering must preserve these mode shapes:

| Response Mode | Required Shape |
|---|---|
| Direct answer | Brief answer first. |
| Risk | 1 to 2 risks, impact, next action. |
| Daily briefing | Top 3 to 5 priorities, 1 risk, 1 recommendation. |
| What changed | Clear change summary, relevant deltas, source/trace. |
| Plan | Steps, owner or next action, constraints. |
| Summary | Condensed explanation of supplied material. |
| Code | Complete runnable answer for basic requests. |
| Clarification | One targeted question, optional safe context. |
| Blocked action | Clear reason, safe alternative if available. |

---

## 6. Adaptive Tone Design

### 6.1 Inputs

Adaptive Tone should evaluate:

- explicit tone request
- user role or audience if stated
- response mode
- urgency
- sensitivity
- technical complexity
- user frustration or confusion
- prior user preference when governed memory allows it

### 6.2 Tone Profiles

Supported tone profiles:

| Tone Profile | Use When | Characteristics |
|---|---|---|
| `default_helpful` | Normal user requests. | Concise, warm, direct. |
| `executive` | Status, approval, CEO/email-ready updates. | Crisp, decision-oriented, minimal filler. |
| `technical` | Code, architecture, debugging, implementation. | Precise, concrete, testable. |
| `plain_language` | User asks for simpler explanation. | Lower jargon, shorter sentences, clear examples. |
| `urgent` | Incident, risk, blocked deployment, safety concern. | Direct, prioritized, action-oriented. |
| `sensitive` | Privacy, security, personal, regulated topics. | Careful, explicit, non-casual. |
| `coaching` | User is learning or exploring. | Supportive, explanatory, non-condescending. |

### 6.3 Tone Boundaries

Adaptive Tone must not:

- change factual claims
- hide uncertainty
- soften policy blocks
- imply approval where approval is required
- add emotional claims the system cannot know
- use personal memory for style unless allowed and relevant
- produce overly casual language for high-risk subjects
- make executive summaries too thin to be useful

### 6.4 Tone Conflict Rules

When tone requests conflict with safety or clarity:

- safety wins over tone
- response mode wins over tone
- clarity wins over style
- explicit user tone wins over inferred tone when safe
- sensitive-topic tone wins over casual tone

Example:

```text
User asks: "Make this sound casual and tell me whether I can expose the API token."
Expected: careful safety-oriented answer, not casual approval.
```

---

## 7. Interaction With Packs

Packs may influence domain vocabulary, preferred framing, and helpful context only when relevant.

Packs must not:

- override route priority
- force policy-style output for direct questions
- change required proactive templates
- change code completeness requirements
- change memory ownership
- change tool approval requirements
- change source/retrieval truth

If a pack tries to force expression behavior that conflicts with this design, the response mode and system governance win.

---

## 8. Interaction With Memory

Memory may influence context and tone only when:

- the authenticated user owns or is permitted to access the memory
- the memory is active, relevant, current enough, and not conflicted
- memory policy allows use
- the current prompt benefits from memory

Memory must not be used:

- for unrelated general knowledge prompts
- to guess vague action targets
- to identify people without validated entity context
- to personalize sensitive answers unnecessarily
- across users, tenants, browsers, or sessions

Trace/debug should distinguish:

- memory used
- memory skipped as not relevant
- memory unavailable
- memory restricted
- memory conflicted
- memory stale

---

## 9. Interaction With Web And Sources

The Use Web toggle and trusted source retrieval are evidence features, not tone features.

Comprehension and expression should:

- incorporate retrieved source findings naturally into the answer
- cite sources in readable grouped form
- avoid breaking the answer into awkward source-by-source sections unless the user asks for comparison
- distinguish retrieval off, skipped, unavailable, zero results, and used
- not claim web access was unavailable when trusted retrieval was actually used

When multiple sources are cited, the answer should still read as one coherent response. Source indicators should support the answer, not fragment it.

---

## 10. Interaction With MCP And Actions

For tool/action prompts:

- Context Awareness identifies action candidates.
- Clarity Filtering prevents vague execution.
- Adaptive Tone keeps blocked/approval-required responses clear and respectful.

The expression layer must not execute tools or decide approval.

Action responses should clearly state one of:

- executed
- available read-only result
- requires approval
- blocked by policy
- clarification required
- tool unavailable

---

## 11. Trace And Debug Requirements

Authorized trace/debug views should expose:

- context state
- response mode
- clarity filter outcome
- tone profile
- memory use state
- retrieval use state
- source grouping
- MCP/tool policy state
- final response template used
- reason codes for skipped context, skipped memory, and skipped retrieval

User-facing answers should not show internal labels unless the UI/debug surface is open.

---

## 12. Failure Behavior

If Context Awareness fails:

- fall back to route and response mode
- do not inject memory
- answer conservatively

If Clarity Filtering fails:

- preserve original response mode output
- do not drop required content
- mark trace/debug warning

If Adaptive Tone fails:

- use default helpful tone
- preserve safety, source, and policy language

If any refinement layer is unavailable:

- final answer should still be correct, governed, and traceable
- degraded mode should be visible in debug/admin views

---

## 13. Refinement Addendum - Shared Reason Codes And Quality Gates

This review identified a traceability gap: each layer had behavior, but the design needed shared reason codes so engineering, QA, and support can explain why an answer sounded the way it did.

### 13.1 Required Reason Codes

Context Awareness should emit one or more of:

- `context_simple_direct`
- `context_proactive_report`
- `context_memory_profile`
- `context_current_or_latest`
- `context_source_required`
- `context_action_candidate`
- `context_ambiguous`
- `context_sensitive`
- `context_skipped_not_relevant`
- `context_skipped_restricted`
- `context_skipped_unavailable`

Clarity Filtering should emit one or more of:

- `clarity_answer_first`
- `clarity_mode_shape_enforced`
- `clarity_boilerplate_removed`
- `clarity_ambiguity_preserved`
- `clarity_clarification_required`
- `clarity_warning_preserved`
- `clarity_sources_grouped`
- `clarity_no_change_needed`
- `clarity_degraded`

Adaptive Tone should emit one or more of:

- `tone_default_helpful`
- `tone_executive`
- `tone_technical`
- `tone_plain_language`
- `tone_urgent`
- `tone_sensitive`
- `tone_coaching`
- `tone_explicit_user_request`
- `tone_memory_preference_used`
- `tone_memory_preference_skipped`
- `tone_safety_override`
- `tone_degraded_default`

### 13.2 Quality Gates

Before final output, the response should pass these gates:

- Route Gate: final answer still matches selected route and response mode.
- Evidence Gate: source-backed claims remain tied to available sources.
- Memory Gate: no unauthorized memory appears in answer or tone.
- Policy Gate: blocked, approval-required, and clarification-required states remain explicit.
- Clarity Gate: final answer is readable and free of avoidable ambiguity.
- Tone Gate: style does not alter meaning or risk severity.
- UX Gate: answer text is visible without relying on debug panels.

If any gate fails, the system should fall back to the safest available response mode output and mark the degraded state in trace/debug.

---

## 14. Acceptance Criteria

Design is accepted when:

- each capability has defined inputs, decisions, boundaries, and trace output
- response modes remain dominant
- packs cannot override expression rules
- memory cannot leak through context or tone
- web/source use remains toggle/policy driven
- action execution remains policy/approval gated
- general knowledge, summary, code, proactive, memory, source-backed, and blocked-action prompts all have expected expression behavior

---

## 15. Approval Gate

Decision requested:

```text
Approve or deny the Comprehension And Expression design as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
