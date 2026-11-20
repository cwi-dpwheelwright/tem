# ADR-COORD-010: Append-Only Coordination Ledger

## Status
Accepted

## Context
Coordination events (membership, rounds, routing hints) require an audit trail
that is tamper-evident and easy to replicate.

## Decision
- Store coordination events in an append-only ledger.
- Hash-chain each event using the previous event hash to provide tamper
  detection.
- Persist the ledger to durable storage in production while using in-memory
  repositories for dev/test.

## Consequences
- Pros: tamper-evident audit trail with simple integrity checks.
- Cons: log growth requires retention policies and archival strategies.
