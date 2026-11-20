# Observability & SLO Readiness (Iteration-08)

## SLO targets (initial)
- **Gateway**: 99.9% availability, p95 < 300ms
- **Router**: 99.9% availability, p95 < 500ms
- **Orchestrator**: 99.5% availability, p95 < 800ms

## Alert thresholds
- 5xx error rate > 1% for 5 minutes
- p95 latency > 2x target for 5 minutes
- Circuit breaker open rate > 5% for 10 minutes

## Dashboards
- Prometheus config: `OPS/cwi-ops/observability/prometheus.yml`
- Alerts: `OPS/cwi-ops/observability/prometheus-alerts.yml`
- Grafana dashboards: `OPS/cwi-ops/observability/grafana/*.json`

## Runbooks
- SLO/SLI runbook: `OPS/cwi-ops/runbooks/observability/slo-sli.md`
- Guardrails alerts: `OPS/cwi-ops/runbooks/observability/guardrails-alerts.md`
