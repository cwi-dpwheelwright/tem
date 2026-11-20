# CWI Presentation Specifications Review And Gap Analysis

**Document ID:** CWI-SPEC-PRESENTATION-REVIEW-GAPS-001  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Review artifact  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document records the review of the CWI Presentation specification set.

The goal is to make sure Structural Templates, Rhythm Control, and Adaptive Formatting improve answer readability without weakening response modes, evidence, source display, memory boundaries, accessibility, or safety.

---

## 2. Files Reviewed

- `cwi_presentation_structural_templates_specification.md`
- `cwi_presentation_rhythm_control_specification.md`
- `cwi_presentation_adaptive_formatting_specification.md`

---

## 3. Overall Assessment

The presentation specs are strong as an initial set. They correctly define presentation as a response-surface layer rather than a routing, evidence, memory, or MCP execution layer.

The main risk is that presentation can quietly change the user's perception of the answer. Bad formatting can make a blocked action look available, hide uncertainty, bury the next action, make sources unreadable, or create mobile layouts that technically contain the answer but are unpleasant to use.

The refined specs now make the presentation layer stricter, more traceable, and easier to validate in rendered UI smoke tests.

---

## 4. Refinements Applied

### 4.1 Structural Templates

Added requirements for:

- stable template contracts
- required and optional fields
- item limits and empty-state behavior
- template reason codes
- accessible labels and reading order
- source-backed and memory-backed overlays
- final answer visibility

### 4.2 Rhythm Control

Added requirements for:

- rhythm reason codes
- streaming boundaries
- blocked-action pacing
- mobile pacing limits
- source-heavy answer spacing
- no artificial dramatic delays

### 4.3 Adaptive Formatting

Added requirements for:

- formatting reason codes
- safety gates
- mobile downgrade behavior
- source-heavy answer grouping
- short-answer override for sensitive prompts
- code answer preservation

---

## 5. Remaining Gaps And Product Decisions

### 5.1 Fixed Versus Flexible Templates

Decision needed:

```text
Which templates are fixed structure, and which can be restyled by adaptive formatting?
```

Recommendation:

- fixed: risk, daily briefing, blocked action, approval required, clarification
- flexible: direct answer, summary, explanation, plan, source-backed answer

### 5.2 Source Pill Labels

Decision needed:

```text
What exact source group labels should appear in ordinary answers?
```

Recommendation:

- Web
- Memory
- Documents
- Graph
- Packs
- Tools
- Home Nodes
- Other

### 5.3 Source Placement

Decision needed:

```text
Should grouped source pills appear after every supported paragraph, at the end of the answer, or both?
```

Recommendation:

- for short answers: end of answer
- for multi-paragraph source-backed answers: after the paragraph or section they support
- for debug/source panels: full source list separately

### 5.4 Markdown And Renderer Contract

Decision needed:

```text
What Markdown or rich-text subset is officially supported by the UI?
```

Recommendation:

- paragraphs
- short headings
- bullets
- numbered lists
- tables only when responsive-safe
- inline code and fenced code for code answers
- source pills rendered by UI metadata rather than raw text when possible

### 5.5 Mobile Table Downgrade

Decision needed:

```text
At what viewport or content width should tables downgrade to grouped bullets/cards?
```

Recommendation:

- require a responsive-safe fallback rather than hard-coding a single business rule in the spec
- validate with mobile smoke tests

### 5.6 Streaming Presentation

Decision needed:

```text
Should source pills and final formatting appear during streaming or only after completion?
```

Recommendation:

- preliminary content can stream
- source pills and final grouped formatting should finalize when source metadata is stable
- blocked/approval status should appear immediately

### 5.7 Accessibility Acceptance

Decision needed:

```text
What accessibility checks are required before presentation changes ship?
```

Recommendation:

- keyboard navigation preserved
- final answer readable by screen reader order
- source pills have text labels
- tables have non-table fallback on narrow screens
- no color-only status meaning
- final answer visible without hover

### 5.8 User Formatting Preferences

Decision needed:

```text
Should users have persistent preferences for short, detailed, executive, technical, or plain formatting?
```

Recommendation:

- Phase 1 should honor per-request format instructions
- persistent preference should wait for governed user settings or memory controls

---

## 6. Cross-Spec Acceptance Criteria

The presentation layer is ready only when:

- direct answers remain direct
- risk and daily briefing templates preserve required item limits
- source-backed answers cite/group sources without becoming source-by-source fragments
- memory-backed answers are readable and do not expose raw private memory
- blocked and approval-required answers remain visually clear
- rhythm improves readability without artificial pauses or content loss
- adaptive formatting honors safe user format requests
- adaptive formatting overrides unsafe format requests
- mobile/narrow layouts remain usable
- final answer text is visible by default
- debug/admin traces show template, rhythm, and formatting reason codes
- rendered UI smoke tests prove behavior, not just API output

---

## 7. Recommended Next Work Items

1. Define final response template contracts for each response mode.
2. Define presentation trace/debug field names.
3. Confirm exact source pill labels and placement.
4. Define responsive table fallback behavior.
5. Add rendered UI smoke tests for direct, risk, briefing, source-backed, memory-backed, blocked, and mobile answers.
6. Decide whether persistent formatting preferences are in Phase 1.
7. Require release evidence with screenshots or rendered-output captures for presentation changes.

---

## 8. Approval Note

Recommended decision:

```text
Approve the refined presentation specification set for implementation planning.
Separately approve or reject the product decisions in Section 5.
```
