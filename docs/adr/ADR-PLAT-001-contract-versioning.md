# ADR-PLAT-001: Contract Versioning and Compatibility Policy

## Status
Accepted

## Context
The platform ships multiple services and connector plugins that exchange
contracts and schemas. We need explicit compatibility rules to support safe
rollouts, upgrades, and rollbacks without breaking existing clients or data
pipelines.

## Decision
- Adopt semantic versioning for platform contracts and connector SDKs.
- Use the compatibility matrix in `docs/platform/iteration-04/schema-compatibility-matrix.md`
  to classify changes and decide whether a breaking version bump is required.
- Connector plugins must declare a `sdk_version` and match the runtime
  `CONNECTOR_SDK_VERSION`. The runtime rejects mismatched connector versions.
- Breaking contract changes require a parallel, frozen contract version and
  updated fixtures/tests before release.

## Consequences
- Pros: predictable upgrade/rollback path and explicit compatibility checks.
- Cons: requires discipline to publish new versions and keep fixtures/tests in sync.
