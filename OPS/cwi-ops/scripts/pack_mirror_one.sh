#!/usr/bin/env bash
set -euo pipefail

PACK="${1:?Usage: pack_mirror_one.sh <pack_id>}"

if [[ ! "${PACK}" =~ ^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$ ]]; then
  echo "ERROR: PACK must be an alphanumeric identifier (got: ${PACK})" >&2
  exit 2
fi

TEACHER_ALIAS="${TEACHER_ALIAS:-teacher}"
BUCKET="${BUCKET:-cwi-packs-published}"

LIVE_BASE="${LIVE_BASE:-/srv/cwi/packs}"
STAGING_BASE="${STAGING_BASE:-/srv/cwi/packs_staging}"
VERSIONS_BASE="${VERSIONS_BASE:-/srv/cwi/packs_versions}"

VERIFY_SCRIPT="${VERIFY_SCRIPT:-/opt/cwi/bin/verify_pack_anchor.py}"
LOCK_PATH="${LOCK_PATH:-/var/lock/cwi-pack-mirror.lock}"

PACK_UID="${PACK_UID:-1654}"
PACK_GID="${PACK_GID:-1654}"

if command -v flock >/dev/null 2>&1; then
  exec 9>"${LOCK_PATH}"
  if ! flock -n 9; then
    echo "INFO: mirror already running (lock: ${LOCK_PATH})."
    exit 0
  fi
fi

if ! command -v mc >/dev/null 2>&1; then
  echo "ERROR: mc is required on PATH." >&2
  exit 2
fi

mkdir -p "${LIVE_BASE}/${PACK}" "${STAGING_BASE}/${PACK}" "${VERSIONS_BASE}/${PACK}"

HASH="$(mc cat "${TEACHER_ALIAS}/${BUCKET}/${PACK}/latest.txt" | tr -d '\r\n\t ')"
if [[ -z "${HASH}" ]]; then
  echo "ERROR: latest.txt empty for ${PACK}" >&2
  exit 2
fi

HASH="$(echo "${HASH}" | tr 'A-F' 'a-f')"
if [[ ! "${HASH}" =~ ^[0-9a-f]{64}$ ]]; then
  echo "ERROR: latest.txt is not a 64-hex SHA-256 digest: ${HASH}" >&2
  exit 2
fi

echo "INFO: ${PACK} latest hash = ${HASH}"

rm -rf "${STAGING_BASE:?}/${PACK:?}/"*
mc cp "${TEACHER_ALIAS}/${BUCKET}/${PACK}/${HASH}/pack.json" "${STAGING_BASE}/${PACK}/pack.json"
mc cp "${TEACHER_ALIAS}/${BUCKET}/${PACK}/${HASH}/anchor.txt" "${STAGING_BASE}/${PACK}/anchor.txt"

if [[ ! -x "${VERIFY_SCRIPT}" ]]; then
  echo "ERROR: verify script not executable: ${VERIFY_SCRIPT}" >&2
  exit 2
fi

"${VERIFY_SCRIPT}" \
  "${STAGING_BASE}/${PACK}/pack.json" \
  "${STAGING_BASE}/${PACK}/anchor.txt" \
  --expected "${HASH}"

CUR_HASH_FILE="${LIVE_BASE}/${PACK}/current_hash.txt"
CUR_HASH=""
if [[ -f "${CUR_HASH_FILE}" ]]; then
  CUR_HASH="$(tr -d '\r\n\t ' < "${CUR_HASH_FILE}")"
fi

if [[ "${CUR_HASH}" == "${HASH}" ]] && \
   [[ -f "${LIVE_BASE}/${PACK}/latest/pack.json" ]] && \
   [[ -f "${LIVE_BASE}/${PACK}/latest/anchor.txt" ]]; then
  echo "INFO: ${PACK} already at ${HASH}; nothing to do."
  exit 0
fi

TS="$(date -u +%Y%m%d_%H%M%S)"

if [[ -n "${CUR_HASH}" ]]; then
  SNAP_DIR="${VERSIONS_BASE}/${PACK}/${TS}_${CUR_HASH}"
  mkdir -p "${SNAP_DIR}"
  cp -a "${LIVE_BASE}/${PACK}/." "${SNAP_DIR}/" 2>/dev/null || true
fi

mkdir -p "${LIVE_BASE}/${PACK}/latest" "${LIVE_BASE}/${PACK}/${HASH}"

cp -a "${STAGING_BASE}/${PACK}/pack.json" "${LIVE_BASE}/${PACK}/pack.json"
cp -a "${STAGING_BASE}/${PACK}/anchor.txt" "${LIVE_BASE}/${PACK}/anchor.txt"

cp -a "${STAGING_BASE}/${PACK}/pack.json" "${LIVE_BASE}/${PACK}/${HASH}/pack.json"
cp -a "${STAGING_BASE}/${PACK}/anchor.txt" "${LIVE_BASE}/${PACK}/${HASH}/anchor.txt"

cp -a "${STAGING_BASE}/${PACK}/pack.json" "${LIVE_BASE}/${PACK}/latest/pack.json"
cp -a "${STAGING_BASE}/${PACK}/anchor.txt" "${LIVE_BASE}/${PACK}/latest/anchor.txt"

printf "%s\n" "${HASH}" > "${CUR_HASH_FILE}"

chown -R "${PACK_UID}:${PACK_GID}" "${LIVE_BASE}/${PACK}" "${STAGING_BASE}/${PACK}" 2>/dev/null || true
find "${LIVE_BASE}/${PACK}" -type d -exec chmod 0755 {} \; 2>/dev/null || true
find "${LIVE_BASE}/${PACK}" -type f -exec chmod 0644 {} \; 2>/dev/null || true
find "${STAGING_BASE}/${PACK}" -type d -exec chmod 0755 {} \; 2>/dev/null || true
find "${STAGING_BASE}/${PACK}" -type f -exec chmod 0644 {} \; 2>/dev/null || true

LIVE_BASE="${LIVE_BASE}" PACK="${PACK}" HASH="${HASH}" python3 - <<'PY'
import json
import os
import pathlib

live_base = pathlib.Path(os.environ["LIVE_BASE"])
pack_id = os.environ["PACK"]
hash_value = os.environ["HASH"]

idx_path = live_base / "index.json"
if idx_path.exists():
    try:
        idx = json.loads(idx_path.read_text(encoding="utf-8"))
    except Exception:
        idx = {"packs": []}
else:
    idx = {"packs": []}

packs_list = idx.get("packs", [])
packs_map = {}
for p in packs_list:
    if isinstance(p, dict) and p.get("pack_id"):
        packs_map[p["pack_id"]] = p

entry = packs_map.get(pack_id, {})
versions = entry.get("versions", [])
for v in (hash_value, "latest"):
    if v not in versions:
        versions.append(v)

entry.update({
    "pack_id": pack_id,
    "name": entry.get("name") or pack_id,
    "latest": "latest",
    "versions": versions,
    "visibility": entry.get("visibility", "public"),
    "retired": entry.get("retired", False),
    "retired_utc": entry.get("retired_utc"),
})
packs_map[pack_id] = entry

ordered = []
for pid in ["enterprise", "personal"]:
    if pid in packs_map:
        ordered.append(packs_map.pop(pid))
for pid in sorted(packs_map.keys()):
    ordered.append(packs_map[pid])

out = {"packs": ordered}
tmp_path = idx_path.with_name(idx_path.name + ".tmp")
tmp_path.write_text(json.dumps(out, indent=2) + "\n", encoding="utf-8")
os.replace(tmp_path, idx_path)
PY

chmod 0644 "${LIVE_BASE}/index.json"
chown "${PACK_UID}:${PACK_GID}" "${LIVE_BASE}/index.json" 2>/dev/null || true

echo "OK: mirrored ${PACK} -> ${HASH} (${TS})"
