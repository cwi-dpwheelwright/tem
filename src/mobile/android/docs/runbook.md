# Android Client Runbook

## Setup
- Build: `gradle :app:assembleDebug`
- Base URL: set `CWI_API_BASE_URL` (env var or Gradle property) before building the app.
- Pack trust: verify packs with `Verify Pack` and ensure anchors match.

## Pack Trust + Offline Behavior
- The client caches verified packs locally.
- If verification fails, it attempts to load the cached pack and enters offline mode.
- Trust banner reflects Verified/Unverified/Unknown based on last verification result.
- Cached packs are integrity-checked against stored anchors before use.

## Survival Mode UX
- Survival banner shows `normal`, `degraded`, or `survival` mode from `/api/survival/status`.
- Actions are restricted when survival mode is not `normal` or when offline mode is active.
- Offline guidance message is pulled from `/api/survival/offline-response` when available.

## Android Auto Safe-Mode
- Use the `Android Auto Mode` toggle to enter safe-mode.
- Safe-mode disables free-text input and renders only allowlisted actions.
- Kill-switch disables action execution even in safe-mode.
- Survival/offline mode further restricts actions in Auto mode.

## Telemetry (Redacted)
- Client emits redacted events to `/api/ingest/telemetry` when enabled.
- Events include only event name, pack name, and trace id (no user text).

## Interaction Capture + Feedback
- Consent toggle gates interaction and feedback capture.
- Interaction events are buffered locally (encrypted) and uploaded in batches.
- Outcome feedback is queued and sent to `/api/feedback` with retry/backoff.

## Testing & Simulation Hooks
- Offline simulation: set `BuildConfig.SIMULATE_OFFLINE=true` (debug only).
- Backpressure simulation: set `BuildConfig.SIMULATE_BACKPRESSURE_MS` to delay network calls.
- Interaction sample rate: `BuildConfig.INTERACTION_SAMPLE_RATE` (0.0 - 1.0).
- Queue limits: `BuildConfig.INTERACTION_QUEUE_LIMIT`, `BuildConfig.FEEDBACK_QUEUE_LIMIT`.
- Integration smoke test requires `CWI_ETL_SMOKE_TEST=1` and orchestrator URL/token.

## Troubleshooting
- If pack verification fails, confirm `/packs/{pack}/pack.json` and `anchor.txt` are reachable.
- If actions are disabled, check kill-switch status and approvals.
- For API errors, confirm gateway base URL and auth token.

## Privacy
- Avoid sending raw questions or responses in telemetry.
- Token storage uses Android keystore-backed encryption via `SecureTokenStore`.
