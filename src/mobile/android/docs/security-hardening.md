# Android Security Hardening

## Logging
- Authorization header is redacted in OkHttp logs.
- HTTP logging disabled in release builds (`BuildConfig.LOG_HTTP=false`).

## Data at rest
- Auth token stored in EncryptedSharedPreferences (AES256-GCM + MasterKey).
- Pack cache stored in app private storage; metadata in Room.

## Transport
- Enforce HTTPS in production.
- Optional certificate pinning is enabled for release builds when pins are configured.
- Configure `CERT_PIN_HOST` and `CERT_PIN_SHA256` (comma-separated pins) in build config.

## UI/UX
- Avoid displaying sensitive tokens in UI.
- Display minimal error text to user (no stack traces).

## Security review checklist
- [ ] Token storage uses EncryptedSharedPreferences (AES256-GCM + MasterKey).
- [ ] Authorization headers are redacted in logs.
- [ ] Certificate pinning enabled in release when pins configured.
- [ ] Debug-only simulation flags disabled in release builds.
