from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List


@dataclass(frozen=True)
class IRAttribute:
    name: str
    value: Any
    value_type: str
    source_id: str
    confidence: float = 1.0
    metadata: Dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class IREntity:
    entity_id: str
    entity_type: str
    attributes: List[IRAttribute]
    source_id: str
    provenance: List[str] = field(default_factory=list)


@dataclass(frozen=True)
class IREvent:
    event_id: str
    event_type: str
    timestamp_utc: str
    entity_id: str
    payload: Dict[str, Any]
    source_id: str


@dataclass(frozen=True)
class IRProvenanceLink:
    from_id: str
    to_id: str
    relation: str
    source_id: str


@dataclass(frozen=True)
class IRDocument:
    doc_id: str
    source_id: str
    entities: List[IREntity] = field(default_factory=list)
    events: List[IREvent] = field(default_factory=list)
    attributes: List[IRAttribute] = field(default_factory=list)
    provenance_links: List[IRProvenanceLink] = field(default_factory=list)
    metadata: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "doc_id": self.doc_id,
            "source_id": self.source_id,
            "entities": [entity_to_dict(entity) for entity in self.entities],
            "events": [event_to_dict(event) for event in self.events],
            "attributes": [attribute_to_dict(attr) for attr in self.attributes],
            "provenance_links": [provenance_to_dict(link) for link in self.provenance_links],
            "metadata": self.metadata,
        }


def attribute_to_dict(attribute: IRAttribute) -> Dict[str, Any]:
    return {
        "name": attribute.name,
        "value": attribute.value,
        "value_type": attribute.value_type,
        "source_id": attribute.source_id,
        "confidence": attribute.confidence,
        "metadata": attribute.metadata,
    }


def entity_to_dict(entity: IREntity) -> Dict[str, Any]:
    return {
        "entity_id": entity.entity_id,
        "entity_type": entity.entity_type,
        "attributes": [attribute_to_dict(attr) for attr in entity.attributes],
        "source_id": entity.source_id,
        "provenance": entity.provenance,
    }


def event_to_dict(event: IREvent) -> Dict[str, Any]:
    return {
        "event_id": event.event_id,
        "event_type": event.event_type,
        "timestamp_utc": event.timestamp_utc,
        "entity_id": event.entity_id,
        "payload": event.payload,
        "source_id": event.source_id,
    }


def provenance_to_dict(link: IRProvenanceLink) -> Dict[str, Any]:
    return {
        "from_id": link.from_id,
        "to_id": link.to_id,
        "relation": link.relation,
        "source_id": link.source_id,
    }
