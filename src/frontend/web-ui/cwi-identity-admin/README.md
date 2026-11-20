# CWI Identity Admin

React admin console for the identity service.

## Getting started
```
npm install
npm run dev
```

## Runtime configuration
- `VITE_IDENTITY_BASE_URL` (default: http://localhost:8085)

## Container build
Build with the base URL baked into the bundle:
```
docker build -t cwi-identity-admin --build-arg VITE_IDENTITY_BASE_URL=http://cwi-identity-server:8085 .
```

## Testing
Playwright smoke tests require a live identity server (no stubs). Ensure the
identity stack is running and provide admin credentials.

Example (PowerShell):
```
docker compose -f OPS/cwi-ops/compose/cwi-identity-stack/docker-compose.yml --env-file OPS/cwi-ops/compose/cwi-identity-stack/.env up -d postgres cwi-identity-server

set PLAYWRIGHT_BASE_URL=http://localhost:8090
set PLAYWRIGHT_IDENTITY_BASE_URL=http://localhost:8085
set PLAYWRIGHT_ADMIN_IDENTIFIER=superadmin
set PLAYWRIGHT_ADMIN_PASSWORD=ValidPassw0rd!
set PLAYWRIGHT_ADMIN_TENANT_ID=admin
set PLAYWRIGHT_TENANT_ID=admin

npm run test:e2e
```

Required env vars:
- `PLAYWRIGHT_BASE_URL` (admin UI URL; default: http://localhost:8090)
- `PLAYWRIGHT_IDENTITY_BASE_URL` (identity API URL; default: http://localhost:8085)
- `PLAYWRIGHT_ADMIN_IDENTIFIER`
- `PLAYWRIGHT_ADMIN_PASSWORD`
- `PLAYWRIGHT_TENANT_ID`

Optional env vars:
- `PLAYWRIGHT_ADMIN_TENANT_ID` (only needed if the admin login requires a tenant)
```
set PLAYWRIGHT_BASE_URL=http://localhost:8090
npm run test:e2e
```

## Notes
- This UI stores the bearer token in localStorage for the session.
- Login accepts either an admin username or email address; tenant ID is optional for super admin logins.
- Admin endpoints must be protected upstream (mTLS, gateway auth, or private network).
