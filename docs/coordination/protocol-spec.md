# Coordination Protocol Specification (Decentralized Baseline)

## Status
Draft

## Scope
Defines the baseline coordination protocol for membership, rounds, artifact publication,
and health signaling. This spec is contract-first and aligns with the coordination-layer
API models and threat model notes.

## Versioning
All coordination requests include the `X-Coordination-Protocol` header with the
protocol version string (example: `1.0`). Servers reject mismatched versions.

## Message Schemas

### Membership Join (Enrollment)
Request:
```json
{
  "node_id": "node-1",
  "cert_fingerprint": "hexsha256",
  "protocol_version": "1.0"
}
```
Response:
```json
{
  "node_id": "node-1",
  "status": "enrolled"
}
```

### Membership Leave (Revocation)
Request:
```json
{
  "node_id": "node-1",
  "reason": "operator_revoked"
}
```
Response:
```json
{
  "node_id": "node-1",
  "status": "revoked"
}
```

### Membership Rotation
Request:
```json
{
  "node_id": "node-1",
  "new_cert_fingerprint": "hexsha256"
}
```
Response:
```json
{
  "node_id": "node-1",
  "status": "rotated"
}
```

### Round Start
Request:
```json
{
  "name": "round-1"
}
```
Response:
```json
{
  "round_id": "uuid",
  "name": "round-1",
  "status": "active"
}
```

### Round End
Request:
```json
{
  "round_id": "uuid",
  "status": "closed"
}
```
Response:
```json
{
  "round_id": "uuid",
  "status": "closed"
}
```

### Artifact Publish
Request:
```json
{
  "artifact_id": "artifact-1",
  "round_id": "uuid",
  "node_id": "node-1",
  "sha256": "hexsha256",
  "uri": "s3://bucket/path",
  "size_bytes": 12345,
  "created_utc": "2026-02-01T00:00:00Z"
}
```
Response:
```json
{
  "artifact_id": "artifact-1",
  "status": "accepted"
}
```

### Heartbeat
Request:
```json
{
  "node_id": "node-1",
  "status": "healthy"
}
```
Response:
```json
{
  "node_id": "node-1",
  "status": "ok"
}
```

## State Machines

### Membership Lifecycle
- Pending -> Enrolled -> Active
- Active -> Rotated (cert updated) -> Active
- Active -> Revoked (by admin) -> Revoked

### Round Lifecycle
- Created -> Active -> Closed
- Closed -> Archived (optional retention cleanup)

## Threat Model Summary
Primary risks:
- Unauthorized enrollment or stale nodes continuing to participate.
- Credential theft or replay of enrollment/rotation requests.
- Tampering with coordination events.

Mitigations:
- mTLS on all coordination endpoints with protocol version enforcement.
- Certificate rotation and revocation endpoints with audit logging.
- Append-only, hash-chained ledger for critical coordination events.

See `docs/ops/coordination-threat-model.md` for additional notes.

## Security Model
- Node identity is established via mTLS client certificates.
- Enrollment establishes node identity; rotation updates trust anchors.
- Revocation is explicit and enforced via CRL/trust bundle updates.
- Authorization is policy-driven per endpoint, with break-glass controls for
  admin actions and explicit audit logging of governance changes.
