# CWI User Fresh Slate Reset Report

Run UTC: 2026-05-02T21:17:10Z

## Scope

Performed a data/context reset only. Infrastructure and runtime configuration were preserved.

Cleared:
- current chat history
- conversation summaries
- imported/source memory records and chunks
- governed user memory
- connector-derived normalized memory records
- connector ingestion receipts
- pending approval tickets / pending MCP-style action state

Preserved:
- identity/users/auth infrastructure
- tenant configuration
- connector account configuration
- trusted-site configuration
- packs, pins, policy, and tool registry
- ledger/provenance records
- Redis event stream plumbing

## Before / After Counts

| Store | Before | After |
|---|---:|---:|
| ask_history | 42 | 0 |
| conversation_summaries | 4 | 0 |
| source_memory | 10167 | 0 |
| source_chunks | 359098 | 0 |
| governed_memory | 98 | 0 |
| memory_fact_index | 0 | 0 |
| normalized_connector_records | 0 | 0 |
| connector_ingestion_receipts | 0 | 0 |
| approval_tickets_pending | 2 | 0 |
| connector_runs_active | 0 | 0 |

## Preservation Checks

| Store | Before | After |
|---|---:|---:|
| ledger_events | 1284 | 1284 |
| tenant_config | 4 | 4 |
| user_connector_connections | 23 | 23 |

## Runtime Checks

- Orchestrator health: OK
- Router health: OK
- Redis keys after reset: preserved shared event stream only
- Ownership/succession pending state: none present after reset

## Evidence Files

- before_counts.json
- after_counts.json
- reset_user_context.sql
- reset_user_context.out
- redis_keys_before.txt
- redis_keys_after.txt
- health_after.txt
- table_list.txt
- before_breakdown.txt
- post_pending_governance_check.txt

## Result

PASS: User chat/context/memory state is clear and core infrastructure remains healthy.