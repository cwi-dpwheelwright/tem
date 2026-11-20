from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, Iterable, List

from .cwi_entity_extractor import extract_entities
from .cwi_relationship_extractor import extract_relationships


def backfill_graph_artifacts(
    inputs: Iterable[str | Path],
    *,
    out_dir: str | Path,
    pack_target: str | None = None,
) -> Dict[str, Any]:
    output_root = Path(out_dir)
    entities_dir = output_root / "entities"
    relationships_dir = output_root / "relationships"
    entities_dir.mkdir(parents=True, exist_ok=True)
    relationships_dir.mkdir(parents=True, exist_ok=True)

    entity_generated_count = 0
    relationship_generated_count = 0
    skipped_count = 0
    results: List[Dict[str, Any]] = []

    for path in _expand_inputs(inputs):
        loaded = _load_input(path)
        if loaded["status"] != "ok":
            skipped_count += 1
            results.append({"path": str(path), "status": "skipped", "reason": loaded["reason"]})
            continue

        entity_artifact = extract_entities(
            loaded["text"],
            source_document=loaded["source_document"],
            pack_target=pack_target,
            metadata=loaded["metadata"],
        )
        relationship_artifact = extract_relationships(
            loaded["text"],
            entity_artifact,
            source_document=loaded["source_document"],
            pack_target=pack_target,
            metadata=loaded["metadata"],
        )

        entity_path = entities_dir / f"{entity_artifact['file_id']}.entities.json"
        relationship_path = relationships_dir / f"{relationship_artifact['file_id']}.relationships.json"
        entity_path.write_text(json.dumps(entity_artifact, indent=2) + "\n", encoding="utf-8")
        relationship_path.write_text(json.dumps(relationship_artifact, indent=2) + "\n", encoding="utf-8")

        entity_generated_count += int(entity_artifact.get("entity_count", 0))
        relationship_generated_count += int(relationship_artifact.get("relationship_count", 0))
        results.append(
            {
                "path": str(path),
                "status": "generated",
                "entities": str(entity_path),
                "relationships": str(relationship_path),
            }
        )

    return {
        "manifest_count": len(results),
        "entity_generated_count": entity_generated_count,
        "relationship_generated_count": relationship_generated_count,
        "skipped_count": skipped_count,
        "results": results,
    }


def _expand_inputs(inputs: Iterable[str | Path]) -> List[Path]:
    expanded: List[Path] = []
    for value in inputs:
        path = Path(value)
        if path.is_dir():
            expanded.extend(sorted(candidate for candidate in path.rglob("*") if candidate.is_file()))
        elif path.exists():
            expanded.append(path)
    return expanded


def _load_input(path: Path) -> Dict[str, Any]:
    suffix = path.suffix.lower()
    if suffix in {".txt", ".md"}:
        return {
            "status": "ok",
            "text": path.read_text(encoding="utf-8"),
            "source_document": path.name,
            "metadata": {},
        }
    if suffix == ".json":
        payload = json.loads(path.read_text(encoding="utf-8"))
        text = payload.get("text") or payload.get("content") or payload.get("body")
        if not text:
            return {"status": "skipped", "reason": "missing extracted text"}
        metadata = dict(payload.get("metadata") or {})
        metadata.setdefault("file_id", payload.get("file_id"))
        metadata.setdefault("document_class", payload.get("document_class"))
        metadata.setdefault("document_authority", payload.get("document_authority"))
        return {
            "status": "ok",
            "text": str(text),
            "source_document": str(payload.get("source_document") or payload.get("title") or path.name),
            "metadata": metadata,
        }
    return {"status": "skipped", "reason": "unsupported file type"}
