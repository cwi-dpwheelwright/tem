#!/usr/bin/env python
import argparse
import os
import sys
from datetime import datetime, timedelta, timezone

import requests


def parse_args():
    parser = argparse.ArgumentParser(description="Purge expired vectors from a Qdrant shard.")
    parser.add_argument("--base-url", default=None, help="Qdrant base URL (default: VECTOR_SHARD_URL/VECTOR_SHARD_1_URL)")
    parser.add_argument("--collection", default=None, help="Collection name (default: VECTOR_COLLECTION or cwi_corpus)")
    parser.add_argument("--retention-days", type=int, default=None, help="Retention window in days")
    parser.add_argument("--cutoff-epoch", type=int, default=None, help="Override cutoff epoch seconds (UTC)")
    parser.add_argument("--tenant-id", default=None, help="Optional tenant_id filter")
    parser.add_argument("--backup-marker", default=None, help="Path to a marker file proving backup completed")
    parser.add_argument("--dry-run", action="store_true", help="Only count matching points")
    return parser.parse_args()


def build_filter(cutoff_epoch, tenant_id):
    must = [{
        "key": "created_at",
        "range": {"lte": cutoff_epoch}
    }]
    if tenant_id:
        must.append({"key": "tenant_id", "match": {"value": tenant_id}})
    return {"must": must}


def post_json(url, payload):
    response = requests.post(url, json=payload, timeout=20)
    return response.status_code, response.text


def main():
    args = parse_args()
    base_url = args.base_url or os.environ.get("VECTOR_SHARD_URL") or os.environ.get("VECTOR_SHARD_1_URL")
    if not base_url:
        print("Missing base URL (use --base-url or VECTOR_SHARD_URL/VECTOR_SHARD_1_URL).", file=sys.stderr)
        return 2
    base_url = base_url.rstrip("/")

    collection = args.collection or os.environ.get("VECTOR_COLLECTION") or "cwi_corpus"

    if args.cutoff_epoch is not None:
        cutoff_epoch = args.cutoff_epoch
    else:
        retention_days = args.retention_days
        if retention_days is None:
            retention_days = int(os.environ.get("VECTOR_RETENTION_DAYS", "90"))
        cutoff_epoch = int((datetime.now(tz=timezone.utc) - timedelta(days=retention_days)).timestamp())

    if args.backup_marker and not os.path.exists(args.backup_marker):
        print(f"Backup marker not found: {args.backup_marker}", file=sys.stderr)
        return 3

    filter_payload = build_filter(cutoff_epoch, args.tenant_id)
    count_url = f"{base_url}/collections/{collection}/points/count"
    count_payload = {"filter": filter_payload, "exact": True}
    status, body = post_json(count_url, count_payload)
    if status not in (200, 201):
        print(f"Count failed: {status} {body}", file=sys.stderr)
        return 4

    print(f"Count response: {body}")
    if args.dry_run:
        return 0

    delete_url = f"{base_url}/collections/{collection}/points/delete"
    delete_payload = {"filter": filter_payload, "wait": True}
    status, body = post_json(delete_url, delete_payload)
    if status not in (200, 201, 202):
        print(f"Delete failed: {status} {body}", file=sys.stderr)
        return 5

    print(f"Delete response: {body}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
