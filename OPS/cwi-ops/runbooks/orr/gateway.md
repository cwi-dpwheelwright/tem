# ORR Runbook: cwi-gateway (container: public-core-gateway)

## Pre-flight
- Verify DNS and certificates.
- Confirm router and pack-service are reachable.

## Startup
1. Deploy Caddy container via compose bundle.
2. Confirm HTTPS and routing for /api/* and /packs/*.

## Readiness
- HTTP 200 for /readyz and /healthz through gateway.
- Verify /api/* and /packs/* routing through the gateway.
- Security headers present.
- If MCP alias is enabled, verify /mcp/* routes through router (set MCP_ALIAS_ENABLED=true).

## TLS renewal
- ACME certificates renew automatically when `{$CADDY_EMAIL}` and `{$CWI_DOMAIN}` are configured.
- For internal CA rotation, update CA bundle and client cert/key env vars, then restart the gateway container.

## Upstream mTLS guardrails
- `UPSTREAM_MTLS_ENABLED=true` (default) enforces TLS/mTLS to upstreams.
- To disable mTLS for dev, set `UPSTREAM_MTLS_ENABLED=false` and `ALLOW_INSECURE_UPSTREAM=true`.
- If mTLS is disabled without `ALLOW_INSECURE_UPSTREAM=true`, upstream requests return 503.

## Config reload
- Preferred: update Caddyfile and restart the compose service.
- If running with admin enabled in a non-prod environment, use `caddy reload --config /etc/caddy/Caddyfile`.

## Logs
- Access logs are emitted to stdout in JSON format.
- Use `docker logs -f public-core-gateway` (or your log shipper) to view request/response logs.

## Rollback
- Stop container.
- Restore previous Caddyfile and image tag.

## Evidence
- Capture response headers and routing traces.
