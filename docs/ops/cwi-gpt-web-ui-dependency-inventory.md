# CWI GPT Web UI Dependency Inventory & Licensing

Date: 2026-02-07

## Direct Dependencies

| Package | Type | License | Risk |
| --- | --- | --- | --- |
| next | dependency | MIT | Low (permissive) |
| oidc-client-ts | dependency | Apache-2.0 | Low (permissive) |
| react | dependency | MIT | Low (permissive) |
| react-dom | dependency | MIT | Low (permissive) |
| @playwright/test | devDependency | Apache-2.0 | Low (permissive) |
| @tailwindcss/postcss | devDependency | MIT | Low (permissive) |
| @testing-library/dom | devDependency | MIT | Low (permissive) |
| @testing-library/react | devDependency | MIT | Low (permissive) |
| @types/node | devDependency | MIT | Low (permissive) |
| @types/react | devDependency | MIT | Low (permissive) |
| @types/react-dom | devDependency | MIT | Low (permissive) |
| eslint | devDependency | MIT | Low (permissive) |
| eslint-config-next | devDependency | MIT | Low (permissive) |
| jsdom | devDependency | MIT | Low (permissive) |
| tailwindcss | devDependency | MIT | Low (permissive) |
| typescript | devDependency | Apache-2.0 | Low (permissive) |
| vitest | devDependency | MIT | Low (permissive) |

## High-Risk License Flags

High-risk licenses include strong/reciprocal copyleft or network copyleft such as GPL, AGPL, LGPL, SSPL, CPAL, MPL, or EPL.

- No direct dependencies use high-risk licenses as of 2026-02-07.

## Transitive Dependencies

Transitive license metadata is recorded in `src/frontend/web-ui/cwi-gpt-web-ui/package-lock.json`. Before release, run a full license scan to verify no transitive packages introduce high-risk licenses.

Recommended checks:
- `npm ls --all --json` for a full dependency tree.
- A license scanner (e.g., `license-checker`, `oss-licenses`, or internal tooling) to flag copyleft/network copyleft.
