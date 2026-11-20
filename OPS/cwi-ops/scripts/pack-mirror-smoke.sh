#!/usr/bin/env bash
set -euo pipefail

PACKS="${PACKS:-enterprise personal}"
TEACHER_MINIO_BASE="${TEACHER_MINIO_BASE:-}"
PACK_SERVICE_URL="${PACK_SERVICE_URL:-}"
GATEWAY_BASE_URL="${GATEWAY_BASE_URL:-}"
TEACHER_ALIAS="${TEACHER_ALIAS:-teacher}"
BUCKET="${BUCKET:-cwi-packs-published}"
RUN_MIRROR="${RUN_MIRROR:-false}"
MIRROR_SCRIPT="${MIRROR_SCRIPT:-/opt/cwi/bin/pack_mirror_one.sh}"

usage() {
  cat <<EOF
Usage: pack-mirror-smoke.sh

Required env vars:
  TEACHER_MINIO_BASE   (e.g., http://100.87.205.59:9000)
  PACK_SERVICE_URL     (e.g., http://localhost:18083)

Optional env vars:
  GATEWAY_BASE_URL     (e.g., http://100.124.140.66)
  TEACHER_ALIAS        (default: teacher)
  BUCKET               (default: cwi-packs-published)
  PACKS                (default: "enterprise personal")
  RUN_MIRROR           (default: false)
  MIRROR_SCRIPT        (default: /opt/cwi/bin/pack_mirror_one.sh)
EOF
}

if [[ -z "${TEACHER_MINIO_BASE}" || -z "${PACK_SERVICE_URL}" ]]; then
  usage
  exit 2
fi

if ! command -v mc >/dev/null 2>&1; then
  echo "ERROR: mc not found on PATH." >&2
  exit 2
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "ERROR: curl not found on PATH." >&2
  exit 2
fi

echo "INFO: checking Teacher MinIO health"
code="$(curl -s -o /dev/null -w "%{http_code}" "${TEACHER_MINIO_BASE}/minio/health/live")"
if [[ "${code}" != "200" ]]; then
  echo "ERROR: MinIO health check failed (HTTP ${code})" >&2
  exit 2
fi

for pack in ${PACKS}; do
  echo "INFO: checking ${pack} latest.txt"
  latest="$(mc cat "${TEACHER_ALIAS}/${BUCKET}/${pack}/latest.txt" | tr -d '\r\n\t ')"
  if [[ ! "${latest}" =~ ^[0-9a-fA-F]{64}$ ]]; then
    echo "ERROR: latest.txt not a 64-hex digest for ${pack}: ${latest}" >&2
    exit 2
  fi
  mc stat "${TEACHER_ALIAS}/${BUCKET}/${pack}/${latest}/pack.json" >/dev/null
  mc stat "${TEACHER_ALIAS}/${BUCKET}/${pack}/${latest}/anchor.txt" >/dev/null

  if [[ "${RUN_MIRROR}" == "true" ]]; then
    if [[ ! -x "${MIRROR_SCRIPT}" ]]; then
      echo "ERROR: mirror script not executable: ${MIRROR_SCRIPT}" >&2
      exit 2
    fi
    "${MIRROR_SCRIPT}" "${pack}"
  fi
done

echo "INFO: checking Pack Service"
curl -s "${PACK_SERVICE_URL}/packs" | head -n 20 >/dev/null

for pack in ${PACKS}; do
  curl -s "${PACK_SERVICE_URL}/packs/${pack}/latest/pack.json" | head -n 5 >/dev/null
done

if [[ -n "${GATEWAY_BASE_URL}" ]]; then
  echo "INFO: checking Gateway /packs"
  curl -s "${GATEWAY_BASE_URL}/packs" | head -n 20 >/dev/null
  for pack in ${PACKS}; do
    curl -s "${GATEWAY_BASE_URL}/packs/${pack}/latest/pack.json" | head -n 5 >/dev/null
  done
fi

echo "OK: pack mirror smoke checks passed."
