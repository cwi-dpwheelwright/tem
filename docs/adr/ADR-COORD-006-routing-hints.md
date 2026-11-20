# ADR-COORD-006: Routing Hint Interface

## Status
Accepted

## Context
Coordination needs a simple interface for producing routing hints based on link metrics
and task complexity. The hint must be deterministic and explainable for ops.

## Decision
Expose a routing hint endpoint that accepts:
- node_id
- task_complexity (low/medium/high)

Return:
- preferred_interface
- confidence (0-1)
- reason
- fallback_interfaces

The initial implementation uses a weighted heuristic (signal quality, loss, latency,
bandwidth). The interface is stable even if the internal model evolves.

## Consequences
- Pros: decouples routing logic from clients; supports incremental AI upgrades.
- Cons: hints are best-effort and not guaranteed to be optimal in all conditions.
