# Internal PKI (Sanitized)

This folder contains sanitized templates for generating a local CA and service certificates. Replace placeholders before production use.

## Files
- `openssl-ca.cnf`: CA configuration template
- `openssl-service.cnf`: Service certificate template (uses SANs)

## Trust bundle distribution model
- Root CA is offline and not distributed to services.
- An intermediate CA signs service certificates.
- Services receive a **trust bundle** containing:
  - Intermediate CA certificate
  - Optional root CA certificate (if required by your runtime)
- Trust bundles are stored as read-only mounted secrets and rotated on a fixed schedule.
- Revocation is distributed via a signed CRL or OCSP responder (choose one and document).

## Storage model
- Place generated artifacts under `/opt/cwi/pki/<role>/` on each node.
- Mount trust bundles read-only into containers (e.g., `/etc/cwi/pki/trust-bundle.pem`).
- Keep private keys in OS key stores or secret managers; never commit keys to git.
