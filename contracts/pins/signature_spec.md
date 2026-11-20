# Pin Signature Specification (Ed25519)

This document defines the canonical signing rules for pin documents.

## Signature Algorithm
- Algorithm: `ed25519-raw`
- Signature encoding: Base64 of the 64-byte Ed25519 signature.

## Signing Payload
1. Build the **payload object** by taking the pin document and **removing**:
   - `signature`
   - `signature_algorithm`
   - `signature_key_id`
2. Canonicalize the payload using **RFC 8785 (JSON Canonicalization Scheme)**.
3. Sign the canonical JSON bytes with the Ed25519 private key.
4. Store the Base64 signature in the `signature` field and set:
   - `signature_algorithm = "ed25519-raw"`
   - `signature_key_id` to the verifying key identifier.

## Verification Rules
- The verifier MUST:
  - Remove signature fields.
  - Canonicalize the payload with RFC 8785.
  - Verify the Ed25519 signature using the public key referenced by `signature_key_id`.
- If verification fails, the pin document MUST be rejected (fail-closed).

## Example (Pseudo)
```
payload = pin_doc - {signature, signature_algorithm, signature_key_id}
canonical = rfc8785(payload)
signature = ed25519_sign(private_key, canonical)
```

## Notes
- Any change to pack hashes or `issued_at_utc` invalidates the signature.
- Backward-compatible verification is permitted only during the republish window.
