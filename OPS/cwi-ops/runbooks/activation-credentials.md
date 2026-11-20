# Device Identity Provisioning & Rotation

## Provision
1. Generate device-bound credentials.
2. Issue activation handshake artifact.
3. Store credentials in secure storage.

## Rotate
1. Issue new credentials.
2. Update activation handshake.
3. Revoke old credentials.

## Revoke
1. Mark device as revoked.
2. Invalidate outstanding handshakes.
3. Audit and record in incident logs.
