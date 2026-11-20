# CWI GPT Web UI Support Matrix

## Desktop Browsers (Minimum)
- Chrome 120+
- Edge 120+
- Firefox 115 ESR+

## Mobile Behavior
- Mobile Safari and Chrome on Android should render the UI, but layouts are optimized for desktop.
- Ask/action/trace flows should function; verify on target devices before release.

## Notes
- Requires modern browser APIs (fetch, AbortController, Web Crypto).
- Keep this matrix updated quarterly or when platform requirements change.

## Manual Smoke Checklist
1. `/settings`: set gateway URL, pack name, and token; save overrides.
2. `/trust`: verify pack and confirm hash appears.
3. `/ask`: submit a question and confirm answer + trace panel.
4. `/action`: select an action, invoke, and confirm result/approval status.
5. `/trace/[traceId]`: open a known trace and verify mapping.
6. Clear token and confirm 401 redirects to `/settings` with a notice.
