# CWI Comprehension And Expression Review And Gap Analysis

**Document ID:** CWI-SPEC-COMP-EXPR-REVIEW-GAPS-001  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Review artifact  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document records the review of the Comprehension And Expression specification set.

The goal is to make sure Context Awareness, Clarity Filtering, and Adaptive Tone are specified as durable runtime capabilities rather than short-term answer-polishing instructions.

---

## 2. Files Reviewed

- `cwi_comprehension_expression_objectives_specification.md`
- `cwi_comprehension_expression_design_specification.md`
- `cwi_comprehension_expression_implementation_specification.md`

---

## 3. Overall Assessment

The three documents are strong as a first specification set. They correctly position Context Awareness, Clarity Filtering, and Adaptive Tone as refinement layers after routing and response mode selection.

The original set had three main gaps:

- the boundaries between expression layers and routing were implied but not strict enough
- trace/debug output needed shared reason codes
- release evidence needed to require rendered answer validation, not only backend pass/fail

Those gaps have been refined in the underlying specifications.

---

## 4. Refinements Applied

### 4.1 Objectives Specification

Added a refinement section clarifying that:

- Context Awareness must not invent context
- Clarity Filtering must not remove required uncertainty or warnings
- Adaptive Tone must not change facts, confidence, risk, policy, or action availability
- none of the layers may trigger MCP/tool execution
- memory and tone cannot cross authenticated user or tenant boundaries

### 4.2 Design Specification

Added shared reason codes for:

- context state
- clarity decision
- tone profile
- skipped/degraded behavior

Also added final-output quality gates:

- route gate
- evidence gate
- memory gate
- policy gate
- clarity gate
- tone gate
- UX gate

### 4.3 Implementation Specification

Added release evidence requirements so validation must capture:

- prompt
- user/role
- selected pack
- Use Web state
- route/intent
- response mode
- context state
- clarity decision
- tone profile
- memory/retrieval state
- trace ID
- visible rendered answer
- pass/fail result

This prevents a deploy from being considered valid when only API health passes.

---

## 5. Remaining Gaps And Product Decisions

### 5.1 Persistent Tone Preference

Decision needed:

```text
Should a user's preferred tone persist across sessions?
```

Recommendation:

- Phase 1 should support explicit per-request tone only.
- Persistent tone should wait until memory governance and user controls are complete.

### 5.2 Tone Preference Storage

Decision needed:

```text
If persistent tone is approved later, should it be stored in memory, profile settings, tenant settings, or session state?
```

Recommendation:

- session state for temporary preference
- user profile setting for explicit durable preference
- governed memory only if the user explicitly approves it as memory

### 5.3 User-Visible Tone Indicator

Decision needed:

```text
Should users see the selected tone profile?
```

Recommendation:

- ordinary users should not see internal tone labels by default
- admin/debug views should show tone profile and reason code
- user-facing UI may show tone only if the user explicitly opens trace/debug

### 5.4 Compression Boundaries

Decision needed:

```text
How concise can clarity filtering make answers before it removes useful detail?
```

Recommendation:

- response mode should set the compression boundary
- user-requested brevity can compress more
- safety, approval, uncertainty, source limits, and next actions must never be compressed away

### 5.5 Interplay With Response Templates

Decision needed:

```text
Which response templates are allowed to be restyled, and which must remain exact?
```

Recommendation:

- risk, daily briefing, blocked action, clarification, and approval-required templates should preserve exact structure
- factual, explanatory, summary, and planning answers can be restyled within mode boundaries

### 5.6 Sensitive Tone Escalation

Decision needed:

```text
Which prompts automatically force sensitive tone?
```

Recommendation:

- privacy
- credentials/secrets
- security
- safety
- legal/financial/medical subjects
- personal memory
- external write actions
- customer or tenant data

### 5.7 Debug Role Visibility

Decision needed:

```text
Which roles can inspect context, clarity, and tone decisions?
```

Recommendation:

- authenticated user: user-facing trace summary only
- tenant admin: scoped operational trace without raw private memory
- platform admin: operational trace under authorized controls
- auditor: metadata and ledger trace, not raw private content by default

---

## 6. Cross-Spec Acceptance Criteria

The feature set is ready only when:

- simple questions remain direct across all packs
- summaries summarize user-provided text
- code prompts produce complete answers
- proactive modes preserve required templates
- ambiguous actions ask clarification
- blocked actions remain blocked
- sensitive prompts use careful tone
- explicit tone requests are honored when safe
- memory-backed tone never leaks across users
- source-backed answers remain coherent and cite grouped sources
- trace/debug shows context, clarity, and tone reason codes
- rendered UI output is verified, not only API output

---

## 7. Recommended Next Work Items

1. Convert the validation matrix into a smoke-test checklist.
2. Define the trace/debug field names for context, clarity, and tone decisions.
3. Decide whether persistent tone preferences are in or out of Phase 1.
4. Decide which templates are fixed structure versus restylable.
5. Add user-facing validation scenarios to the broader CWI P0/P1 evaluation harness.
6. Require rendered-answer evidence before deploy.

---

## 8. Approval Note

Recommended decision:

```text
Approve the refined specification set for implementation planning.
Separately approve or reject the product decisions in Section 5.
```
