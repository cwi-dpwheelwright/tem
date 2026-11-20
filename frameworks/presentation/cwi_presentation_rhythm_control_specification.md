# CWI Presentation Rhythm Control Specification

**Document ID:** CWI-SPEC-PRESENTATION-RHYTHM-CONTROL-001  
**Version:** 1.0  
**Date:** 2026-05-02  
**Audience:** Product, engineering, QA, architecture review, governance review  
**Status:** Draft for approval  
**Classification:** Internal - Sanitized  

---

## 1. Purpose

This document defines requirements for Rhythm Control in CWI responses.

Rhythm Control manages pacing, spacing, paragraph breaks, and conversational flow so answers feel readable and natural without becoming slow, theatrical, vague, or over-formatted.

This is a presentation-layer capability. It does not change facts, routing, response mode, policy, memory, sources, or MCP/tool execution.

---

## 2. Capability Summary

| Attribute | Value |
|---|---|
| Capability | Rhythm Control |
| Complexity | Moderate |
| Risk | Medium |
| Primary Question | How should the answer breathe on the page? |
| Outcome | Responses are easier to read, scan, and absorb. |

---

## 3. Product Principle

CWI should pace answers like a good operator or advisor: concise when the user needs speed, deliberate when the topic requires care, and structured when the user needs to act.

The product promise is:

```text
The answer should have enough spacing to be readable,
enough flow to feel conversational,
and enough restraint to avoid wasting the user's time.
```

---

## 4. Scope

### 4.1 In Scope

Rhythm Control covers:

- paragraph length
- section spacing
- list spacing
- ordering of short versus long content
- answer-first pacing
- pause-like breaks for complex or sensitive answers
- reducing wall-of-text output
- avoiding excessive fragmentation
- streaming answer pacing when applicable
- mobile readability

### 4.2 Out Of Scope

Rhythm Control does not:

- insert artificial delays for theatrical effect
- decide intent or route
- decide evidence/source use
- decide MCP/tool execution
- decide memory use
- change answer meaning
- hide required warnings
- make blocked actions sound optional

---

## 5. Runtime Placement

Rhythm Control should run after:

1. response mode selection
2. structural template selection
3. draft answer creation
4. clarity filtering

Rhythm Control should run before:

1. adaptive formatting
2. final response refinement
3. rendered output

Required rule:

```text
Rhythm controls readability, not substance.
```

---

## 6. Rhythm Modes

### 6.1 Compact Mode

Use when:

- direct answer
- deterministic answer
- short factual question
- user asks for brief/short/quick
- mobile or constrained UI

Expected behavior:

- 1 short paragraph or 1 to 3 bullets
- no heavy sectioning
- no long preamble

### 6.2 Standard Mode

Use when:

- normal explanations
- summaries
- source-backed answers
- memory-backed answers
- troubleshooting without urgency

Expected behavior:

- short paragraphs
- occasional bullets
- clear spacing between ideas
- sources placed where readable

### 6.3 Structured Mode

Use when:

- plans
- risk analysis
- daily briefing
- what changed
- implementation summaries
- test results
- deployment updates

Expected behavior:

- sections or numbered steps
- one concept per line or bullet where helpful
- next action visibly separated

### 6.4 Careful Mode

Use when:

- privacy/security/safety-sensitive topics
- approval or blocked actions
- uncertainty or conflict
- user frustration or confusion

Expected behavior:

- slower explanatory pacing
- explicit status
- short sections
- no casual compression of risks

### 6.5 Streaming Mode

Use when:

- answer streams token-by-token or chunk-by-chunk

Expected behavior:

- avoid layout jitter
- avoid repeated headers
- keep current section coherent
- do not create huge blank gaps during streaming
- preserve final structure after completion

---

## 7. Spacing Rules

Rhythm Control should:

- keep paragraphs short enough to scan
- avoid single-sentence paragraph spam in long answers
- use bullets for lists of parallel items
- use numbered lists for ordered steps
- separate risk, impact, and next action when risk mode applies
- separate answer and sources when source pills are shown
- avoid double spacing that makes answers feel sparse
- avoid dense wall-of-text output

Rhythm Control should not:

- split every sentence into its own paragraph
- add decorative pauses
- use spacing to imply certainty or emotion
- hide caveats below large gaps
- push required next actions below excessive explanation

---

## 8. Conversational Pacing Rules

For normal chat:

- answer the question first
- give context second
- give next action last only when the user asked for guidance

For urgent or operational chat:

- status first
- risk/impact second
- next action third

For sensitive chat:

- status or caution first
- reason second
- safe alternative third

For long explanations:

- introduce the answer briefly
- group related ideas
- avoid overusing headings
- end with a useful next step only if relevant

---

## 9. Interaction With Response Modes

Rhythm Control must preserve required response shapes:

- risk prompts still show risk, impact, next action
- daily briefing still shows priorities, one risk, one recommendation
- clarification prompts still ask one targeted question
- blocked-action prompts still clearly state blocked/approval status
- code answers still keep runnable code intact
- summaries remain summaries

If rhythm preferences conflict with response mode structure, response mode wins.

---

## 10. Interaction With UI

Rhythm Control must consider rendered chat behavior:

- final answer text must be visible by default
- spacing must not cause composer overlap
- source pills should not crowd answer text
- long answers should remain scrollable and readable
- mobile layout should not create excessive vertical waste
- collapsed debug/trace sections must not hide final answer content

---

## 11. Trace And Debug Requirements

Authorized trace/debug views should show:

- rhythm mode selected
- reason for rhythm mode
- whether streaming mode applied
- whether rhythm control was skipped or degraded
- whether response mode forced a structure

User-facing answers should not display internal rhythm labels unless debug/trace is open.

---

## 12. Failure Behavior

If Rhythm Control fails:

- preserve the structural template
- preserve answer content
- preserve warnings and sources
- fall back to standard spacing
- mark degraded state in trace/debug

Failure must not drop answer content.

---

## 13. Acceptance Tests

### 13.1 Compact Direct Answer

Prompt:

```text
what is 2 plus 2?
```

Expected:

- compact answer
- no unnecessary spacing
- no section overload

### 13.2 Risk Pacing

Prompt:

```text
what's at risk right now?
```

Expected:

- risk, impact, next action are visually distinct
- answer is concise

### 13.3 Long Explanation

Prompt:

```text
explain how memory activation works
```

Expected:

- readable paragraphs
- no wall of text
- no one-sentence paragraph spam

### 13.4 Sensitive Answer

Prompt:

```text
can I share this private note?
```

Expected:

- careful pacing
- warning is visible
- safe next option is visible

### 13.5 Streaming Answer

Prompt:

```text
write a long implementation summary
```

Expected:

- no repeated headers during streaming
- no layout jitter
- final answer is readable

---

## 14. Refinement Addendum - Rhythm Reason Codes And Streaming Boundaries

This review identified a gap: Rhythm Control needs traceable reason codes and stricter streaming boundaries so pacing does not create UI jitter, repeated sections, or hidden caveats.

Required reason codes:

- `rhythm_compact`
- `rhythm_standard`
- `rhythm_structured`
- `rhythm_careful`
- `rhythm_streaming`
- `rhythm_mobile_compact`
- `rhythm_response_mode_forced`
- `rhythm_user_requested_brief`
- `rhythm_user_requested_detail`
- `rhythm_degraded_standard`

Streaming requirements:

- streaming should not repeat headings as tokens arrive
- streaming should not create large blank gaps
- streaming should not auto-scroll rhythmically if the user has intentionally scrolled up
- final rendered output should normalize into the selected structural template
- source pills should appear only when source information is ready enough to be accurate
- warnings and blocked states must appear early enough that the user does not mistake a blocked action for progress

Pacing boundaries:

- do not add artificial waits or dramatic pauses
- do not split every sentence onto its own line
- do not compress sensitive warnings into a short casual line
- do not push next action below low-value background
- do not create so much vertical spacing that mobile users lose the thread

Additional acceptance coverage:

- long streaming answer finalizes into one coherent structure
- blocked action displays status before explanation
- mobile answer remains readable without excessive vertical scrolling
- source-heavy answer remains readable and does not crowd the source pills

---

## 15. Definition Of Done

Rhythm Control is complete when:

- rhythm modes are defined and traceable
- compact, standard, structured, careful, and streaming modes behave distinctly
- response modes remain dominant
- warnings, approvals, sources, and next actions remain visible
- rendered answers avoid wall-of-text and over-fragmentation
- mobile readability is validated
- degraded behavior preserves answer content

---

## 16. Rollback / Disable Requirements

If Rhythm Control becomes unreliable:

- disable rhythm-specific transformations
- preserve structural templates
- use standard spacing
- preserve all answer text, warnings, sources, and trace metadata
- show degraded state in debug/admin views

---

## 17. Approval Gate

Decision requested:

```text
Approve or deny Rhythm Control as written.
Do not reduce, reinterpret, or remove acceptance criteria without explicit approval.
```
