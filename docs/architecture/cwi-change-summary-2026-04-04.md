# CWI Change Summary

## Metadata

- Date: 2026-04-04
- Repo: `cwi-platform`
- Scope: current local CWI platform changes present in this workspace as of this summary
- Deployment status: not deployed in this summary pass

## Executive Summary

This change set tightens the CWI home-node architecture and aligns the implementation with the current control-plane design:

- `client -> cwi-gateway -> cwi-router -> cwi-orchestrator -> cwi-coordination-layer -> cwi-node-agent-runtime -> home node`
- home-node routing is now modeled as a control-plane concern instead of an ad hoc direct path
- coordination-layer now supports persistent outbound node sessions and durable work tracking
- orchestrator now has durable home-node registry, assignment, policy, routing, and home-node ask dispatch behavior
- node-agent runtime now supports the outbound control session and a real local ask execution path
- orchestrator contract drift and architecture drift were cleaned up
- coordination integration tests were made environment-safe on machines without Docker

The result is a much cleaner repo state for the current feature slice: the relevant orchestrator, coordination-layer, and node-agent-runtime builds and tests are now green locally.

## Architecture Alignment

### What the stack now reflects

- Home-node work is routed through the CWI control plane, not around it.
- Orchestrator owns user-to-node routing decisions, policy gating, fallback, and response shaping.
- Coordination-layer owns enrollment, node presence, durable work records, and active session delivery.
- Node-agent runtime owns node-side token validation, local policy checks, task execution, and result return.
- Durable provenance is preserved through orchestrator trace data and coordination-layer work records.

### Key design corrections made

- The temporary long-poll mailbox assumption was superseded by the newer immediate-processing spec and replaced with persistent outbound WebSocket sessions.
- Coordination API layering was corrected so API code no longer directly reaches into domain/data-access membership concerns for node session validation.
- Orchestrator contract fixtures were updated so the stored examples reflect the current serialized response shape.

## Change Areas

## 1. `cwi-orchestrator`

### Home-node control-plane model

The orchestrator now has a durable home-node model that covers:

- home-node registry
- user-to-home-node assignment
- policy scopes
- persistence and migrations
- API surface for management

Implemented pieces include:

- `HomeNodeRecord`, `HomeNodeAssignment`, and `HomeNodePolicyScope` domain entities
- repositories for each of those entities
- `0008_home_node_control_plane.sql` migration
- registry, assignment, and policy services
- admin controllers for home nodes, assignments, and policy scopes

### Home-node ask routing and dispatch

The ask workflow now supports routing to a user-assigned home node when policy and health allow it.

This includes:

- `AskHomeNodeDispatchStep`
- `HomeNodeDispatchService`
- `HomeNodeDispatchClient`
- `HomeNodeWorkTokenService`
- coordination-client wiring and options in orchestrator config

Behavior added:

- load user/home-node assignment before shared inference
- evaluate node availability and fallback eligibility
- issue a signed short-lived work token for node execution
- queue work to coordination-layer
- wait briefly for terminal node results when possible
- return `pending` or fallback states when the node path cannot complete immediately

### Ask response and trace enrichment

The orchestrator response contract now preserves more home-node execution detail.

Added or extended:

- `home_node_sources` in the ask response DTO
- richer `trace` payload fields for home-node execution
- mapping of node-returned evidence into response and trace
- confidence and provenance handling for node-local evidence

This allows a node-local answer to carry:

- evidence sources
- execution mode metadata
- pack or bundle references
- policy outcome context
- local search terms and source counts where returned

### Journey ingest cleanup

An architecture leak in orchestrator API was cleaned up by moving journey-stream ingest behavior into the application layer.

Result:

- API no longer directly references forbidden domain/data-access concerns for journey stream ingest
- orchestrator arch tests pass again

### Contract drift cleanup

The frozen JSON fixtures under `contracts/fixtures/orchestrator` were updated to match the current response models.

Affected fixture families:

- ask response
- action response
- approval-complete response
- telemetry-ingest response
- trace governance payload

Also tightened:

- null serialization for `TracePayload.RequestId`

## 2. `cwi-coordination-layer`

### Membership enrichment

The enrollment model is now materially richer than the earlier minimal node-id plus certificate form.

Enrollment and membership state now include:

- tenant id
- owner user id
- device class
- Tailscale IP
- watcher node id
- capabilities
- pack versions
- policy profile version
- health payload
- last seen updates

Enrollment behavior was also improved so same-node re-enrollment with the same certificate can refresh node state cleanly.

### Durable node work model

Coordination-layer now has a durable node work subsystem.

Added:

- `NodeWorkItem` domain entity
- repository interfaces and implementations
- in-memory and persistent storage support
- migration `0003_node_work_mailbox.sql`
- `NodeWorkService`
- `NodeWorkController`
- request and response models for node work

This gives the platform:

- durable work records
- a control-plane place to store request metadata
- ack/result tracking
- retry and provenance groundwork

### Persistent node session transport

The transport was pivoted to persistent outbound WebSocket node sessions.

Added:

- `/control/nodes/connect` WebSocket path
- `NodeControlWebSocketHandler`
- `NodeSessionManager`
- node session contracts and options
- `INodeSessionDispatcher`

Runtime behavior now supports:

- `node_hello`
- keepalive
- heartbeat
- immediate push of `work_envelope`
- `work_ack`
- `work_result`

This aligns the repo with the immediate-processing home-node spec and removes the polling-first assumption from the primary path.

### Layering cleanup

The coordination API arch violation was fixed by moving node-session membership validation and touch/update logic into:

- `NodeSessionMembershipService`

This keeps the API entrypoint thin and preserves the intended layering:

- API
- Application
- Domain/DataAccess

### Test environment hardening

Docker-backed integration tests were made safe for machines without Docker.

Instead of failing at runtime, the affected tests now use:

- `DockerFactAttribute`

That means:

- Docker-backed tests skip intentionally when the daemon is unavailable
- the coordination integration test project can pass cleanly in local non-Docker environments

## 3. `cwi-node-agent-runtime`

### Outbound control session

The node agent now supports a real outbound control channel to coordination-layer.

Added:

- `control_channel.py`
- CLI support through `run-control-session`
- control-session configuration in `config.py`
- updated example config and README docs

Capabilities added:

- open WebSocket session
- send `node_hello`
- keepalive and heartbeat
- receive work
- validate work tokens
- protect against replay
- return `work_ack`
- return `work_result`

### Local ask execution

The node runtime now has a real local ask handler instead of a placeholder response.

Added:

- `local_ask.py`

Behavior:

- searches local unsealed bundle content
- searches cached artifacts for evidence
- returns structured sources and trace metadata
- can still run explicitly approved local subprocess work via the existing runtime path

This gives the home-node round trip a meaningful local execution bridge while leaving room for a future true local retrieval/model adapter.

### Runtime/test updates

Node-agent runtime also received:

- config surface expansion
- CLI registration updates
- documentation updates
- test additions for control channel and local ask
- stabilization of a flaky enrollment rotation test

## 4. Documentation and Planning

Added:

- `docs/architecture/home-node-control-plane-implementation-plan-2026-04-04.md`

That plan now reflects the WebSocket-based node session model and the current repo-aligned implementation direction.

## Validation Performed

The following validations were run successfully during this cleanup cycle.

### Orchestrator

- `dotnet build src/backend/services/cwi-orchestrator/cwi-orchestrator.sln -m:1 /p:UseSharedCompilation=false /nodeReuse:false`
- `dotnet test src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Cwi.Orchestrator.UnitTests.csproj -m:1 /p:UseSharedCompilation=false /nodeReuse:false`
- `dotnet test src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.ContractTests/Cwi.Orchestrator.ContractTests.csproj -m:1 /p:UseSharedCompilation=false /nodeReuse:false`
- `dotnet test src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.IntegrationTests/Cwi.Orchestrator.IntegrationTests.csproj -m:1 /p:UseSharedCompilation=false /nodeReuse:false`
- `dotnet test src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.ArchTests/Cwi.Orchestrator.ArchTests.csproj -m:1 /p:UseSharedCompilation=false /nodeReuse:false`

### Coordination-layer

- `dotnet build src/backend/services/cwi-coordination-layer/cwi-coordination-layer.sln -m:1 /p:UseSharedCompilation=false /nodeReuse:false`
- `dotnet test src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.UnitTests/Cwi.Coordination.UnitTests.csproj -m:1 /p:UseSharedCompilation=false /nodeReuse:false`
- `dotnet test src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.ArchTests/Cwi.Coordination.ArchTests.csproj -m:1 /p:UseSharedCompilation=false /nodeReuse:false`
- `dotnet test src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.IntegrationTests/Cwi.Coordination.IntegrationTests.csproj -m:1 /p:UseSharedCompilation=false /nodeReuse:false`

Coordination integration test note:

- two Docker-backed tests now skip intentionally on machines where Docker is unavailable

### Node-agent runtime

- `python -m pytest src/node-agent-runtime/tests`

Result:

- `89 passed, 1 skipped`

### Diff hygiene

- `git diff --check` is clean for the touched areas aside from Windows line-ending warnings

## Current State After Cleanup

- The relevant builds are green.
- The relevant unit, contract, integration, and architecture test suites are green.
- Dockerless local environments no longer misreport coordination integration health because of container startup failures.
- API/Application/Domain boundaries are cleaner in both orchestrator and coordination-layer.
- The home-node round trip now exists as a real control-plane path instead of only a design note.
- This summary pass did not deploy changes.

## Known Notes

- The working tree is still intentionally dirty because the home-node/control-plane feature work is not yet committed.
- The two Docker-backed coordination tests are designed to skip when Docker is not available locally.
- Windows Git line-ending warnings are present in diff output but are not functional failures.

## File Inventory

### New planning/doc files

- `docs/architecture/home-node-control-plane-implementation-plan-2026-04-04.md`

### New orchestrator files

- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Controllers/HomeNodeAssignmentsController.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Controllers/HomeNodePolicyScopesController.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Controllers/HomeNodesController.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Ask/AskResponseFactory.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/HomeNodes/HomeNodeAssignmentService.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/HomeNodes/HomeNodeDispatchClient.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/HomeNodes/HomeNodeDispatchService.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/HomeNodes/HomeNodePolicyScopeService.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/HomeNodes/HomeNodeRegistryService.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/HomeNodes/HomeNodeWorkTokenService.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Journey/JourneyStreamIngestService.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Models/HomeNodeDispatchDtos.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Models/HomeNodeDtos.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/Steps/AskHomeNodeDispatchStep.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.DataAccess/Migrations/0008_home_node_control_plane.sql`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.DataAccess/Repositories/HomeNodeAssignmentRepository.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.DataAccess/Repositories/HomeNodePolicyScopeRepository.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.DataAccess/Repositories/HomeNodeRepository.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Domain/Entities/HomeNodeAssignment.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Domain/Entities/HomeNodePolicyScope.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Domain/Entities/HomeNodeRecord.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Domain/Repositories/IHomeNodeAssignmentRepository.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Domain/Repositories/IHomeNodePolicyScopeRepository.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Domain/Repositories/IHomeNodeRepository.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/HomeNodes/HomeNodeDispatchServiceTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/HomeNodes/HomeNodeServicesTests.cs`

### New coordination-layer files

- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Api/Controllers/NodeWorkController.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Api/Realtime/NodeControlWebSocketHandler.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Api/Realtime/NodeSessionManager.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Application/Models/NodeSessionMembershipModels.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Application/Models/NodeSessionModels.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Application/Models/NodeWorkModels.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Application/NodeSessionOptions.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Application/Services/NodeSessionMembershipService.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Application/Services/NodeWorkService.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Application/Transport/INodeSessionDispatcher.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.DataAccess/InMemoryNodeWorkRepository.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.DataAccess/Migrations/0003_node_work_mailbox.sql`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.DataAccess/Repositories/NodeWorkRepository.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Domain/Entities/NodeWorkItem.cs`
- `src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Domain/Repositories/INodeWorkRepository.cs`
- `src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.IntegrationTests/DockerFactAttribute.cs`
- `src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.IntegrationTests/NodeSessionTransportIntegrationTests.cs`
- `src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.IntegrationTests/NodeWorkMailboxIntegrationTests.cs`
- `src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.UnitTests/MembershipServiceEnrichmentTests.cs`
- `src/backend/services/cwi-coordination-layer/tests/Cwi.Coordination.UnitTests/NodeWorkServiceTests.cs`

### New node-agent runtime files

- `src/node-agent-runtime/node_agent_runtime/control_channel.py`
- `src/node-agent-runtime/node_agent_runtime/local_ask.py`
- `src/node-agent-runtime/tests/test_control_channel.py`
- `src/node-agent-runtime/tests/test_local_ask.py`

### Modified contract fixtures

- `contracts/fixtures/orchestrator/action-response.json`
- `contracts/fixtures/orchestrator/approval-complete-response.json`
- `contracts/fixtures/orchestrator/ask-response.json`
- `contracts/fixtures/orchestrator/telemetry-ingest-response.json`
- `contracts/fixtures/orchestrator/trace-governance.json`

### Modified existing files of note

- orchestrator configuration, startup, workflow, DTO, trace, dependency injection, data access, and tests
- coordination-layer membership, startup, protocol middleware, gRPC surface, DI, DB context, repositories, and tests
- node-agent runtime config, CLI, package metadata, docs, and existing enrollment tests

