# Flakiness Tracking & Quarantine

## Goals
- Detect flaky tests quickly.
- Quarantine unstable tests without blocking critical pipelines.

## Process
1. Track test retries and failures by suite.
2. Tag flaky tests with a quarantine label.
3. Require a follow-up issue with owner and fix plan.

## Evidence
- CI logs with flake tags.
- Quarantine list maintained in repo notes or CI metadata.
