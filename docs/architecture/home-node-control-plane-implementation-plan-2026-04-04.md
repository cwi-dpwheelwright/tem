# CWI Home Node Control Plane Implementation Plan

## Status
- Date: 2026-04-04
- Owner: CWI Platform
- Status: Draft implementation plan
- Scope: repo-aligned plan for remote user to home-node execution through the CWI control plane

## 1. Purpose

This document turns the remote home-node design into an implementation plan tied to the current repo.

The required runtime path remains:

`client -> cwi-gateway -> cwi-router -> cwi-orchestrator -> cwi-coordination-layer -> cwi-node-agent-runtime -> home node`

The goal is to move from:

- secure mesh reachability only

to:

- enrolled node identity
- durable user to node assignment
- policy-gated dispatch
- health-aware execution
- explicit client states
- end-to-end route provenance

## 2. Current Repo Baseline

The repo already contains meaningful building blocks.

### 2.1 Present now

- `cwi-coordination-layer` already exposes:
  - `/api/membership/enroll`
  - `/api/membership/rotate`
  - `/api/membership/revoke`
  - `/api/rounds`
  - `/api/rounds/{id}/heartbeat`
- `cwi-node-agent-runtime` already supports:
  - identity generation
  - enrollment
  - activation validation
  - kill-switch sync
  - status reporting
  - data-boundary gating
  - task runtime gating
  - offline queueing
- `cwi-edge-agent` is already positioned as telemetry and buffered forwarding, not node command/control.
- `cwi-orchestrator` already has:
  - JWT-backed tenant and user resolution
  - ask/action workflows
  - journey events
  - ledger plumbing
  - policy and break-glass primitives

### 2.2 Missing now

The missing pieces are mostly control-plane and dispatch logic:

- no durable user to home-node assignment model
- no durable watcher/core pairing model
- no home-node registry carrying owner, capabilities, policy scope, and pack/version state
- no orchestrator service that resolves node-local routing before ask/action execution
- no node work mailbox or leased task transport between coordination-layer and node-agent runtime
- no signed short-lived work token model for orchestrator to node dispatch
- no explicit client-facing execution states for node-local requests
- no complete route provenance for home-node dispatch and result receipt

## 3. Recommended First Transport Choice

### 3.1 Source-of-truth update

The first production implementation should use a persistent outbound WebSocket control session initiated by the home node.

### 3.2 Why this is now the correct fit

- it matches the immediate-processing home-node spec dated 2026-04-04
- it preserves no inbound NAT and no direct gateway dialing
- it removes polling latency from the product path
- it keeps the public request path unchanged while moving delivery onto an already-open node session
- it still allows durable mailbox records for retry, audit, and reconnect recovery

### 3.3 What this means

- orchestrator submits node-targeted work to coordination-layer
- coordination-layer persists the work item durably
- coordination-layer pushes the work immediately over an active node WebSocket session
- node-agent runtime acknowledges, executes, and returns results on the same session
- durable work items remain the source of truth for retry, timeout, and provenance, but not the canonical delivery mechanism

## 4. Ownership Split

### 4.1 Demitrius

Primary ownership:

- orchestrator-side registry and assignment model
- routing policy
- dispatch decisions
- fallback rules
- route provenance
- user-visible execution states

### 4.2 Vale

Primary ownership:

- node runtime polling channel
- signed work token validation
- local execution handlers
- local policy enforcement
- pack/version compatibility checks
- node readiness state machine

## 5. Workstream A: Orchestrator Control-Plane Logic

This should be implemented inside `src/backend/services/cwi-orchestrator`.

### 5.1 New domain model

Add new entities under `src/Cwi.Orchestrator.Domain/Entities`:

- `HomeNodeRecord.cs`
- `HomeNodeAssignment.cs`
- `HomeNodePolicyScope.cs`
- `HomeNodeDispatchRecord.cs`

Suggested responsibilities:

- `HomeNodeRecord`
  - node id
  - tenant id
  - owner user id
  - watcher node id
  - device class
  - tailscale ip
  - capabilities json
  - pack versions json
  - policy profile version
  - status
  - last heartbeat utc
  - assigned state
- `HomeNodeAssignment`
  - tenant id
  - user id
  - primary home node id
  - watcher node id
  - policy scope id
  - local only flag
  - remote fallback allowed flag
  - assigned utc
  - revoked utc
- `HomeNodePolicyScope`
  - policy scope id
  - tenant id
  - rules json
  - version
- `HomeNodeDispatchRecord`
  - work id
  - request id
  - trace id
  - tenant id
  - user id
  - node id
  - task type
  - dispatch state
  - result state
  - created utc
  - completed utc

### 5.2 New repositories

Add repository interfaces under `src/Cwi.Orchestrator.Domain/Repositories`:

- `IHomeNodeRepository.cs`
- `IHomeNodeAssignmentRepository.cs`
- `IHomeNodePolicyScopeRepository.cs`
- `IHomeNodeDispatchRepository.cs`

Add EF-backed implementations under `src/Cwi.Orchestrator.DataAccess/Repositories`.

### 5.3 Database migration

Add a new orchestrator migration:

- `src/Cwi.Orchestrator.DataAccess/Migrations/0008_home_node_control_plane.sql`

Recommended tables:

- `home_nodes`
- `home_node_assignments`
- `home_node_policy_scopes`
- `home_node_dispatch_records`

Do not create a second provenance store. Reuse:

- `ledger_events`
- `journey_events`

for audit and operator traceability.

### 5.4 New application services

Add a new `HomeNodes` or `NodeDispatch` folder under `src/Cwi.Orchestrator.Application` with:

- `HomeNodeRegistryService.cs`
- `HomeNodeAssignmentService.cs`
- `HomeNodeRoutingService.cs`
- `HomeNodeDispatchService.cs`
- `HomeNodeExecutionStateResolver.cs`
- `HomeNodeWorkTokenService.cs`

Responsibilities:

- resolve the authenticated user to the correct home node
- evaluate whether the request belongs on a home node
- apply fallback policy when the node is offline or degraded
- emit a signed short-lived work token
- dispatch work to coordination-layer
- translate dispatch outcomes into `completed`, `pending`, `offline`, `denied`, or `degraded`

### 5.5 API surface

Add controller(s) under `src/Cwi.Orchestrator.Api/Controllers`:

- `HomeNodesController.cs`
- `HomeNodeAssignmentsController.cs`

Recommended endpoints:

- `GET /api/home-nodes`
- `GET /api/home-nodes/{nodeId}`
- `POST /api/home-nodes/assignments`
- `PUT /api/home-nodes/{nodeId}/policy-scope`
- `POST /api/home-nodes/dispatch/evaluate`

These are operator and control-plane APIs. Keep end-user request entry on the existing ask and action endpoints.

### 5.6 Ask and action integration

Update existing request paths first, rather than creating a second orchestrator:

- `src/Cwi.Orchestrator.Api/Controllers/AskController.cs`
- `src/Cwi.Orchestrator.Api/Controllers/ActionController.cs`
- `src/Cwi.Orchestrator.Application/Workflows/AskWorkflow.cs`
- `src/Cwi.Orchestrator.Application/Workflows/ActionWorkflow.cs`
- `src/Cwi.Orchestrator.Application/Workflows/Steps/AskWorkflowSteps.cs`
- `src/Cwi.Orchestrator.Application/Workflows/Steps/ActionWorkflowSteps.cs`

Add an early home-node routing decision step:

1. resolve tenant and user from JWT
2. load home-node assignment
3. load node health and policy scope
4. decide local node vs shared compute vs denial
5. if node-local, dispatch a work item and return an explicit execution state
6. if not node-local, continue the current shared workflow

### 5.7 Response model changes

Extend:

- `src/Cwi.Orchestrator.Application/Models/AskResponseDto.cs`
- `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/api/types.ts`

Add fields such as:

- `execution_state`
- `execution_target`
- `deferred_work_id`
- `home_node_status`

Recommended `execution_state` values:

- `completed`
- `pending`
- `offline`
- `denied`
- `degraded`

## 6. Workstream B: Coordination-Layer Mailbox and Node Presence

This should be implemented inside `src/backend/services/cwi-coordination-layer`.

### 6.1 Extend enrollment model

Update:

- `src/Cwi.Coordination.Application/Models/MembershipModels.cs`
- `src/Cwi.Coordination.Domain/Entities/Membership.cs`
- `src/Cwi.Coordination.Application/Services/MembershipService.cs`
- `contracts/grpc/cwi-coordination-layer.proto`

Move from the current minimal payload:

- `node_id`
- `cert_fingerprint`
- `protocol_version`

to a richer registration payload:

- `node_id`
- `tenant_id`
- `owner_user_id`
- `device_class`
- `tailscale_ip`
- `capabilities`
- `pack_versions`
- `policy_profile_version`
- `watcher_node_id`
- `health`
- `protocol_version`
- `cert_fingerprint`

### 6.2 Add explicit node presence and mailbox storage

Add new entities under `src/Cwi.Coordination.Domain/Entities`:

- `NodePresence.cs`
- `NodeWorkItem.cs`
- `NodeWorkLease.cs`
- `NodeWorkResult.cs`

Add corresponding repositories under `src/Cwi.Coordination.Domain/Repositories` and `DataAccess/Repositories`.

Recommended new migration:

- `src/Cwi.Coordination.DataAccess/Migrations/0003_node_work_mailbox.sql`

Recommended tables:

- `coordination_node_presence`
- `coordination_node_work_items`
- `coordination_node_work_leases`
- `coordination_node_work_results`

### 6.3 New API surface

Add:

- `src/Cwi.Coordination.Application/Models/NodeWorkModels.cs`
- `src/Cwi.Coordination.Application/Services/NodeWorkService.cs`
- `src/Cwi.Coordination.Api/Controllers/NodeWorkController.cs`

Recommended endpoints:

- `POST /api/node-work`
  - orchestrator submits a node-targeted work item
- `POST /api/node-work/lease`
  - node-agent runtime long-polls for one or more work items
- `POST /api/node-work/{workId}/ack`
  - node accepts a leased work item
- `POST /api/node-work/{workId}/result`
  - node posts final result or denied state
- `POST /api/node-work/{workId}/heartbeat`
  - optional long-running task heartbeat

Keep existing membership and rounds APIs. Rounds are useful for simulation and liveness, but mailbox and work-result APIs should become the canonical task transport.

### 6.4 Use existing mesh and backhaul primitives as inputs, not the whole solution

The following should feed dispatch and health scoring:

- `MeshController.cs`
- `RoutingHintService.cs`
- `TopologyHealthService.cs`
- `BackhaulPolicyService.cs`

They should inform placement and channel trust, but not replace user-to-node assignment or mailbox delivery.

### 6.5 gRPC parity

Extend `contracts/grpc/cwi-coordination-layer.proto` to add:

- `CreateNodeWork`
- `LeaseNodeWork`
- `AckNodeWork`
- `CompleteNodeWork`

HTTP should ship first if needed, but the proto should be kept in sync so the control plane has one contract set.

## 7. Workstream C: Node-Agent Runtime Polling and Execution

This should be implemented inside `src/node-agent-runtime`.

### 7.1 Enrollment payload extension

Update:

- `node_agent_runtime/enrollment.py`
- `node_agent_runtime/config.py`
- `node_agent_runtime/cli.py`

The node runtime should submit the richer enrollment payload described above.

### 7.2 Add work-channel client

Add new modules:

- `node_agent_runtime/work_protocol.py`
- `node_agent_runtime/work_channel.py`
- `node_agent_runtime/work_token.py`

Responsibilities:

- long-poll coordination-layer for work
- validate short-lived work tokens
- enforce work expiry and idempotency
- acknowledge leases
- submit results
- retry safely after transport interruptions

### 7.3 Integrate with task runtime

Extend:

- `node_agent_runtime/task_runtime.py`

The task runtime already enforces:

- activation
- kill-switch
- data boundary
- survival mode

Now it should additionally enforce:

- owner binding
- tenant binding
- policy scope
- pack/version compatibility
- task token validity

### 7.4 Add node readiness state machine

Add a new module:

- `node_agent_runtime/readiness.py`

Recommended states:

- `discovered`
- `enrolled`
- `assigned`
- `healthy`
- `ready`
- `degraded`
- `offline`
- `revoked`

Update status reporting in:

- `node_agent_runtime/status_reporter.py`

to include:

- current readiness state
- queue depth
- last completed work id
- policy profile version
- active pack versions
- watcher node id

### 7.5 Add a long-running worker mode

Extend `node_agent_runtime/cli.py` with a command such as:

- `work-loop`

This process should:

1. validate readiness
2. sync activation status
3. sync kill-switch state
4. post current status
5. lease work
6. execute via `TaskRuntime`
7. post result
8. repeat with backoff

## 8. Security Model

### 8.1 User authentication

Keep current user authentication at the public ingress and orchestrator layer.

The current JWT claim pattern is already present in orchestrator:

- tenant id from `tenant_id`
- user id from `sub`

### 8.2 Node authentication

Keep node enrollment authenticated by:

- cert fingerprint
- mTLS where configured
- protocol version enforcement

Then add:

- renewable node credential id
- revocation support
- short-lived work token validation

### 8.3 Work token

The orchestrator should issue a short-lived signed token for each node-targeted work item.

Minimum claims:

- `work_id`
- `tenant_id`
- `user_id`
- `node_id`
- `task_type`
- `policy_scope`
- `issued_utc`
- `expires_utc`
- `trace_id`
- `request_id`

Node-agent runtime must reject work when:

- token expired
- node id mismatch
- owner mismatch
- tenant mismatch
- policy scope mismatch
- activation invalid
- kill-switch enabled

## 9. Provenance and Audit

Use the existing audit systems rather than creating parallel logging.

### 9.1 Orchestrator

Record route decisions through:

- `journey_events`
- `ledger_events`

Recommended event types:

- `home_node.assignment.resolved`
- `home_node.dispatch.selected`
- `home_node.dispatch.denied`
- `home_node.dispatch.deferred`
- `home_node.dispatch.completed`
- `home_node.fallback.shared_compute`

### 9.2 Coordination layer

Record transport events through:

- `coordination_ledger_events`

Recommended event types:

- `node_work.created`
- `node_work.leased`
- `node_work.acknowledged`
- `node_work.completed`
- `node_work.failed`
- `node_presence.updated`

## 10. API Payload Examples

### 10.1 Enrollment request v2

```json
{
  "node_id": "home-node-01",
  "tenant_id": "tenant-1",
  "owner_user_id": "user-123",
  "device_class": "personal-core",
  "tailscale_ip": "100.120.10.24",
  "watcher_node_id": "home-node-01-watcher",
  "capabilities": {
    "local_retrieval": true,
    "local_model": false,
    "file_custody": true
  },
  "pack_versions": {
    "core-pack": "2026.04.01"
  },
  "policy_profile_version": "1.0",
  "health": {
    "status": "healthy"
  },
  "protocol_version": "1.1",
  "cert_fingerprint": "sha256:..."
}
```

### 10.2 Work item envelope

```json
{
  "work_id": "work_01HTEST",
  "request_id": "req_01HTEST",
  "trace_id": "trace_01HTEST",
  "tenant_id": "tenant-1",
  "user_id": "user-123",
  "node_id": "home-node-01",
  "task_type": "ask.local",
  "execution_state": "pending",
  "policy_scope": "private_local_only",
  "expires_utc": "2026-04-04T22:15:00Z",
  "idempotency_key": "home-node-01:req_01HTEST",
  "token": "<signed work token>",
  "payload": {
    "question": "What happened in my local archive yesterday?",
    "pack_name": "core-pack",
    "pack_anchor": "latest"
  }
}
```

### 10.3 Work result

```json
{
  "work_id": "work_01HTEST",
  "node_id": "home-node-01",
  "status": "completed",
  "result": {
    "answer": "Local answer text",
    "citations": [],
    "trace": {
      "trace_id": "trace_01HTEST"
    }
  },
  "observed_utc": "2026-04-04T22:14:12Z"
}
```

## 11. UI and Client Impact

Update:

- `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/api/types.ts`

and then render explicit execution states in the GPT UI once the backend supports them.

Minimum client behaviors:

- show `pending` when node work has been dispatched but not completed
- show `offline` when no healthy assigned node exists
- show `denied` when policy blocks node-local execution
- show `degraded` when fallback occurred
- show `completed` when node-local execution finished

The UI must continue to talk only to gateway and router-facing APIs. It should never know how to talk to a home node directly.

## 12. Test Plan

### 12.1 Orchestrator tests

Add unit tests under:

- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/HomeNodes`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Workflows`

Key coverage:

- assignment resolution
- watcher fallback
- policy denial
- offline handling
- execution-state mapping
- work-token generation and validation contract
- route provenance emission

If `AskResponseDto` changes, also update contract tests in:

- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.ContractTests`

### 12.2 Coordination-layer tests

Extend:

- `tests/Cwi.Coordination.IntegrationTests/MultiNodeSimulationTests.cs`
- `tests/Cwi.Coordination.IntegrationTests/GrpcCoordinatorTests.cs`

Add new coverage for:

- mailbox lease and ack
- retry-safe completion
- stale lease recovery
- revoked node rejection
- per-node queue isolation

### 12.3 Node-agent runtime tests

Add:

- `src/node-agent-runtime/tests/test_work_channel.py`
- `src/node-agent-runtime/tests/test_work_token.py`
- `src/node-agent-runtime/tests/test_readiness.py`

Extend existing tests for:

- enrollment payload v2
- task runtime owner binding
- policy scope rejection
- work replay rejection
- queue resume after outage

## 13. Rollout Order

### Phase 1

- finalize ADR for outbound-only mailbox transport
- add orchestrator registry and assignment tables
- extend coordination enrollment payload and persistence

### Phase 2

- add coordination mailbox APIs and storage
- add node-agent work loop and work token validation

### Phase 3

- integrate orchestrator ask/action routing with home-node dispatch
- add client execution states
- add end-to-end provenance

### Phase 4

- add watcher fallback
- add degraded routing logic
- add operator views for heartbeat age, queue depth, and assignment state

## 14. Production Readiness Checklist

A home node is only production-ready when all of the following are true:

- Tailscale connected
- node enrolled
- node assigned to a user
- activation valid
- kill-switch clear
- mailbox lease path healthy
- heartbeat fresh
- pack/version state known
- policy profile loaded
- local storage mounted
- queue writable
- provenance emission verified

## 15. Immediate Next Coding Tasks

### Demitrius first

1. Add orchestrator migration `0008_home_node_control_plane.sql`.
2. Add orchestrator home-node entities, repositories, and services.
3. Add a first `HomeNodeRoutingService` and wire it into ask/action workflows.
4. Add execution-state fields to response DTOs.

### Vale first

1. Extend enrollment payload in `enrollment.py`.
2. Add `work_channel.py` and `work_token.py`.
3. Add `work-loop` to `cli.py`.
4. Extend `task_runtime.py` to enforce owner and policy scope.

### Shared integration point

After the above lands, add the coordination mailbox API and integrate the orchestrator dispatch service against it.
