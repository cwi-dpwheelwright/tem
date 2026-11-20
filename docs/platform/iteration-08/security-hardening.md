# Security Hardening Checklist (Iteration-08)

## Secrets hygiene
- Use `env://` or secret manager references where supported.
- Run secret scan: `OPS/cwi-ops/scripts/scan-secrets.py`.

## TLS/mTLS
- Gateway TLS config validated (`OPS/cwi-ops/compose/gateway/Caddyfile`).
- Coordination layer trust bundle documented (`OPS/cwi-ops/pki/coordination-layer-trust-bundle.md`).

## Authn/Authz
- Router/gateway enforce JWT validation and rate limits.
- Orchestrator is not directly exposed in production (ingress only via router/gateway).

## Logging + redaction
- Review log sinks for PII/PHI.
- Track mitigation in `log-redaction-review.md`.

## Data retention
- Retention and purge guidance in `data-retention.md`.

## Verification steps
- Capture auth enforcement evidence (401/403) for protected endpoints.
- Ensure JWKS/issuer/audience are set for production.
