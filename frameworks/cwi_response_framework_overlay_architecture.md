# CWI Response Framework Overlay Architecture

## Purpose

This document defines how the memory, comprehension/expression, and presentation frameworks participate in CWI responses.

The frameworks are an abstraction over the existing runtime architecture. They fill missing response-intelligence layers; they do not replace packs, routing, policy, MCP, connectors, ledger, retrieval, or the native answer pipeline.

## Architectural Boundary

The native CWI stack remains primary:

```text
input normalization
-> routing / intent classification
-> policy and pack resolution
-> retrieval, memory, graph, trusted web, MCP, or model work as allowed
-> communication core
-> response framework overlays
-> refinement
-> final output with trace
```

Frameworks operate after the system understands what kind of response is being produced and before the final answer is returned to the user. They may influence expression and final presentation, but they must not change authorization, tool execution, pack trust, tenant scope, or evidence requirements.

## Universal Participation

Every final user-facing ASK response should pass through the response frameworks.

Universal participation means evaluation, not forced output.

- Memory evaluates whether stored or imported knowledge is relevant, current, scoped to the user, non-conflicting, permitted, and sourced. It may decide that no memory should be used.
- Comprehension/expression evaluates the current situation, ambiguity, user need, clarity, tone, and response fit.
- Presentation evaluates the final delivery shape: structure, spacing, grouping, source display, and whether the answer is digestible.

This prevents response quality from depending on narrow trigger words while preserving deterministic gates where they are useful.

## Non-Interference Rules

Framework overlays must not:

- replace the routing order;
- override pack verification or pack trust;
- alter MCP policy decisions;
- execute tools or cause tools to execute;
- bypass approval gates;
- inject memory without relevance and permission checks;
- invent sources, confidence, or provenance;
- suppress required warnings, blocked-action messages, or clarification prompts;
- remove trace/request/source metadata.

Framework overlays may:

- select or confirm response shape after routing;
- choose concise vs. detailed delivery;
- preserve ambiguity instead of hiding it;
- group citations and memory/source pills;
- separate work/personal context when memory is used;
- make provenance visible in a readable way;
- refine tone for the user and situation.

## Why This Exists

The response should feel intelligent because CWI has a complete conversational flow:

1. Memory answers, "What does CWI know, and is it allowed/current/relevant?"
2. Comprehension/expression answers, "What does the user need right now, and how should this be understood?"
3. Presentation answers, "How should this be delivered so the user can actually receive and act on it?"

Without this overlay, CWI can have correct data and still produce generic, poorly shaped, or psychologically mismatched responses.

## Implementation Expectations

Response work should be built so framework decisions are observable in trace metadata. A response can be short, direct, and source-free, but the trace should still show that the frameworks evaluated it.

Implementation should prefer reusable orchestration points over scattered prompt-specific branches. Trigger phrases are acceptable for regression protection and high-confidence intent guards, but broad framework involvement must be semantic and context-aware.

## Acceptance Criteria

A response implementation is aligned with this architecture when:

- native routing, policy, pack, MCP, and ledger behavior remains unchanged unless explicitly requested;
- memory is evaluated for relevance/scope/provenance before use;
- comprehension/expression decisions are recorded for final answers;
- presentation decisions are recorded for final answers;
- deterministic/direct responses do not bypass overlay trace metadata;
- clarification, blocked, tool-backed, memory-backed, sourced, and simple direct answers all retain correct response mode;
- tests verify behavior through user-visible output and trace fields.
