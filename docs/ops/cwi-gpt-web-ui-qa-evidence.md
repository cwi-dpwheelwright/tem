# CWI GPT Web UI QA Evidence Capture

## Artifact Location
- Store evidence under `artifacts/qa/YYYY-MM-DD/`.
- Use subfolders for `logs/`, `screenshots/`, and `perf/` as needed.

## Required Evidence
- Lint/typecheck/unit test outputs.
- Manual smoke checklist results (see `docs/ops/cwi-gpt-web-ui-support-matrix.md`).
- Accessibility checklist results (see `docs/ops/cwi-gpt-web-ui-accessibility-checklist.md`).
- Screenshots of primary flows (/trust, /ask, /action, /trace, /settings).

## Recommended Evidence
- Lighthouse/performance traces for /ask and /action.
- Network traces for a successful ask/action request.

## Steps
1. Run quality gates:
   - `npm run lint`
   - `npm run typecheck`
   - `npm run test:unit`
   - `npm run test:e2e` (if applicable)
2. Save command outputs to `artifacts/qa/YYYY-MM-DD/logs/`.
3. Capture UI screenshots and store them in `artifacts/qa/YYYY-MM-DD/screenshots/`.
4. Attach performance traces (if collected) in `artifacts/qa/YYYY-MM-DD/perf/`.
5. Record any failures with timestamps and links to fixes.
