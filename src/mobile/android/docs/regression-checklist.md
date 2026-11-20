# Android Regression Checklist

## Core flows
- App launches to home screen without crashes.
- Pack verification succeeds with valid pack anchor.
- Pack verification fails with tampered pack and shows Unverified.
- Offline mode loads cached pack when network is unavailable.
- Background refresh triggers after cache age exceeds threshold.

## Ask flow
- Ask request succeeds and displays answer.
- Citations list renders (source + snippet).
- Trace fields render (trace_id, policy_mode, model, pack_hash).
- Ask handles server errors and shows error message.

## Action + approvals
- Action invoke posts to `/api/action` with allowlisted action.
- Approval ticket displays when approval is required.
- Approval ticket is cleared/updated after action success.

## Security + auth
- Auth token stored securely and injected on requests.
- Authorization header is redacted from logs.

## Offline & recovery
- App handles gateway timeouts gracefully.
- Cached pack can be cleared and re-fetched.

## Device matrix (minimum)
- Pixel 5 / Android 14 (API 34) - primary.
- Pixel 4a / Android 13 (API 33) - mid-tier.
- Samsung A-series / Android 12 (API 31) - low-tier.
- Emulator x86_64 / API 30 - CI smoke.
