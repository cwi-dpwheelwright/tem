# Controller Dependency Audit Checklist

Purpose: verify API controllers do not reference repositories, DbContext, or DataAccess types directly.

## Checklist
- [ ] Controllers only depend on application-level abstractions and DTOs.
- [ ] No references to Entity Framework or DbContext.
- [ ] No references to repository implementations.
- [ ] No direct SQL access or connection strings.
- [ ] Cross-cutting concerns are handled by middleware/pipeline, not controllers.

## Findings
- No controllers beyond health/metrics in current skeleton.
