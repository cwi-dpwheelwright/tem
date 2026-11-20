# Handoff Package (Sanitized)

This document defines the sanitized handoff package for platform parity and governance work.

## Contents
- **Architecture overview**: `docs/platform/cwi-application-dependency-flow.md`
- **Repo structure**: `docs/platform/cwi-component-repository-structure.md`
- **Release gates**: `docs/platform/iteration-02/platform-release-gates.md`
- **Feature matrix**: `docs/platform/iteration-02/platform-feature-matrix.md`
- **Invariant evidence crosswalk**: `docs/platform/iteration-04/invariant-evidence-crosswalk.md`
- **Acceptance criteria templates**: `docs/platform/iteration-04/acceptance-criteria-templates.md`

## Sanitization Rules
- No secrets or tokens.
- No internal hostnames.
- No raw logs containing PII.
- Use fixtures and redacted examples only.

## Delivery Checklist
- [ ] All docs listed above exist and are up to date.
- [ ] References point to current iteration artifacts.
- [ ] No sensitive data present.
