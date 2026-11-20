# ADR-0001: Orchestrator Layering Retrofit

## Status
Accepted

## Context
The 10H baseline requires strict layer boundaries: API ? Application ? Domain ? DataAccess. Controllers must remain thin and must not reference repositories or DbContext directly. The current migration needs a documented baseline to prevent drift.

## Decision
- Enforce strict project boundaries with build-time architecture tests.
- Move persistence concerns into DataAccess and expose only domain interfaces to Application.
- Require controllers to depend on application services and return ServiceResponse results.

## Consequences
- Implementation will introduce project references and DI modules to keep dependencies clean.
- Changes that bypass the layering rules will fail CI.

## Rollback
Revert to prior project references and remove architecture tests (not recommended).
