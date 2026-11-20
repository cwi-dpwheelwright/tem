# Coordination Layer Threat Model Notes

## Key risks
- Unauthorized node enrollment.
- Certificate theft or replay.
- Stale or revoked nodes continuing to participate.

## Mitigations
- mTLS required for all coordination endpoints.
- Enrollment/rotation/revocation APIs enforce protocol version checks.
- Trust bundle rotation and revocation supported via signed bundles.

## Rotation / Revocation
- Rotate node certs via `/api/membership/rotate`.
- Revoke nodes via `/api/membership/revoke`.
- Update trust bundles and CRLs on rotation windows.
