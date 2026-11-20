#!/bin/sh
set -eu

if [ -z "${PACKS_DIR:-}" ]; then
  echo "PACKS_DIR is not set"
  exit 1
fi

for pack in enterprise personal; do
  if [ ! -f "${PACKS_DIR}/${pack}/pack.json" ]; then
    echo "Missing ${PACKS_DIR}/${pack}/pack.json"
    exit 1
  fi
  if [ ! -f "${PACKS_DIR}/${pack}/anchor.txt" ]; then
    echo "Missing ${PACKS_DIR}/${pack}/anchor.txt"
    exit 1
  fi
done

echo "OK"
