# CWI GPT Web UI STRIDE Checklist

## Scope
- End-user web client flows: auth/session, pack verification, ask/action requests, trace display.

## Assumptions
- TLS is enforced between browser and gateway/pack services.
- Identity provider issues bearer tokens (or OIDC) with appropriate scopes.

## STRIDE by Flow
### Auth / Session
- Spoofing: Token stored in sessionStorage only; 401 redirects to `/settings` and clears session.
- Tampering: Tokens never in URLs; client-side config validation rejects invalid values.
- Repudiation: API errors capture request id headers for audit trails.
- Information Disclosure: Logging redacts Authorization and token-like values.
- Denial of Service: Request timeouts prevent hung sessions.
- Elevation of Privilege: No admin routes exposed; roles not used to unlock admin capabilities.

### Pack Verification
- Spoofing: Pack hash computed via RFC8785 canonicalization and anchor.txt verification.
- Tampering: Anchor mismatch blocks pack usage and clears cached pack.
- Repudiation: Pack hash shown to user and recorded in diagnostics.
- Information Disclosure: Pack content not logged; only hash exposed.
- Denial of Service: Timeouts on pack fetch; cached pack used when safe.
- Elevation of Privilege: Unverified packs block ask/action flows.

### Ask / Action
- Spoofing: Requests include tenant/user IDs from settings; auth required.
- Tampering: Pack hash included in action requests; policy mode enforced.
- Repudiation: Request ids surfaced in error messages and diagnostics.
- Information Disclosure: Prompts/responses never logged; redaction utility applied.
- Denial of Service: AbortController timeouts and bounded retries for GETs only.
- Elevation of Privilege: Free-text actions disabled; actions derived from pack only.

### Trace Display
- Spoofing: Trace lookup requires auth; trace id validated via gateway.
- Tampering: Trace data displayed read-only; no edits.
- Repudiation: Request ids captured on lookup failures.
- Information Disclosure: Trace fields shown without sensitive payloads; sources listed as strings.
- Denial of Service: Retry button with bounded retries; timeouts enforced.
- Elevation of Privilege: No admin-only trace endpoints exposed.

## Open Items
- None.
