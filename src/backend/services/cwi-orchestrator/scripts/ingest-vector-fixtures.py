#!/usr/bin/env python
import argparse
import hashlib
import json
import math
import os
import sys
from datetime import datetime, timezone
from urllib.parse import urlparse

import requests


def _normalize_vector(values):
    norm = math.sqrt(sum(v * v for v in values))
    if norm <= 0.0:
        return values
    return [v / norm for v in values]


def embed_text(text, dimension):
    if dimension <= 0:
        raise ValueError("Embedding dimension must be positive.")
    digest = hashlib.sha256((text or "").encode("utf-8")).digest()
    vec = [0.0] * dimension
    for i, byte in enumerate(digest):
        vec[i % dimension] += (byte / 255.0) - 0.5
    return _normalize_vector(vec)


def http_json(method, url, payload=None):
    parsed = urlparse(url)
    if parsed.scheme not in ("http", "https"):
        raise ValueError(f"Unsupported URL scheme for {url}")
    resp = requests.request(method, url, json=payload, timeout=15)
    return resp.status_code, resp.text


def to_epoch_seconds(value):
    if value is None:
        return int(datetime.now(tz=timezone.utc).timestamp())
    if isinstance(value, (int, float)):
        return int(value)
    if isinstance(value, str):
        try:
            normalized = value.strip()
            if normalized.endswith("Z"):
                normalized = normalized[:-1] + "+00:00"
            return int(datetime.fromisoformat(normalized).timestamp())
        except ValueError:
            return int(datetime.now(tz=timezone.utc).timestamp())
    return int(datetime.now(tz=timezone.utc).timestamp())


def ensure_collection(shard_url, collection, dimension):
    status, _ = http_json("GET", f"{shard_url}/collections/{collection}")
    if status == 200:
        return
    if status != 404:
        raise RuntimeError(f"Failed to check collection {collection} on {shard_url} (status {status}).")
    payload = {"vectors": {"size": dimension, "distance": "Cosine"}}
    status, body = http_json("PUT", f"{shard_url}/collections/{collection}", payload)
    if status not in (200, 201):
        raise RuntimeError(f"Failed to create collection {collection} on {shard_url}: {status} {body}")


def upsert_points(shard_url, collection, points):
    payload = {"points": points}
    status, body = http_json("PUT", f"{shard_url}/collections/{collection}/points?wait=true", payload)
    if status not in (200, 201):
        raise RuntimeError(f"Upsert failed on {shard_url}: {status} {body}")


def parse_args():
    parser = argparse.ArgumentParser(description="Ingest vector_corpus.jsonl fixtures into Qdrant shards.")
    parser.add_argument("--input", required=True, help="Path to vector_corpus.jsonl")
    parser.add_argument(
        "--collection",
        default=None,
        help="Qdrant collection name (default: VECTOR_COLLECTION or cwi_corpus)",
    )
    parser.add_argument("--dimension", type=int, default=None, help="Embedding dimension (default: env or 64)")
    parser.add_argument("--shard-url", action="append", default=[], help="Qdrant base URL (repeatable)")
    parser.add_argument("--batch-size", type=int, default=64, help="Batch size for upserts")
    return parser.parse_args()


def main():
    args = parse_args()
    shard_urls = [u.rstrip("/") for u in args.shard_url if u]
    if not shard_urls:
        env_urls = [
            os.environ.get("VECTOR_SHARD_1_URL", ""),
            os.environ.get("VECTOR_SHARD_2_URL", ""),
        ]
        shard_urls = [u.rstrip("/") for u in env_urls if u]
    if not shard_urls:
        print("No shard URLs provided (use --shard-url or VECTOR_SHARD_1_URL/2).", file=sys.stderr)
        return 2

    collection = args.collection
    if not collection:
        collection = os.environ.get("VECTOR_COLLECTION", "").strip()
    if not collection:
        collection = "cwi_corpus"

    dimension = args.dimension
    if dimension is None:
        dimension = int(os.environ.get("VECTOR_EMBEDDING_DIMENSION", "64"))

    for shard in shard_urls:
        ensure_collection(shard, collection, dimension)

    default_tenant = os.environ.get("VECTOR_TENANT_ID", "tenant-1")
    points = []
    ingested = 0
    with open(args.input, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            record = json.loads(line)
            doc_id = record.get("doc_id")
            chunk_id = record.get("chunk_id")
            content = record.get("content") or record.get("text") or ""
            if not doc_id or not chunk_id:
                raise ValueError("Each record must contain doc_id and chunk_id.")
            vector = embed_text(content, dimension)
            point_id = f"{doc_id}:{chunk_id}"
            payload = {
                "doc_id": doc_id,
                "chunk_id": chunk_id,
                "content": content,
                "created_at": to_epoch_seconds(record.get("created_at")),
                "tenant_id": record.get("tenant_id") or default_tenant,
                "pack_id": record.get("pack_id"),
                "pack_hash": record.get("pack_hash"),
                "pack_version": record.get("pack_version"),
            }
            points.append({"id": point_id, "vector": vector, "payload": payload})
            if len(points) >= args.batch_size:
                for shard in shard_urls:
                    upsert_points(shard, collection, points)
                ingested += len(points)
                points = []

    if points:
        for shard in shard_urls:
            upsert_points(shard, collection, points)
        ingested += len(points)

    print(f"Ingested {ingested} vectors into {len(shard_urls)} shard(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
