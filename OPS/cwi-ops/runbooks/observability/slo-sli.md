# SLO/SLI Targets and Alert Thresholds

## Scope
Defines baseline SLOs and SLIs for gateway, router, orchestrator, and pack service.
Targets are starting points for staging and can be tightened with production data.

## Global conventions
- Availability SLI: successful requests / total requests over 5m windows.
- Latency SLI: p95 and p99 for critical endpoints.
- Error budget: 1% monthly (99% availability) unless noted.
- Alert windows: page on sustained breaches (10-15m), ticket on 30-60m.

## Gateway (service: cwi-gateway, container: public-core-gateway)
SLIs:
- Availability: `/healthz`, `/readyz`, `/api/*`, `/packs/*`.
- Latency: p95 `/api/ask` <= 2.0s, p99 <= 5.0s at gateway edge.
Alerts:
- Page if availability < 99% for 10m.
- Page if p99 `/api/ask` > 8s for 10m.
- Ticket if 4xx/5xx rate > 5% for 30m.

## Router (service: cwi-router, container: internal-01-core-router)
SLIs:
- Availability: `/api/ask`, `/api/action`, `/api/approval/*`.
- Latency: p95 `/api/ask` <= 1.5s, p99 <= 4s.
Alerts:
- Page if availability < 99% for 10m.
- Page if p99 `/api/ask` > 6s for 10m.
- Ticket if dependency-down responses > 5% for 30m.

## Orchestrator (service: cwi-orchestrator, container: internal-01-core-orchestrator)
SLIs:
- Availability: `/api/ask`, `/api/action`, `/api/approval/*`.
- Latency: p95 `/api/ask` <= 2.5s, p99 <= 6s.
Alerts:
- Page if availability < 99% for 10m.
- Page if p99 `/api/ask` > 8s for 10m.
- Ticket if provenance write failures > 1% for 30m.

## Pack service (service: cwi-pack-service, container: internal-01-core-pack-service)
SLIs:
- Availability: `/packs/{pack}/pack.json`, `/packs/{pack}/anchor.txt`.
- Latency: p95 <= 200ms, p99 <= 500ms.
Alerts:
- Page if availability < 99.5% for 10m.
- Ticket if 5xx rate > 1% for 30m.

## Runbooks
- Gateway routing failures -> `OPS/cwi-ops/runbooks/orr/gateway.md`
- Pack service availability -> `OPS/cwi-ops/runbooks/orr/pack-service.md`
- Orchestrator incidents -> `OPS/cwi-ops/runbooks/orr/orchestrator.md` (add if missing)
