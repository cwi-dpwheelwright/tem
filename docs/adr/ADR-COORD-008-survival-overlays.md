# ADR-COORD-008: Survival-Mode Overlays

## Status
Accepted

## Context
When the mesh enters survival mode, routing and coordination should bias toward
stable, low-risk paths even if that reduces performance.

## Decision
Maintain a global survival-mode flag and reason. Routing hints will enforce stricter
link quality thresholds when enabled (e.g., low loss and bounded latency).

## Consequences
- Pros: consistent degraded behavior across routing clients.
- Cons: conservative routing may reduce throughput during survival mode.
