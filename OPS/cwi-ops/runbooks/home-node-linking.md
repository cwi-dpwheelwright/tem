# Runbook: Home Node Linking

## Purpose
Define the canonical node identity, QR payload contract, and customer onboarding
flow for linking a delivered home node to a CWI user account.

## Canonical identifier
Use `node_id` as the primary identifier everywhere in CWI.

- `node_id` is the durable key used by enrollment, activation, control-session
  routing, node work dispatch, metrics ingestion, and home-node assignment.
- `tailscale_ip` is mutable transport metadata and must not be used as the
  primary user-link identifier.
- Hostnames, DNS names, and certificate fingerprints are also mutable or
  rotation-bound and should remain secondary metadata only.

## Recommended `node_id` derivation
For customer home nodes, derive `identity.node_id` from the node's
`/etc/machine-id`.

When using `cwi-node-agent`, you can set `identity.node_id` to `auto` so the
runtime derives this value directly from `/etc/machine-id` on the node.

Algorithm:
1. Read `/etc/machine-id`.
2. Normalize to lowercase ASCII hex with no whitespace.
3. Compute `sha256(machine_id)`.
4. Truncate to the first 32 hex characters.
5. Prefix with `home-`.

Format:
`home-<32 lowercase hex chars>`

Example:
`home-032c311b95ac9a14a81f0b3fa2520ae9`

Rationale:
- Stable across reboots and Tailscale IP changes.
- Available on standard Linux installs without extra hardware dependencies.
- Does not expose the raw machine-id directly in the public QR payload.
- Fits the existing CWI contract, which expects a durable `node_id`.

## Fallback order
If `/etc/machine-id` is unavailable, use the first available fallback below and
persist the derived value in node config/state so it never changes unexpectedly.

1. Linux DMI product UUID from `/sys/class/dmi/id/product_uuid`
2. A generated UUID written once to durable state during provisioning

Do not regenerate a new identifier on each boot.

## QR payload contract
The QR code presented to the customer should encode JSON.

Required fields:
- `node_id`

Recommended fields:
- `identifier_version`

Recommended payload:

```json
{
  "node_id": "home-032c311b95ac9a14a81f0b3fa2520ae9",
  "identifier_version": "machine-id-sha256-v1"
}
```

Notes:
- The GPT site scanner can also accept a raw `node_id` or legacy raw
  `tailscale_ip`, but JSON is the preferred contract because it leaves room for
  versioning and future metadata.
- Do not embed `tenant_id` in the customer QR payload. Tenant is resolved from
  the signed-in account and the node's registered membership during linking.
- Older QR labels that already include `tenant_id` can still be accepted for
  backward compatibility.
- `identifier_version` should reflect the derivation method so future
  migrations remain traceable.
- Keep transport and routing metadata out of the QR payload. `tailscale_ip`,
  `watcher_node_id`, `policy_scope_id`, `local_only`, and
  `remote_fallback_allowed` can all change after provisioning.

## Customer onboarding flow
Target flow for a home-delivered node:

1. Provision the node before shipment.
2. Derive and persist `identity.node_id`.
3. Enroll the node into CWI with that `node_id`.
   The deployed node-agent control service must re-enroll the current canonical
   identity before opening its persistent control channel. This keeps the
   registration contract self-healing after node redeploys, hostname changes,
   QR regeneration, and container restarts.
4. Generate a QR code from the recommended JSON payload and attach it to the
   device or expose it on local setup UI.
5. Customer receives the node at home and powers it on.
6. Customer registers for CWI, signs in to the GPT site, and opens
   `Account settings`.
7. Customer clicks `Link My Node` and scans the QR code.
8. CWI resolves the node by `node_id`, then links that node to the signed-in
   `user_id`.
9. The linked node becomes the user's primary home-node target for local-only or
   hybrid execution policies.

## Ownership and claim semantics
Recommended product behavior:

- A shipped node should be linkable by the intended customer after sign-in.
- Deployment defaults should keep shipped/beta nodes claimable. Do not bake a
  user tenant or owner into the node runtime unless the node is intentionally
  pre-assigned through a governed provisioning flow.
- If the node is unclaimed, the first successful authenticated scan should bind
  the node to that `user_id`.
- If the node is already linked to the same user, scanning should refresh or
  confirm the assignment.
- If the node is already claimed by a different user, linking must fail and
  require support/admin intervention.

## Current implementation note
The public account-linking path in the GPT site accepts the canonical JSON
payload, supports legacy raw `node_id` and raw `tailscale_ip` labels, and can
claim an enrolled unclaimed node on the first successful authenticated scan.

Tenant boundaries are still enforced:
- the signed-in account must belong to the same tenant as the registered node
- a node already claimed by a different user must fail linking
- canonical JSON remains the preferred customer-facing contract

## Operational guidance
- Print or display the QR code using the canonical JSON payload, not just the IP address.
- Keep `node_id` immutable after provisioning.
- Treat `tailscale_ip` as display and legacy fallback metadata only.
- Record the derivation version used at provisioning time.
- If a motherboard swap or OS reinstall changes the machine identity source,
  reissue the node only through an explicit migration flow.
