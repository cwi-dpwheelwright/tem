from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Dict, Iterable, List


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, *, length: int = 12) -> str:
    digest = hashlib.sha256(value.encode("utf-8")).hexdigest()
    return digest[:length]


def canonical_snake_case(value: str) -> str:
    lowered = value.strip().lower()
    lowered = re.sub(r"[^\w\s]+", " ", lowered)
    lowered = re.sub(r"[\s_]+", "_", lowered)
    lowered = re.sub(r"_+", "_", lowered)
    return lowered.strip("_")


def stable_file_id(source_document: str) -> str:
    stem = canonical_snake_case(source_document.rsplit(".", 1)[0]) or "document"
    return f"{stem}-{stable_hash(source_document, length=8)}"


def stable_relationship_id(
    source_entity_id: str,
    relationship_type: str,
    target_entity_id: str,
    *extras: str,
) -> str:
    base = "->".join(
        [source_entity_id, canonical_snake_case(relationship_type), target_entity_id, *[extra for extra in extras if extra]]
    )
    return canonical_snake_case(base)


@dataclass(frozen=True)
class GraphEntityRecord:
    entity_id: str
    entity_type: str
    canonical_name: str
    source_text: str
    confidence: float
    file_id: str
    source_document: str
    timestamp: str
    document_class: str
    document_authority: int
    pack_target: str | None = None
    domain: str | None = None
    aliases: List[str] = field(default_factory=list)
    metadata: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        payload = {
            "entity_id": self.entity_id,
            "entity_type": self.entity_type,
            "canonical_name": self.canonical_name,
            "source_text": self.source_text,
            "confidence": round(float(self.confidence), 4),
            "file_id": self.file_id,
            "source_document": self.source_document,
            "timestamp": self.timestamp,
            "document_class": self.document_class,
            "document_authority": self.document_authority,
        }
        if self.pack_target:
            payload["pack_target"] = self.pack_target
        if self.domain:
            payload["domain"] = self.domain
        if self.aliases:
            payload["aliases"] = sorted({alias for alias in self.aliases if alias})
        if self.metadata:
            payload["metadata"] = self.metadata
        return payload

    @staticmethod
    def from_dict(payload: Dict[str, Any]) -> "GraphEntityRecord":
        return GraphEntityRecord(
            entity_id=str(payload["entity_id"]),
            entity_type=str(payload["entity_type"]),
            canonical_name=str(payload["canonical_name"]),
            source_text=str(payload.get("source_text") or payload.get("canonical_name") or ""),
            confidence=float(payload.get("confidence", 1.0)),
            file_id=str(payload["file_id"]),
            source_document=str(payload["source_document"]),
            timestamp=str(payload["timestamp"]),
            document_class=str(payload.get("document_class") or "unknown"),
            document_authority=int(payload.get("document_authority", 0)),
            pack_target=str(payload["pack_target"]) if payload.get("pack_target") else None,
            domain=str(payload["domain"]) if payload.get("domain") else None,
            aliases=[str(alias) for alias in payload.get("aliases", [])],
            metadata=dict(payload.get("metadata") or {}),
        )


@dataclass(frozen=True)
class GraphRelationshipRecord:
    relationship_id: str
    source_entity_id: str
    target_entity_id: str
    relationship_type: str
    evidence_text: str
    confidence: float
    file_id: str
    source_document: str
    timestamp: str
    document_class: str
    document_authority: int
    relationship_mode: str = "direct"
    source_pack_target: str | None = None
    target_pack_target: str | None = None
    metadata: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        payload = {
            "relationship_id": self.relationship_id,
            "source_entity_id": self.source_entity_id,
            "target_entity_id": self.target_entity_id,
            "relationship_type": self.relationship_type,
            "evidence_text": self.evidence_text,
            "confidence": round(float(self.confidence), 4),
            "file_id": self.file_id,
            "source_document": self.source_document,
            "timestamp": self.timestamp,
            "document_class": self.document_class,
            "document_authority": self.document_authority,
            "relationship_mode": self.relationship_mode,
        }
        if self.source_pack_target:
            payload["source_pack_target"] = self.source_pack_target
        if self.target_pack_target:
            payload["target_pack_target"] = self.target_pack_target
        if self.metadata:
            payload["metadata"] = self.metadata
        return payload

    @staticmethod
    def from_dict(payload: Dict[str, Any]) -> "GraphRelationshipRecord":
        return GraphRelationshipRecord(
            relationship_id=str(payload["relationship_id"]),
            source_entity_id=str(payload["source_entity_id"]),
            target_entity_id=str(payload["target_entity_id"]),
            relationship_type=str(payload["relationship_type"]),
            evidence_text=str(payload.get("evidence_text") or ""),
            confidence=float(payload.get("confidence", 1.0)),
            file_id=str(payload["file_id"]),
            source_document=str(payload["source_document"]),
            timestamp=str(payload["timestamp"]),
            document_class=str(payload.get("document_class") or "unknown"),
            document_authority=int(payload.get("document_authority", 0)),
            relationship_mode=str(payload.get("relationship_mode") or "direct"),
            source_pack_target=str(payload["source_pack_target"]) if payload.get("source_pack_target") else None,
            target_pack_target=str(payload["target_pack_target"]) if payload.get("target_pack_target") else None,
            metadata=dict(payload.get("metadata") or {}),
        )


@dataclass(frozen=True)
class DocumentEntityArtifact:
    file_id: str
    source_document: str
    generated_at: str
    document_class: str
    document_authority: int
    entity_count: int
    entities: List[GraphEntityRecord]

    def to_dict(self) -> Dict[str, Any]:
        entities = sorted(self.entities, key=lambda item: (item.entity_type, item.canonical_name, item.entity_id))
        return {
            "file_id": self.file_id,
            "source_document": self.source_document,
            "generated_at": self.generated_at,
            "document_class": self.document_class,
            "document_authority": self.document_authority,
            "entity_count": self.entity_count,
            "entities": [entity.to_dict() for entity in entities],
        }


@dataclass(frozen=True)
class DocumentRelationshipArtifact:
    file_id: str
    source_document: str
    generated_at: str
    document_class: str
    document_authority: int
    relationship_count: int
    relationships: List[GraphRelationshipRecord]

    def to_dict(self) -> Dict[str, Any]:
        relationships = sorted(
            self.relationships,
            key=lambda item: (item.relationship_type, item.source_entity_id, item.target_entity_id, item.relationship_id),
        )
        return {
            "file_id": self.file_id,
            "source_document": self.source_document,
            "generated_at": self.generated_at,
            "document_class": self.document_class,
            "document_authority": self.document_authority,
            "relationship_count": self.relationship_count,
            "relationships": [relationship.to_dict() for relationship in relationships],
        }


def load_entity_records(payload: Dict[str, Any]) -> List[GraphEntityRecord]:
    return [GraphEntityRecord.from_dict(item) for item in payload.get("entities", [])]


def load_relationship_records(payload: Dict[str, Any]) -> List[GraphRelationshipRecord]:
    return [GraphRelationshipRecord.from_dict(item) for item in payload.get("relationships", [])]


def dedupe_entities(records: Iterable[GraphEntityRecord]) -> List[GraphEntityRecord]:
    deduped: Dict[str, GraphEntityRecord] = {}
    for record in records:
        existing = deduped.get(record.entity_id)
        if existing is None or record.confidence > existing.confidence:
            deduped[record.entity_id] = record
    return sorted(deduped.values(), key=lambda item: (item.entity_type, item.canonical_name, item.entity_id))


def dedupe_relationships(records: Iterable[GraphRelationshipRecord]) -> List[GraphRelationshipRecord]:
    deduped: Dict[str, GraphRelationshipRecord] = {}
    for record in records:
        existing = deduped.get(record.relationship_id)
        if existing is None or record.confidence > existing.confidence:
            deduped[record.relationship_id] = record
    return sorted(
        deduped.values(),
        key=lambda item: (item.relationship_type, item.source_entity_id, item.target_entity_id, item.relationship_id),
    )
