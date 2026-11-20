# CWI GPT Web UI Error Reporting Policy

## Overview
Error reporting is optional and disabled by default. When enabled, the app sends non-sensitive error metadata to a configured endpoint to help with troubleshooting.

## Opt-In Control
- Users can enable or disable error reporting in `/settings`.
- Consent is stored locally in the browser and can be revoked at any time.

## Configuration
- Set `NEXT_PUBLIC_CWI_ERROR_REPORT_URL` to the HTTPS endpoint that should receive error reports.
- If the endpoint is not configured, error reporting remains disabled and no reports are sent.

## Payload Contents (Sanitized)
Reports include:
- Error category and summary
- HTTP status and request id (when available)
- Trace id (when available)
- Current route path
- Build version and short SHA
- Gateway base URL and pack name
- Timestamp and user agent

## Scrubbing Rules
Prompts, responses, tokens, and other sensitive fields are removed or redacted before sending. Any token-like values in strings are also redacted.

## Failure Behavior
If reporting fails, the app suppresses errors and continues without user impact.
