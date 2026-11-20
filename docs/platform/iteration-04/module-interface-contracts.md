# Module Interface Contracts Index (Iteration 04)

This index lists the current module interface contracts and their fixtures.

## Core Service Contracts
- `contracts/openapi/cwi-coordination-layer.openapi_10H_frozen.yaml`
- `contracts/grpc/cwi-coordination-layer.proto`
- `contracts/openapi/cwi-identity-server.openapi_10H_frozen.yaml`
- `contracts/openapi/cwi-router.openapi_10H_frozen.yaml`
- `contracts/openapi/cwi-orchestrator.openapi_10H_frozen.yaml`

## JSON Schemas
- `contracts/json-schema/ledger_event_canonical.json`
- `contracts/json-schema/decision_event.json`
- `contracts/json-schema/guardrails_constraints.json`
- `contracts/json-schema/telemetry_schema.json`
- `contracts/json-schema/ingest_document_schema.json`
- `contracts/json-schema/pack_schema.json`
- `contracts/json-schema/teacher_template_schema.json`
- `contracts/json-schema/retention_policy.json`
- `contracts/json-schema/document_record.json`
- `contracts/json-schema/canonical_record.json`
- `contracts/json-schema/source_metadata.json`
- `contracts/json-schema/multi_source_alignment.schema.json`
- `contracts/json-schema/activation_handshake.json`
- `contracts/json-schema/activation_signature_payload.json`
- `contracts/json-schema/activation_state.json`
- `contracts/json-schema/activation_status.json`
- `contracts/json-schema/degradation_event.json`
- `contracts/json-schema/kill_switch_state.json`
- `contracts/json-schema/auth_token_response.json`
- `contracts/json-schema/auth_token_claims.json`
- `contracts/json-schema/operator_memory_correlation.json`
- `contracts/json-schema/ownership_succession_plan.json`
- `contracts/json-schema/procedure_document.json`
- `contracts/json-schema/mesh_placement_recommendation.json`
- `contracts/json-schema/mesh_placement_metrics.json`
- `contracts/json-schema/network_assessment.json`
- `contracts/json-schema/route_attestation_envelope.json`
- `contracts/json-schema/dual_format_report.json`
- `contracts/json-schema/reconciliation_report.json`
- `contracts/json-schema/inference_discrepancy_event.json`
- `contracts/json-schema/remote_consent_response.json`

## Fixtures
- `contracts/fixtures/orchestrator/*`
- `contracts/fixtures/orchestrator/decision_event.sample.json`
- `contracts/fixtures/router/*`
- `contracts/fixtures/router/network-assessment.sample.json`
- `contracts/fixtures/router/route-attestation-envelope.sample.json`
- `contracts/fixtures/coordination/*`
- `contracts/fixtures/drive/*`
- `contracts/fixtures/identity/*`
- `contracts/fixtures/outlook/*`
- `contracts/fixtures/smartsheet/*`
- `contracts/fixtures/multi_source_alignment.sample.json`
- `contracts/fixtures/activation_handshake.sample.json`
- `contracts/fixtures/activation_signature_payload.sample.json`
- `contracts/fixtures/activation_state.sample.json`
- `contracts/fixtures/activation_status.sample.json`
- `contracts/fixtures/degradation_event.sample.json`
- `contracts/fixtures/kill_switch_state.sample.json`
- `contracts/fixtures/operator_memory_correlation.sample.json`
- `contracts/fixtures/ownership_succession_plan.sample.json`
- `contracts/fixtures/procedure_document.sample.json`
- `contracts/fixtures/teacher-node/teacher_template.sample.json`
- `contracts/fixtures/mesh_placement_recommendation.sample.json`
- `contracts/fixtures/mesh_placement_metrics.sample.json`
- `contracts/fixtures/dual_format_report.sample.json`
- `contracts/fixtures/reconciliation_report.sample.json`
- `contracts/fixtures/inference_discrepancy_event.sample.json`
- `contracts/fixtures/remote_consent_response.sample.json`

## Usage
Each module should reference the relevant contract(s) above and add fixtures for any new fields or invariants.
