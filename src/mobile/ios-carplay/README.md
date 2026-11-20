# iOS CarPlay repo skeleton

This folder is the baseline XcodeGen-compatible skeleton for the CWI iOS CarPlay app.

## Generate Xcode project

```
xcodegen generate
```

## Runtime configuration (Info.plist)

- `CertPinHost`: Hostname for TLS pinning (empty disables).
- `CertPinSHA256`: Comma-separated SHA256 pins (base64, with or without `sha256/` prefix).
- `CertPinEnabled`: Enables pinning in non-debug builds only (debug builds always bypass).
- `CrashReporterEnabled`: Enables the debug crash reporter (redacts sensitive values).
- `FeedbackQueueLimit`: Max queued feedback events (default 100).
- `SimulateOffline`: Forces API and pack fetches to behave as offline.
- `SimulateBackpressureMs`: Adds artificial latency (milliseconds) to API and pack fetches.

## Runtime configuration (runtime-config.json)

To avoid hardcoding a base URL, the app can load a runtime JSON file at startup. The first matching file wins in this order:
- `Documents/runtime-config.json`
- `Application Support/runtime-config.json`
- bundled `runtime-config.json` (optional, if included as a resource)

Supported fields (camelCase or snake_case):
- `baseUrl`
- `tenantId`
- `userId`
- `packName`
- `policyMode`
- `overrideExisting` (set `true` to replace values already saved in UserDefaults)

Only non-empty values are applied; `baseUrl` has no default.

Example:
```json
{
  "baseUrl": "https://api.cwi.example",
  "tenantId": "tenant-1",
  "userId": "user-1",
  "packName": "default",
  "policyMode": "approve",
  "overrideExisting": false
}
```

Sample file: `runtime-config.example.json`.

## Behavior notes

- Pack cache verification uses anchor integrity checks before offline fallback.
- Survival mode disables actions and surfaces offline guidance.
- Outcome feedback is queued locally and retried with backoff until upload.

## Simulation test plan

1. Set `SimulateOffline` to true and launch the app.
   - Expect offline banner + cached pack usage.
   - Action buttons should be disabled.
2. Set `SimulateBackpressureMs` to 2000 and verify UI remains responsive.
   - Ask/action calls should take ~2 seconds longer.
3. Toggle survival mode in the backend and call "Refresh Survival Status".
   - Expect survival banner + action gating.
4. Submit feedback with consent enabled and confirm queue count returns to 0 after upload.

## Notes

- Bundle ID prefix: `com.cwi`
- Target bundle id (default): `com.cwi.personal`
