from __future__ import annotations

import hashlib
from dataclasses import dataclass
from typing import List


@dataclass(frozen=True)
class Chunk:
    chunk_id: str
    doc_id: str
    index: int
    start: int
    end: int
    content: str
    content_hash: str


def chunk_document(
    doc_id: str,
    text: str,
    max_chars: int = 2000,
    overlap_chars: int = 200,
    min_chunk_chars: int = 200,
) -> List[Chunk]:
    if not doc_id:
        raise ValueError("doc_id is required")
    if max_chars <= 0:
        raise ValueError("max_chars must be positive")
    if overlap_chars < 0:
        raise ValueError("overlap_chars cannot be negative")
    if min_chunk_chars < 0:
        raise ValueError("min_chunk_chars cannot be negative")

    normalized = _normalize_text(text)
    if not normalized:
        return []

    chunks: List[Chunk] = []
    start = 0
    index = 0
    length = len(normalized)
    while start < length:
        end = min(start + max_chars, length)
        end = _backtrack_to_boundary(normalized, start, end, min_chunk_chars)
        if end <= start:
            end = min(start + max_chars, length)

        content = normalized[start:end]
        content_hash = _sha256(content)
        chunk_id = _sha256(f"{doc_id}:{start}:{end}:{content_hash}")

        chunks.append(
            Chunk(
                chunk_id=chunk_id,
                doc_id=doc_id,
                index=index,
                start=start,
                end=end,
                content=content,
                content_hash=content_hash,
            )
        )
        if end >= length:
            break
        start = max(0, end - overlap_chars)
        index += 1

    return chunks


def _normalize_text(text: str) -> str:
    return "\n".join(line.rstrip() for line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n")).strip()


def _backtrack_to_boundary(text: str, start: int, end: int, min_chunk_chars: int) -> int:
    if end - start <= min_chunk_chars:
        return end
    for idx in range(end, start + min_chunk_chars, -1):
        if text[idx - 1].isspace():
            return idx
    return end


def _sha256(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()
