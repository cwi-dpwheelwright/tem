from __future__ import annotations

from dataclasses import asdict
from typing import Any, Dict, Iterable, List

from cwi_etl.identity import build_content_identity
from cwi_etl.ir import IRAttribute, IRDocument, IREntity
from cwi_etl.parsers import ParsedDocument


def _infer_type(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "bool"
    if isinstance(value, int) or isinstance(value, float):
        return "number"
    if isinstance(value, list):
        return "list"
    if isinstance(value, dict):
        return "object"
    return "string"


def _attribute(name: str, value: Any, source_id: str, confidence: float = 1.0) -> IRAttribute:
    return IRAttribute(
        name=name,
        value=value,
        value_type=_infer_type(value),
        source_id=source_id,
        confidence=confidence,
        metadata={},
    )


def normalize_json(
    doc_id: str,
    data: Any,
    source_id: str = "json",
    entity_type: str = "record",
) -> IRDocument:
    if data is None:
        return IRDocument(doc_id=doc_id, source_id=source_id)

    entities: List[IREntity] = []
    if isinstance(data, list):
        for index, item in enumerate(data):
            if isinstance(item, dict):
                entity_id = item.get("id") or item.get("entity_id") or f"{doc_id}:{index}"
            else:
                entity_id = f"{doc_id}:{index}"
            entities.append(_entity_from_mapping(str(entity_id), entity_type, item, source_id))
    elif isinstance(data, dict):
        entities.append(_entity_from_mapping(doc_id, entity_type, data, source_id))
    else:
        entities.append(
            IREntity(
                entity_id=doc_id,
                entity_type=entity_type,
                attributes=[_attribute("value", data, source_id)],
                source_id=source_id,
            )
        )

    return IRDocument(doc_id=doc_id, source_id=source_id, entities=entities)


def normalize_csv(
    doc_id: str,
    rows: Iterable[Dict[str, Any]],
    source_id: str = "csv",
    entity_type: str = "row",
) -> IRDocument:
    entities: List[IREntity] = []
    for index, row in enumerate(rows):
        entity_id = row.get("id") or row.get("entity_id") or f"{doc_id}:{index}"
        entities.append(_entity_from_mapping(str(entity_id), entity_type, row, source_id))

    return IRDocument(doc_id=doc_id, source_id=source_id, entities=entities)


def normalize_document(parsed: ParsedDocument, source_id: str = "document") -> IRDocument:
    identity = build_content_identity(source_id, parsed.text, doc_id=parsed.doc_id, metadata=parsed.metadata)
    attributes = [
        _attribute("title", parsed.title, source_id),
        _attribute("mime_type", parsed.mime_type, source_id),
        _attribute("content_hash", identity.content_hash, source_id),
        _attribute("stable_id", identity.stable_id, source_id),
        _attribute("version_hash", identity.version_hash, source_id),
        _attribute("content_length", len(parsed.text), source_id),
    ]
    entity = IREntity(
        entity_id=identity.stable_id,
        entity_type="document",
        attributes=attributes,
        source_id=source_id,
        provenance=[parsed.source_path],
    )

    return IRDocument(
        doc_id=identity.doc_id,
        source_id=source_id,
        entities=[entity],
        metadata={
            "source_path": parsed.source_path,
            "parsed": True,
            "identity": asdict(identity),
        },
    )


def _entity_from_mapping(
    entity_id: str,
    entity_type: str,
    mapping: Any,
    source_id: str,
) -> IREntity:
    attributes: List[IRAttribute] = []
    if isinstance(mapping, dict):
        for key, value in mapping.items():
            attributes.append(_attribute(str(key), value, source_id))
    else:
        attributes.append(_attribute("value", mapping, source_id))

    return IREntity(
        entity_id=entity_id,
        entity_type=entity_type,
        attributes=attributes,
        source_id=source_id,
    )
