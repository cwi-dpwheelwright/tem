# Identity Admin Decommission Plan

## Summary
We are deprecating the standalone `cwi-identity-admin` UI in favor of the consolidated `cwi-admin-console`. The identity **API** remains separate; only the admin UI surface is being consolidated.

## Current State
- Standalone UI: `cwi-identity-admin`
- Consolidated UI: `cwi-admin-console`
- Deprecation banner + redirect already added in `cwi-identity-admin`.
- Env var to control redirect target: `VITE_ADMIN_CONSOLE_URL`.

## Target State
- `cwi-admin-console` is the single admin UI for identity tenants/users/roles and pack lifecycle actions.
- `cwi-identity-admin` is read-only, carries a deprecation banner, and redirects users to `cwi-admin-console`.
- Eventually, `cwi-identity-admin` is removed from public routing/compose.

## Scope
### In Scope
- UI parity for identity admin features in `cwi-admin-console`.
- Deprecation banner + redirect in `cwi-identity-admin`.
- Redirect configuration via env.
- Removal of public routing to `cwi-identity-admin` when ready.

### Out of Scope
- Consolidating the identity **API** into the admin console.
- Changes to identity backend contracts beyond UI needs.

## Dependencies
- `cwi-admin-console` OIDC login + admin gating.
- Identity admin UI parity (tenants/users/roles) implemented in admin console.
- Routing + ingress changes to stop exposing `cwi-identity-admin`.

## Migration Plan
### Phase 0: Prepare (Done)
- Add deprecation banner + redirect in `cwi-identity-admin`.
- Configure redirect target via `VITE_ADMIN_CONSOLE_URL`.

### Phase 1: Cutover
- Ensure identity admin parity in `cwi-admin-console`.
- Update documentation + user guidance to point to `cwi-admin-console`.
- Monitor adoption/usage; confirm no blocking regressions.

### Phase 2: Decommission
- Remove `cwi-identity-admin` from public ingress/compose.
- Keep a private/internal build option only if needed for rollback.
- Remove any remaining links to `cwi-identity-admin`.

## Redirect Behavior
- Banner appears on all `cwi-identity-admin` pages.
- Auto-redirect after a short delay to `VITE_ADMIN_CONSOLE_URL`.
- User can cancel and stay temporarily for verification/transition.

## AuthZ & Access Control
- Admin console must enforce admin role checks consistently.
- Identity admin UI should not expose endpoints without auth.
- Prefer server-side guardrails plus client gating.

## Operational Checklist
- Confirm admin console routes for tenants/users/roles.
- Confirm OIDC + admin gating in admin console.
- Validate redirect target and no redirect loops.
- Verify `cwi-identity-admin` not referenced in nav/links.
- Update compose/ingress to remove public exposure.

## Rollback Plan
- Re-enable `cwi-identity-admin` ingress temporarily if critical.
- Keep banner + redirect disabled via env only if needed.
- Prioritize fixes in admin console, then re-disable legacy app.

## Signals / Validation
- Reduction in `cwi-identity-admin` traffic after banner launch.
- No admin workflows blocked in `cwi-admin-console`.
- Support requests for identity admin tasks remain flat or decline.
