# cwi-router

Minimal clean-architecture skeleton for the 10H router service.

## Build
```
dotnet restore cwi-router.sln --locked-mode
dotnet build cwi-router.sln -c Release
```

## Run (dev)
Set env var:
- ROUTER_HTTP_PORT

Then run:
```
dotnet run --project src/Cwi.Router.Api/Cwi.Router.Api.csproj
```

## Config (selected)
- ORCHESTRATOR_URL (required)
- ORCHESTRATOR_URLS (optional; comma-separated orchestrator endpoints for failover/offload)
- MODEL_PRO_URLS / MODEL_PRO_URL (comma-separated endpoints supported)
- MODEL_FREE_URLS / MODEL_FREE_URL (comma-separated endpoints supported)
- MODEL_PRO_CAPABILITIES_URLS / MODEL_PRO_CAPABILITIES_URL (optional capabilities endpoints aligned by index)
- MODEL_FREE_CAPABILITIES_URLS / MODEL_FREE_CAPABILITIES_URL (optional capabilities endpoints aligned by index)
- MODEL_HEALTH_PATH (default: /api/tags)
- MODEL_HEALTH_PROBE_INTERVAL_SECONDS (default: 30, set <= 0 to disable probes)
- MODEL_CB_FAILURE_THRESHOLD / MODEL_CB_COOLDOWN_SECONDS
- ROUTER_SURVIVAL_ENABLED / ROUTER_SURVIVAL_POLL_SECONDS / ROUTER_SURVIVAL_READY_FAIL
- ROUTER_ROUTING_DECISION_ENABLED / ROUTER_ROUTING_DECISION_OVERRIDE_TIER
- ROUTER_ROUTING_TELEMETRY_ENABLED / ROUTER_ROUTING_TELEMETRY_TENANT
- ROUTER_DEV_OPENAPI_ENABLED (dev-only OpenAPI at /api/dev/openapi/{document}.json)
- ROUTER_TRACE_QUERY_ENABLED (default: false; enables /api/dev/trace/{traceId}/spans)
- ROUTER_TRACE_QUERY_TOKEN (optional auth token header: X-Trace-Query-Token)
- ROUTER_TRACE_QUERY_MAX_TRACES / ROUTER_TRACE_QUERY_MAX_SPANS_PER_TRACE / ROUTER_TRACE_QUERY_MAX_SPAN_AGE_SECONDS
- ROUTER_NETWORK_ASSESSMENT_ENABLED (default: true; enable network assessment ingestion)
- ROUTER_MESH_HEALTH_ENABLED (default: true; enable mesh health lookups)
- ROUTER_MESH_HINT_ENABLED (default: true; enable mesh routing hints)
- ROUTER_NETWORK_SCORE_WEIGHT (default: 0.2; blend factor for network score)
- ROUTER_NETWORK_HEALTHY_THRESHOLD (default: 0.7)
- ROUTER_NETWORK_DEGRADED_THRESHOLD (default: 0.4)
- ROUTER_NETWORK_REROUTE_ON_DEGRADED (default: true)
- ROUTER_MESH_HEALTH_CACHE_SECONDS (default: 10)
- ROUTER_MESH_HEALTH_TIMEOUT_SECONDS (default: 2)
- ROUTER_MESH_HINT_TIMEOUT_SECONDS (default: 2)
