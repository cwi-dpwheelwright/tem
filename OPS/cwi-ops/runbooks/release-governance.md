# Release Governance (Branch Protection + Signing)

## Scope
Defines branch protection, signed commit expectations, and two-person review rules
for mission-critical repositories supporting the 10H baseline.

## Branch protection (minimum)
- Protect `main` (and release branches if used).
- Require pull requests; no direct pushes.
- Require at least two approvals for merge.
- Require status checks to pass (CI + security scans).
- Require branch to be up to date before merge.
- Restrict who can dismiss reviews.

## Signed commits
- Require signed commits for protected branches.
- Approved signing methods: GPG or Sigstore (keyless) per org policy.
- If a commit is unsigned or unverified, it must not be merged into protected branches.

## Two-person review rule
- Enforce two distinct human approvals (no self-approval).
- At least one reviewer must be outside the author’s team for high-risk changes.
- Use CODEOWNERS for critical paths (contracts, security, ops).

## Evidence checklist
- Screenshot or export of branch protection settings.
- CI report showing required checks.
- Example PR with two approvals and signed commits.
