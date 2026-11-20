# CWI GPT Web UI Compliance Mapping (Release Checklist)

## Purpose
- Provide a repeatable mapping between internal security/privacy requirements and release evidence.
- Record deviations or exceptions with approvals and expiration dates.

## Inputs
- Internal security/privacy requirements catalog (IDs, titles, and current version).
- Release notes for this build and the QA evidence bundle.
- Supporting evidence docs include: `docs/ops/cwi-gpt-web-ui-error-reporting.md`, `docs/ops/cwi-gpt-web-ui-dependency-inventory.md`, `docs/ops/cwi-gpt-web-ui-stride.md`, `docs/ops/cwi-gpt-web-ui-runbook.md`, `docs/ops/cwi-gpt-web-ui-performance.md`, `docs/ops/cwi-gpt-web-ui-accessibility-checklist.md`.
- Note: If the internal requirements catalog is not available, use descriptive requirement titles and attach evidence (this file follows that approach until IDs are provided).

## Release Checklist
1. Confirm the requirements catalog version and the approver responsible for this release.
2. Populate the compliance mapping table for every applicable security/privacy requirement.
3. Attach evidence links for each requirement (tests, docs, logs, or screenshots).
4. Record any deviations in the exceptions table with approvals and expiry.
5. Store the completed checklist with release artifacts.

## Compliance Mapping
| Requirement title | Area | Status | Evidence | Notes |
| --- | --- | --- | --- | --- |
| Authentication tokens are stored securely and never placed in URLs. | Security | Met | `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/storage.ts`; `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/auth.ts`; `src/frontend/web-ui/cwi-gpt-web-ui/src/app/settings/page.tsx` | Tokens stored in session storage; logout clears state. |
| Client logs redact secrets and payloads. | Security, Privacy | Met | `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/logger.ts`; `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/logger.test.ts` | Log redaction covers Authorization and token-like strings. |
| Security headers and CSP are enforced for all routes. | Security | Met | `src/frontend/web-ui/cwi-gpt-web-ui/next.config.ts` | CSP includes gateway and optional error-report endpoint. |
| Error reporting is opt-in and scrubbed of sensitive data. | Privacy, Security | Met | `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/error-reporting.ts`; `src/frontend/web-ui/cwi-gpt-web-ui/src/app/settings/page.tsx`; `docs/ops/cwi-gpt-web-ui-error-reporting.md` | Disabled by default; user can revoke consent. |
| Local data retention is explicit and user-controlled. | Privacy | Met | `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/history.ts`; `src/frontend/web-ui/cwi-gpt-web-ui/src/components/history-retention.tsx`; `src/frontend/web-ui/cwi-gpt-web-ui/src/app/settings/page.tsx` | Retention opt-in with purge on opt-out. |
| External links are sanitized to prevent unsafe schemes. | Security | Met | `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/link-sanitizer.ts`; `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/link-sanitizer.test.ts` | Only http/https allowed; rel attributes set. |
| Dependency licenses are reviewed and high-risk licenses flagged. | Security, Compliance | Met | `docs/ops/cwi-gpt-web-ui-dependency-inventory.md` | Direct dependencies recorded with licenses. |
| Threat model completed for core flows. | Security | Met | `docs/ops/cwi-gpt-web-ui-stride.md` | STRIDE coverage for auth, pack verify, ask/action, trace. |
| Offline/outage behavior fails closed for unsafe actions. | Security, Resilience | Met | `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/offline.ts`; `src/frontend/web-ui/cwi-gpt-web-ui/src/components/offline-banner.tsx`; `src/frontend/web-ui/cwi-gpt-web-ui/src/app/ask/page.tsx`; `src/frontend/web-ui/cwi-gpt-web-ui/src/app/action/page.tsx` | Offline banner blocks actions until recovery. |
| Support diagnostics exclude secrets and prompts. | Security, Privacy | Met | `src/frontend/web-ui/cwi-gpt-web-ui/src/app/settings/page.tsx`; `docs/ops/cwi-gpt-web-ui-runbook.md` | Diagnostics export omits tokens and prompt content. |

## Deviations and Exceptions
| Requirement | Deviation summary | Risk/impact | Approval | Expiration | Tracking ticket |
| --- | --- | --- | --- | --- | --- |
| None recorded | - | - | - | - | - |

## Completion Record
- Release:
- Checklist owner:
- Date completed:
