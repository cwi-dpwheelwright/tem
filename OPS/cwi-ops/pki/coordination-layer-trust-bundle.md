# Coordination Layer Trust Bundle

## Format
- PEM bundle with one or more CA certificates.
- Bundle is signed and stored as `trust-bundle.pem` plus `trust-bundle.sig`.

## Distribution
- Store bundle under `/etc/cwi/pki/trust-bundle.pem`.
- Mount read-only into the coordination-layer container.

## Rotation
1. Publish new bundle and signature to the distribution location.
2. Replace the mounted file on each node.
3. Restart the coordination-layer service to reload trust roots.

## Revocation
- Maintain a CRL/denylist outside of the service and rotate alongside the trust bundle.
