# CWI Presentation Structural Templates Specification

**Document ID:** CWI-SPEC-PRESENTATION-STRUCTURAL-TEMPLATES-001  
**Version:** 1.0  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Draft for approval  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document defines requirements for Structural Templates in CWI responses.

Structural Templates use predefined output formats such as bullet lists, short sections, tables, checklists, and labeled blocks so users can scan and act on answers quickly.

This is a presentation-layer capability. It does not change routing, response mode, evidence, policy, memory, MCP/tool execution, or source truth.

---

## 2. Capability Summary

| Attribute | Value |
|---|---|
| Capability | Structural Templates |
| Complexity | Low |
| Risk | Low |
| Primary Question | What shape should this answer use? |
| Outcome | CWI responses are consistently structured for the task. |

---

## 3. Product Principle

CWI should present answers in a structure that matches the job the user is trying to do.

The product promise is:

```text
The answer should be easy to scan,
easy to act on,
and shaped by the response mode,
without hiding required content.
```

---

## 4. Scope

### 4.1 In Scope

Structural Templates cover:

- direct-answer format
- risk format
- daily briefing format
- what changed format
- plan format
- summary format
- code/explanation format
- clarification format
- blocked-action format
- source-backed answer format
- memory-backed answer format
- admin/debug status format
- deployment/test result format

### 4.2 Out Of Scope

Structural Templates do not:

- decide user intent
- decide if MCP tools execute
- decide whether web retrieval is used
- decide memory eligibility
- decide safety or approval policy
- add sources that were not retrieved
- rewrite facts to fit a template
- hide uncertainty or blocked states

---

## 5. Runtime Placement

Structural Templates should run after:

1. prompt normalization
2. routing and intent classification
3. response mode selection
4. evidence/retrieval/memory/tool decisions
5. communication core

Structural Templates should run before:

1. rhythm control
2. adaptive formatting
3. final response refinement

Required rule:

```text
Response mode chooses the required structure.
Structural Templates render that structure.
```

If a template conflicts with a response mode, the response mode wins.

---

## 6. Required Template Families

### 6.1 Direct Answer Template

Use for simple factual, deterministic, and general knowledge answers.

Required behavior:

- answer first
- keep short unless detail is requested
- include sources only if retrieval was used or required
- no policy boilerplate
- no unnecessary section headers

Example shape:

```text
Austin is the capital of Texas.
```

### 6.2 Risk Template

Use for risk prompts.

Required behavior:

- maximum 1 to 2 risks
- include impact
- include next action
- avoid long background

Required shape:

```text
Risk: ...
Impact: ...
Next action: ...
```

### 6.3 Daily Briefing Template

Use for daily briefing prompts.

Required behavior:

- top 3 to 5 priorities
- exactly 1 primary risk unless no risk exists
- exactly 1 recommendation unless no recommendation is appropriate
- concise and actionable

Required shape:

```text
Top priorities
1. ...
2. ...
3. ...

Risk
...

Recommendation
...
```

### 6.4 What Changed Template

Use for change, delta, status update, and memory reconciliation prompts.

Required behavior:

- summarize what changed
- distinguish new, updated, removed, superseded, and unchanged when available
- include source/trace when relevant
- avoid pretending there was a change if none exists

### 6.5 Plan Template

Use for planning prompts.

Required behavior:

- state objective if useful
- list steps in order
- include next action
- include owner only when known or requested
- include risks/constraints if relevant

### 6.6 Summary Template

Use for summarization prompts.

Required behavior:

- summarize supplied material
- preserve the requested length
- avoid governance boilerplate
- avoid adding unrelated memory unless user requests personalized interpretation

### 6.7 Code Template

Use for code-generation prompts.

Required behavior:

- complete runnable answer for basic requests
- short explanation only when useful
- no incomplete pseudo-code for simple coding asks
- include assumptions when needed

### 6.8 Clarification Template

Use when the system needs more information.

Required behavior:

- one targeted question
- optional safe context
- no execution
- no multi-question interrogation unless the user requested planning or setup

### 6.9 Blocked Or Approval-Required Template

Use when action is blocked, approval-gated, or unavailable.

Required behavior:

- clear status
- plain reason
- safe next option if available
- no tone that implies execution happened

### 6.10 Source-Backed Answer Template

Use when retrieval, trusted web, documents, memory, graph, or connector sources influence the answer.

Required behavior:

- answer remains coherent
- sources are integrated naturally
- source pills or grouped source indicators appear after relevant paragraph or answer section
- avoid one section per source unless the user asks for source comparison

---

## 7. Template Selection Rules

Structural Template selection should follow this priority:

1. blocked/approval-required action template
2. clarification template
3. deterministic/direct answer template
4. proactive mode template
5. memory/profile template
6. source-backed answer template
7. summary/code/plan template
8. default helpful answer template

If multiple templates apply:

- response mode takes precedence
- blocked/approval-required status takes precedence over all presentation preferences
- source-backed presentation can be layered onto the selected template
- memory source indicators can be layered onto the selected template

---

## 8. Governance And Safety Rules

Structural Templates must not:

- remove required safety language
- compress away approval requirements
- hide uncertainty
- omit trace/source indicators when required
- transform a blocked action into advice that sounds like execution
- make policy fallback appear as direct answer
- expose raw private memory or secrets through template fields

---

## 9. User Experience Requirements

Responses should be:

- readable without expanding debug panels
- visible by default
- not hidden behind collapsed cards
- not over-sectioned for simple answers
- not forced into tables on small screens unless the content benefits from comparison
- compatible with grouped source pills
- compatible with chat layout and sticky input behavior

The final answer should not look like an internal trace unless the user opened a debug/admin view.

---

## 10. Admin / Debug Requirements

Authorized trace/debug views should show:

- template selected
- response mode
- reason for template selection
- whether source-backed presentation was applied
- whether template was skipped or degraded
- blocked/approval/clarification status when relevant

---

## 11. Acceptance Tests

### 11.1 Direct Answer

Prompt:

```text
what is 2 plus 2?
```

Expected:

- answer is direct
- no unnecessary sections
- no policy fallback

### 11.2 Risk

Prompt:

```text
what's at risk right now?
```

Expected:

- risk template used
- 1 to 2 risks
- impact
- next action

### 11.3 Daily Briefing

Prompt:

```text
daily briefing
```

Expected:

- top 3 to 5 priorities
- 1 risk
- 1 recommendation

### 11.4 Clarification

Prompt:

```text
send it
```

Expected:

- one targeted clarification question
- no execution

### 11.5 Source-Backed

Prompt:

```text
use web and answer: what is the capital of Texas?
```

Expected:

- direct answer
- readable source indicators
- no separate section per source unless comparison requested

---

## 12. Refinement Addendum - Template Contracts And Accessibility

This review identified a gap: the first version defined template families, but did not require each template to have a stable contract. Without a contract, different implementations could produce inconsistent answer shapes and make smoke testing unreliable.

Required additions:

- each template must define required fields, optional fields, max item counts where applicable, and empty-state behavior
- template output must preserve the selected response mode
- source-backed and memory-backed presentation must layer onto the selected template rather than replace it
- final answer text must be visible by default and must not be hidden behind trace/debug controls
- template labels must be user-facing and readable, not internal route names
- templates must support grouped source indicators without forcing one source section per source
- blocked, approval-required, and clarification templates must not be visually softened into ordinary advice

Minimum template reason codes for trace/debug:

- `template_direct_answer`
- `template_risk`
- `template_daily_briefing`
- `template_what_changed`
- `template_plan`
- `template_summary`
- `template_code`
- `template_clarification`
- `template_blocked_action`
- `template_source_backed`
- `template_memory_backed`
- `template_default_helpful`
- `template_degraded`

Accessibility requirements:

- headings must be meaningful when used
- lists must preserve logical reading order
- tables must not be the only way to understand the answer
- source pills must have readable labels, not color-only meaning
- final answer content must remain understandable to screen readers
- compact answers must not rely on hover-only UI

Additional acceptance coverage:

- source-backed direct answer keeps direct answer shape and adds grouped sources
- memory-backed profile answer uses readable sections without exposing raw memory
- blocked action template is visually distinct from successful execution
- source pill labels are understandable without debug access

---

## 13. Definition Of Done

Structural Templates are complete when:

- each major response mode has a defined presentation shape
- template selection is traceable
- simple answers remain simple
- proactive modes retain their required structures
- blocked and clarification states are clearly presented
- source-backed answers remain coherent
- templates do not override safety, policy, sources, memory boundaries, or response mode
- rendered UI validation confirms answer text is visible and readable

---

## 14. Rollback / Disable Requirements

If Structural Templates become unreliable:

- disable template-specific rendering
- preserve response mode output
- preserve trace/source data
- fall back to default helpful answer presentation
- show degraded state in debug/admin views

---

## 15. Approval Gate

Decision requested:

```text
Approve or deny Structural Templates as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
