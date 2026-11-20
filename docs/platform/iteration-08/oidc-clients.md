# OIDC Client Registration Guide (Iteration 08)

This note captures the OIDC/OAuth client settings required by the CWI web UI and the Teams bot. The identity admin console does **not** use OIDC (it authenticates directly with the identity API).

## Glassbox Web UI (cwi-glassbox-web-ui)
Client type: **public SPA**
Grant type: **Authorization Code + PKCE**

Required env vars (see `OPS/cwi-ops/compose/full/.env`):
- `NEXT_PUBLIC_OIDC_AUTHORITY`
- `NEXT_PUBLIC_OIDC_CLIENT_ID`
- `NEXT_PUBLIC_OIDC_REDIRECT_URI`
- `NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI`
- `NEXT_PUBLIC_OIDC_SCOPES`

Recommended redirect URIs:
- Local compose: `http://localhost:18090/auth/callback`
- Local dev (Next.js): `http://localhost:3000/auth/callback`

Post-logout redirect URIs:
- Local compose: `http://localhost:18090/`
- Local dev: `http://localhost:3000/`

Scopes:
- Default: `openid profile email`
- Add custom scopes only if the gateway requires them for API access.

Notes:
- The callback route is implemented in `src/frontend/web-ui/cwi-glassbox-web-ui/src/app/auth/callback/page.tsx`.
- `oidc-client-ts` defaults to `response_type=code` and uses sessionStorage.

## Teams Bot (cwi-teams-bot)
Client type: **confidential service**
Grant type: **Client Credentials**

Required env vars:
- `GATEWAY_OAUTH_TOKEN_URL`
- `GATEWAY_OAUTH_CLIENT_ID`
- `GATEWAY_OAUTH_CLIENT_SECRET`
- `GATEWAY_OAUTH_SCOPE`

Notes:
- No redirect URIs are required for client credentials.
- Scope must match what the gateway/router validates for bot traffic.
- If using a static token instead, set `GATEWAY_STATIC_TOKEN` and leave the OAuth settings empty.

## Identity Admin (cwi-identity-admin)
- The admin UI uses **direct login** (`/auth/login`) and stores the bearer token in localStorage.
- It does **not** require an OIDC client or redirect URIs.
- Runtime configuration is via `VITE_IDENTITY_BASE_URL`.

## Validation checklist
- `NEXT_PUBLIC_OIDC_REDIRECT_URI` matches the Glassbox Web UI callback route (`/auth/callback`).
- `NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI` points to the Glassbox Web UI base URL.
- `NEXT_PUBLIC_OIDC_SCOPES` includes `openid` and any required gateway scopes.
- Teams bot OAuth values are configured if `GATEWAY_STATIC_TOKEN` is not set.
