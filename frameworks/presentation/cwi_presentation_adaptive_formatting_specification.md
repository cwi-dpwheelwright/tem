# CWI Presentation Adaptive Formatting Specification

**Document ID:** CWI-SPEC-PRESENTATION-ADAPTIVE-FORMATTING-001  
**Version:** 1.0  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Draft for approval  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document defines requirements for Adaptive Formatting in CWI responses.

Adaptive Formatting adjusts output style based on context, response mode, user request, device constraints, source state, and task complexity. Examples include short versus detailed answers, bullets versus paragraphs, compact versus report-style output, and source grouping.

This is a presentation-layer capability. It does not decide truth, routing, memory, policy, sources, or MCP/tool execution.

---

## 2. Capability Summary

| Attribute | Value |
|---|---|
| Capability | Adaptive Formatting |
| Complexity | Higher |
| Risk | Medium |
| Primary Question | What output style best fits this context? |
| Outcome | CWI adapts answer format while preserving meaning and governance. |

---

## 3. Product Principle

CWI should adapt the answer format to the task without making the user manage formatting manually.

The product promise is:

```text
Short when speed matters,
detailed when depth matters,
structured when action matters,
and always faithful to the underlying answer.
```

---

## 4. Scope

### 4.1 In Scope

Adaptive Formatting covers:

- short versus detailed output
- bullets versus paragraphs
- tables for comparisons
- numbered steps for procedures
- compact source display
- grouped source pills
- technical versus executive layout
- mobile-friendly layout adjustments
- report-style output for proactive modes
- readable formatting for memory-backed answers
- safe formatting for blocked or approval-required actions

### 4.2 Out Of Scope

Adaptive Formatting does not:

- change facts
- change confidence
- change source truth
- decide retrieval use
- decide memory use
- execute MCP tools
- override approval requirements
- make uncertain answers look certain
- hide trace, warnings, or source limitations

---

## 5. Runtime Placement

Adaptive Formatting should run after:

1. response mode selection
2. structural template selection
3. clarity filtering
4. rhythm control
5. adaptive tone

Adaptive Formatting should run before final rendered output.

Required rule:

```text
Adaptive Formatting may change layout.
It must not change meaning.
```

---

## 6. Formatting Inputs

Adaptive Formatting should consider:

- explicit user format request
- response mode
- answer length
- source count and source type
- device/layout constraints
- selected pack only when relevant
- memory state
- retrieval state
- MCP/tool status
- urgency
- sensitivity
- user role or audience when stated
- whether the answer is final, streaming, blocked, or clarification-required

---

## 7. Formatting Modes

### 7.1 Compact Answer

Use when:

- simple question
- deterministic answer
- user asks for short answer
- mobile/constrained context

Expected:

- one short paragraph or a small bullet list
- no heavy sectioning

### 7.2 Standard Explanation

Use when:

- normal explanation
- moderate detail needed
- user did not specify format

Expected:

- short paragraphs
- bullets only where useful
- source indicators when applicable

### 7.3 Detailed Report

Use when:

- daily briefing
- what changed
- risk analysis
- implementation summary
- executive update
- user asks for detail

Expected:

- sections
- bullets or numbered lists
- visible risk and next action when relevant
- grouped sources

### 7.4 Stepwise Procedure

Use when:

- user asks how to do something
- planning
- troubleshooting
- deployment or testing instructions

Expected:

- numbered steps
- prerequisites or constraints if needed
- clear completion criteria

### 7.5 Comparison Table

Use when:

- comparing options
- evaluating tradeoffs
- choosing between providers, tools, plans, or states

Expected:

- table only when it improves readability
- not used for narrow mobile contexts if it harms readability

### 7.6 Clarification Format

Use when:

- the system needs more information

Expected:

- one targeted question
- optional list of choices only when it reduces ambiguity
- no long explanation

### 7.7 Blocked / Approval Format

Use when:

- policy blocks the action
- approval is required
- tool unavailable

Expected:

- status first
- reason second
- safe alternative or approval path third

---

## 8. Format Selection Rules

Adaptive Formatting should follow this priority:

1. user explicitly requested format, when safe and compatible
2. response mode required format
3. blocked/approval/clarification state
4. content type
5. answer length and complexity
6. device/layout constraints
7. source display needs
8. default standard explanation

If user format conflicts with safety, response mode, source requirements, or policy, the governing requirement wins.

Example:

```text
User asks: "Give me a one-word answer: is this safe to publish?"
Expected: CWI should not reduce a privacy/safety answer to one word if explanation is required.
```

---

## 9. Source Formatting

When sources are used:

- cite or indicate sources near the claims they support
- group source indicators by type when multiple sources exist
- avoid raw technical source dumps in ordinary answers
- preserve full source details in authorized debug/admin views
- distinguish web, memory, documents, graph, packs, tools, and home-node sources when available

User-facing source display should support compact pills such as:

```text
Memory 4   Web 2   Graph 1
```

The exact UI rendering may vary, but the product requirement is grouped, readable, and not source-by-source clutter unless requested.

---

## 10. Memory Formatting

When memory is used:

- separate identity, work, projects, people, priorities, and open loops when relevant
- do not expose sensitive memory unnecessarily
- show source/provenance indicators
- show empty, partial, restricted, stale, or conflicted state clearly when applicable
- avoid mixing unrelated personal memory into general answers

Memory-backed answers should feel useful and personal, not invasive.

---

## 11. Proactive Mode Formatting

Adaptive Formatting must preserve proactive templates:

- Daily Briefing: top 3 to 5 priorities, 1 risk, 1 recommendation
- What's At Risk: 1 to 2 risks, impact, next action
- What Changed: clear delta summary
- Make A Plan: actionable steps and next action

Formatting may improve readability, but cannot expand beyond item limits unless the user explicitly asks for more.

---

## 12. Device And Layout Requirements

Adaptive Formatting should account for:

- desktop chat layout
- mobile/narrow screens
- sticky composer
- source pills
- Glass Box/debug panel
- modal contexts
- long answers in scroll containers

Requirements:

- no table should be used if it creates unreadable horizontal overflow
- final answer must not be hidden behind collapsed controls
- last message must not be covered by composer
- source pills must wrap cleanly
- numbered steps must remain readable on mobile

---

## 13. Governance And Safety Rules

Adaptive Formatting must not:

- remove safety warnings
- hide blocked/approval-required status
- compress away uncertainty
- turn citations into unsupported claims
- expose raw private memory through formatting
- change technical code blocks into incomplete fragments
- use formatting to make generated content appear more authoritative than evidence supports

---

## 14. Trace And Debug Requirements

Authorized trace/debug views should show:

- formatting mode selected
- reason for formatting mode
- user format request if present
- response mode that constrained formatting
- source grouping applied
- device/layout fallback if used
- whether formatting was skipped or degraded

---

## 15. Failure Behavior

If Adaptive Formatting fails:

- preserve answer text
- preserve source/trace data
- preserve warnings and approval status
- fall back to structural template output
- mark degraded formatting in debug/admin views

Failure must not drop content.

---

## 16. Acceptance Tests

### 16.1 Short Answer

Prompt:

```text
answer in one sentence: what is CWI?
```

Expected:

- one-sentence answer if safe
- no heavy formatting

### 16.2 Detailed Answer

Prompt:

```text
give me a detailed implementation update
```

Expected:

- detailed report format
- sections or bullets
- next action visible

### 16.3 Comparison

Prompt:

```text
compare trusted sites vs paid search providers
```

Expected:

- comparison table or equivalent readable structure
- tradeoffs clear

### 16.4 Source Grouping

Prompt:

```text
use web and explain the capital of Texas
```

Expected:

- coherent answer
- grouped source indicators
- no awkward source-by-source answer unless requested

### 16.5 Safety Override

Prompt:

```text
answer in one word: can I publish this private customer note?
```

Expected:

- not one word if explanation is required
- privacy/safety warning preserved

### 16.6 Mobile Readability

Scenario:

```text
Open a long source-backed answer on mobile width.
```

Expected:

- no unreadable table overflow
- source pills wrap
- answer remains visible

---

## 17. Refinement Addendum - Formatting Safety Gates And Downgrades

This review identified a gap: Adaptive Formatting needs explicit downgrade behavior. The system must know what to do when a requested format is unsafe, unreadable, or incompatible with the response mode.

Required formatting reason codes:

- `format_compact_answer`
- `format_standard_explanation`
- `format_detailed_report`
- `format_stepwise_procedure`
- `format_comparison_table`
- `format_comparison_list`
- `format_clarification`
- `format_blocked_or_approval`
- `format_source_grouped`
- `format_memory_grouped`
- `format_mobile_downgrade`
- `format_user_request_honored`
- `format_user_request_overridden`
- `format_degraded_template`

Safety gates:

- format changes must preserve meaning
- format changes must preserve response mode
- format changes must preserve safety and approval language
- format changes must preserve source limitations
- format changes must preserve complete code blocks for code answers
- format changes must not make uncertain claims look authoritative

Downgrade rules:

- if a table would be unreadable on mobile, use grouped bullets instead
- if the user asks for a one-word answer but the topic is sensitive, provide the necessary warning
- if source count is high, group sources by type in ordinary answers and keep details in source/debug views
- if memory context is restricted, show restricted state rather than formatting around it
- if a comparison is too complex for a compact answer, state the top recommendation and offer details
- if adaptive formatting fails, fall back to the structural template and preserve all content

Additional acceptance coverage:

- explicit short-answer request is honored for safe factual prompts
- explicit short-answer request is overridden for sensitive/privacy prompts
- comparison table downgrades to readable bullets on narrow layout
- code answer formatting does not remove runnable code
- source-heavy answer groups sources cleanly

---

## 18. Definition Of Done

Adaptive Formatting is complete when:

- formatting modes are defined and traceable
- explicit user format requests are honored when safe
- response modes remain dominant
- safety and approval language cannot be formatted away
- source and memory formatting are readable
- desktop and mobile layouts are validated
- rendered UI smoke tests confirm visible final answers
- degraded behavior preserves content and trace

---

## 19. Rollback / Disable Requirements

If Adaptive Formatting becomes unreliable:

- disable adaptive formatting transformations
- preserve structural templates
- preserve rhythm control if safe
- preserve all answer text, warnings, sources, and trace metadata
- show degraded state in debug/admin views

---

## 20. Approval Gate

Decision requested:

```text
Approve or deny Adaptive Formatting as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
