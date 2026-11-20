# ADR-ETL-003: OCR Provider Selection + Data Handling

## Status
Accepted

## Context
Iteration-06 introduces optional OCR extraction for ETL ingestion. We need a provider selection strategy that is safe by default and keeps data handling constraints explicit before enabling any external OCR service.

## Decision
- Add an OCR adapter interface with a required provider config contract and fail-closed validation.
- Ship a simulated OCR provider for testing and allow a `disabled` provider for deployments that prohibit OCR.
- Require bounded execution controls (timeouts, concurrency limits, max bytes/pages) in the OCR runner.
- External OCR providers require an explicit follow-on ADR and a feature flag before enablement.

## Consequences
- Pros: safe-by-default OCR configuration, deterministic limits, no accidental external data egress.
- Cons: simulated provider only; real OCR integration is deferred until a dedicated provider ADR is approved.
