# CWI GPT Web UI Performance & Footprint

Date: 2026-02-07

## Build Snapshot
Command:
- `npm run build` (from `src/frontend/web-ui/cwi-gpt-web-ui`)

Result:
- Build completed successfully on Next.js 16.1.6.
- Static routes: `/`, `/ask`, `/action`, `/settings`, `/trust`, `/trace`, `/auth/callback`, `/_not-found`
- Dynamic routes: `/api/health`, `/trace/[traceId]`

## Notes
- This build confirms the app compiles and renders static/dynamic routes without errors.
- If bundle size regression tracking is required, add a bundle analyzer step (e.g., `ANALYZE=true npm run build`) with `@next/bundle-analyzer` or internal tooling.

## Optional Performance Budgets (Targets)
These are guardrails for future releases and can be tuned as the app evolves:
- First-load JS per route: <= 250 KB
- Total JS for `/ask` or `/action`: <= 400 KB
- Largest contentful paint on desktop: <= 2.5s (fast network)

Record any deviations below with rationale.

## Deviations
- None recorded.
