# DependsOn Compliance Policy (Iteration 04)

## Purpose
Ensure every P0/P1 backlog item declares explicit dependencies for traceability.

## Rules
- Dependencies must be comma-separated tokens.
- Allowed tokens: application names, `contracts/openapi`, `contracts/json-schema`, `contracts/fixtures`, `tools/cwi-gold-tests`, or evidence IDs (`CON-`, `FIX-`, `GT-`).
- No filenames or ad-hoc descriptors in DependsOn.

## Validation
- Scan `iterations/iteration-04.csv` for P0/P1 rows with empty DependsOn.
- Normalize tokens (trim whitespace; remove invalid entries).

## Current Status
- P0/P1 rows verified to include DependsOn entries.
