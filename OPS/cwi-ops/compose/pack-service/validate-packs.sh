#!/bin/sh
set -eu

if [ -z "${PACKS_DIR:-}" ]; then
  echo "PACKS_DIR is not set"
  exit 1
fi

for pack in enterprise personal; do
  pack_json="${PACKS_DIR}/${pack}/pack.json"
  anchor_txt="${PACKS_DIR}/${pack}/anchor.txt"

  if [ ! -f "${pack_json}" ]; then
    echo "Missing ${pack_json}"
    exit 1
  fi
  if [ ! -f "${anchor_txt}" ]; then
    echo "Missing ${anchor_txt}"
    exit 1
  fi

  version=$(awk -F '"' '/"pack_version"/ {print $4}' "${pack_json}" | head -n 1)
  if [ -z "${version}" ]; then
    echo "Missing pack_version in ${pack_json}"
    exit 1
  fi

  version_dir="${PACKS_DIR}/${pack}/${version}"
  latest_dir="${PACKS_DIR}/${pack}/latest"
  if [ ! -f "${version_dir}/pack.json" ] || [ ! -f "${version_dir}/anchor.txt" ]; then
    echo "Missing versioned artifacts for ${pack} (${version})"
    exit 1
  fi
  if [ ! -f "${latest_dir}/pack.json" ] || [ ! -f "${latest_dir}/anchor.txt" ]; then
    echo "Missing latest artifacts for ${pack}"
    exit 1
  fi
done

echo "OK"
