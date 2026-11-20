#!/usr/bin/env python
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ETL_SRC = ROOT / "src" / "etl"
if ETL_SRC.exists():
    sys.path.insert(0, str(ETL_SRC))


def cmd_ontology(args: argparse.Namespace) -> int:
    from cwi_etl.graph_ontology import DEFAULT_ONTOLOGY_PATH, build_entity_id, canonical_snake_case, load_graph_ontology

    ontology = load_graph_ontology(args.path or DEFAULT_ONTOLOGY_PATH)
    if args.entity_type and args.name:
        canonical = canonical_snake_case(args.name)
        payload = {
            "entity_type": canonical_snake_case(args.entity_type),
            "canonical_name": canonical,
            "entity_id": build_entity_id(args.entity_type, canonical),
        }
    else:
        payload = {
            "path": str(args.path or DEFAULT_ONTOLOGY_PATH),
            "entity_type_count": len(ontology.entity_index),
            "relationship_type_count": len(ontology.relationship_types),
        }
    print(json.dumps(payload, indent=2))
    return 0


def cmd_entity_extract(args: argparse.Namespace) -> int:
    from cwi_etl.cwi_entity_extractor import extract_entities

    text, source_document, metadata = _load_text_like_input(Path(args.input))
    artifact = extract_entities(
        text,
        source_document=args.source_document or source_document,
        pack_target=args.pack_target,
        metadata=metadata,
    )
    _write_json(Path(args.out), artifact)
    print(args.out)
    return 0


def cmd_relationship_extract(args: argparse.Namespace) -> int:
    from cwi_etl.cwi_relationship_extractor import extract_relationships

    text, source_document, metadata = _load_text_like_input(Path(args.input))
    entity_artifact = json.loads(Path(args.entities).read_text(encoding="utf-8"))
    artifact = extract_relationships(
        text,
        entity_artifact,
        source_document=args.source_document or source_document,
        pack_target=args.pack_target,
        metadata=metadata,
    )
    _write_json(Path(args.out), artifact)
    print(args.out)
    return 0


def cmd_rollup(args: argparse.Namespace) -> int:
    from cwi_etl.cwi_graph_rollup import build_rollup

    entity_artifacts = [_load_json(Path(path)) for path in args.entities]
    relationship_artifacts = [_load_json(Path(path)) for path in args.relationships]
    artifact = build_rollup(args.pack_target, entity_artifacts, relationship_artifacts)
    _write_json(Path(args.out), artifact)
    print(args.out)
    return 0


def cmd_curate(args: argparse.Namespace) -> int:
    from cwi_etl.cwi_graph_curate import curate_rollup

    artifact = curate_rollup(_load_json(Path(args.input)))
    _write_json(Path(args.out), artifact)
    print(args.out)
    return 0


def cmd_crosswalk(args: argparse.Namespace) -> int:
    from cwi_etl.cwi_graph_crosswalk import build_crosswalk

    rollups = [_load_json(Path(path)) for path in args.inputs]
    artifact = build_crosswalk(rollups)
    _write_json(Path(args.out), artifact)
    print(args.out)
    return 0


def cmd_age_write(args: argparse.Namespace) -> int:
    from cwi_etl.cwi_age_graph_writer import build_age_artifacts

    rollups = [_load_json(Path(path)) for path in args.rollups]
    crosswalk = _load_json(Path(args.crosswalk)) if args.crosswalk else None
    sql_text, summary = build_age_artifacts(
        rollups,
        crosswalk,
        graph_name=args.graph_name,
        tenant_id=args.tenant_id,
    )
    out_sql = Path(args.out_sql)
    out_sql.parent.mkdir(parents=True, exist_ok=True)
    out_sql.write_text(sql_text, encoding="utf-8")
    _write_json(Path(args.out_summary), summary)
    print(json.dumps({"sql": args.out_sql, "summary": args.out_summary}, indent=2))
    return 0


def cmd_backfill(args: argparse.Namespace) -> int:
    from cwi_etl.cwi_graph_backfill import backfill_graph_artifacts

    summary = backfill_graph_artifacts(args.inputs, out_dir=args.out_dir, pack_target=args.pack_target)
    if args.summary:
        _write_json(Path(args.summary), summary)
    print(json.dumps(summary, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Teacher-side graph tooling CLI")
    sub = parser.add_subparsers(dest="command", required=True)

    ontology = sub.add_parser("ontology")
    ontology.add_argument("--path")
    ontology.add_argument("--entity-type")
    ontology.add_argument("--name")
    ontology.set_defaults(func=cmd_ontology)

    entity_extract = sub.add_parser("entity-extract")
    entity_extract.add_argument("--input", required=True)
    entity_extract.add_argument("--out", required=True)
    entity_extract.add_argument("--source-document")
    entity_extract.add_argument("--pack-target")
    entity_extract.set_defaults(func=cmd_entity_extract)

    relationship_extract = sub.add_parser("relationship-extract")
    relationship_extract.add_argument("--input", required=True)
    relationship_extract.add_argument("--entities", required=True)
    relationship_extract.add_argument("--out", required=True)
    relationship_extract.add_argument("--source-document")
    relationship_extract.add_argument("--pack-target")
    relationship_extract.set_defaults(func=cmd_relationship_extract)

    rollup = sub.add_parser("rollup")
    rollup.add_argument("--pack-target", required=True)
    rollup.add_argument("--entities", required=True, nargs="+")
    rollup.add_argument("--relationships", required=True, nargs="+")
    rollup.add_argument("--out", required=True)
    rollup.set_defaults(func=cmd_rollup)

    curate = sub.add_parser("curate")
    curate.add_argument("--input", required=True)
    curate.add_argument("--out", required=True)
    curate.set_defaults(func=cmd_curate)

    crosswalk = sub.add_parser("crosswalk")
    crosswalk.add_argument("--inputs", required=True, nargs="+")
    crosswalk.add_argument("--out", required=True)
    crosswalk.set_defaults(func=cmd_crosswalk)

    age_write = sub.add_parser("age-write")
    age_write.add_argument("--rollups", required=True, nargs="+")
    age_write.add_argument("--crosswalk")
    age_write.add_argument("--graph-name", default="cwi_graph")
    age_write.add_argument("--tenant-id", default="global")
    age_write.add_argument("--out-sql", required=True)
    age_write.add_argument("--out-summary", required=True)
    age_write.set_defaults(func=cmd_age_write)

    backfill = sub.add_parser("backfill")
    backfill.add_argument("--inputs", required=True, nargs="+")
    backfill.add_argument("--out-dir", required=True)
    backfill.add_argument("--pack-target")
    backfill.add_argument("--summary")
    backfill.set_defaults(func=cmd_backfill)

    args = parser.parse_args(argv)
    return int(args.func(args))


def _load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def _write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def _load_text_like_input(path: Path) -> tuple[str, str, dict]:
    if path.suffix.lower() == ".json":
        payload = _load_json(path)
        text = payload.get("text") or payload.get("content") or payload.get("body")
        if not text:
            raise RuntimeError(f"JSON input missing text/content/body: {path}")
        metadata = dict(payload.get("metadata") or {})
        if payload.get("document_class"):
            metadata["document_class"] = payload["document_class"]
        if payload.get("document_authority") is not None:
            metadata["document_authority"] = payload["document_authority"]
        return str(text), str(payload.get("source_document") or path.name), metadata
    return path.read_text(encoding="utf-8"), path.name, {}


if __name__ == "__main__":
    raise SystemExit(main())
