#!/usr/bin/env bash
set -euo pipefail

PUBLIC_BASE_URL="${CWI_PUBLIC_BASE_URL:-https://cwigpt.com}"
QR_PAYLOAD_URL="${CWI_QR_PAYLOAD_URL:-}"
TENANT_ID="${CWI_TENANT_ID:-}"
USERNAME="${CWI_USERNAME:-}"
PASSWORD="${CWI_PASSWORD:-}"

if [[ -z "$QR_PAYLOAD_URL" || -z "$TENANT_ID" || -z "$USERNAME" || -z "$PASSWORD" ]]; then
  cat <<'USAGE' >&2
Usage:
  CWI_QR_PAYLOAD_URL=http://100.122.244.48:8088/payload.json \
  CWI_TENANT_ID=tenant-1 \
  CWI_USERNAME=edge-agent \
  CWI_PASSWORD=... \
  ./OPS/cwi-ops/ci/smoke-home-node-link.sh
USAGE
  exit 1
fi

require_bin() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_bin curl
require_bin jq

echo "Fetching QR payload from $QR_PAYLOAD_URL"
QR_PAYLOAD="$(curl -fsS "$QR_PAYLOAD_URL")"
QR_NODE_ID="$(printf '%s' "$QR_PAYLOAD" | jq -r '.node_id // empty')"
QR_TENANT_ID="$(printf '%s' "$QR_PAYLOAD" | jq -r '.tenant_id // empty')"

if [[ -z "$QR_NODE_ID" ]]; then
  echo "QR payload is missing node_id." >&2
  exit 1
fi

if [[ -n "$QR_TENANT_ID" && "$QR_TENANT_ID" != "$TENANT_ID" ]]; then
  echo "Tenant mismatch: QR payload is for $QR_TENANT_ID but login is configured for $TENANT_ID." >&2
  exit 1
fi

echo "Logging in as $USERNAME in tenant $TENANT_ID"
ACCESS_TOKEN="$(
  curl -fsS "$PUBLIC_BASE_URL/auth/login" \
    -H 'content-type: application/json' \
    --data "$(jq -nc --arg tenant_id "$TENANT_ID" --arg username "$USERNAME" --arg password "$PASSWORD" '{tenant_id:$tenant_id, username:$username, password:$password}')" \
  | jq -r '.access_token'
)"

if [[ -z "$ACCESS_TOKEN" || "$ACCESS_TOKEN" == "null" ]]; then
  echo "Login failed; no access token returned." >&2
  exit 1
fi

echo "Checking existing account link"
BEFORE_LINK="$(
  curl -fsS "$PUBLIC_BASE_URL/api/account/home-node" \
    -H "authorization: Bearer $ACCESS_TOKEN"
)"

printf '%s\n' "$BEFORE_LINK" | jq '{
  linked,
  tenant_id,
  user_id,
  current_node_id: (.node.node_id // null)
}'

REQUEST_BODY="$(jq -nc --arg payload "$QR_PAYLOAD" '{qr_code_payload:$payload}')"

echo "Submitting QR payload to account/home-node"
LINK_RESPONSE="$(
  curl -fsS "$PUBLIC_BASE_URL/api/account/home-node" \
    -H 'content-type: application/json' \
    -H "authorization: Bearer $ACCESS_TOKEN" \
    --data "$REQUEST_BODY"
)"

LINKED_NODE_ID="$(printf '%s' "$LINK_RESPONSE" | jq -r '.node.node_id // empty')"
MATCHED_VIA="$(printf '%s' "$LINK_RESPONSE" | jq -r '.matched_via // empty')"
LINKED="$(printf '%s' "$LINK_RESPONSE" | jq -r '.linked')"

if [[ "$LINKED" != "true" || "$LINKED_NODE_ID" != "$QR_NODE_ID" ]]; then
  echo "Link smoke test failed." >&2
  printf '%s\n' "$LINK_RESPONSE" | jq >&2
  exit 1
fi

echo
echo "Home-node link smoke test passed."
printf '%s\n' "$LINK_RESPONSE" | jq '{
  linked,
  matched_via,
  node_id: (.node.node_id // null),
  tenant_id,
  user_id
}'

if [[ -n "$MATCHED_VIA" ]]; then
  echo "Matched via: $MATCHED_VIA"
fi
