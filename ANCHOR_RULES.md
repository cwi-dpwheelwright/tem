# Pack Anchor Rules

This document defines the canonicalization and hashing rules used to compute
`anchor.txt` for each pack's `pack.json`. The goal is a stable, deterministic
SHA-256 hash across platforms and serializers.

## Canonicalization

1. Parse `pack.json` as JSON.
2. Canonicalize recursively:
   - **Objects:** sort properties by name (ordinal, ascending). Emit `{` then
     `"name":value` pairs joined by `,`, then `}`. Property names are JSON-
     serialized strings.
   - **Arrays:** preserve order. Emit `[` then canonicalized items joined by
     `,`, then `]`.
   - **Strings:** emit JSON-serialized strings (including escapes).
   - **Numbers:** emit the raw JSON number text.
   - **Booleans:** emit `true` or `false`.
   - **Null:** emit `null`.

No additional whitespace is inserted.

## Hashing

Compute SHA-256 over the UTF-8 bytes of the canonicalized JSON string.
`anchor.txt` is the lowercase hex encoding of the digest.
