# Controller Audit Checklist (Orchestrator)

Purpose: verify API controllers depend only on application services (no repositories or DbContext).

Checklist
- API project has no references to DataAccess namespaces.
- Controllers inject only application services and health checker.
- No controller directly calls repositories or DbContext.
- Architecture tests enforce layer boundaries.

Current status
- HealthController: depends on DependencyHealthChecker only.
- AskController/ActionController/ApprovalController/TelemetryIngestController: depend on application services only.
- Enforced by Cwi.Orchestrator.ArchTests (NetArchTest).
