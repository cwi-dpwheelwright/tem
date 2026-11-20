from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, List, Tuple

import yaml

from .graph_artifacts import canonical_snake_case


DEFAULT_ONTOLOGY_PATH = Path(__file__).resolve().parent / "ontology" / "graph_ontology.yaml"


@dataclass(frozen=True)
class OntologyEntityDefinition:
    entity_type: str
    canonical_name: str
    aliases: Tuple[str, ...]
    domain: str | None = None


@dataclass(frozen=True)
class GraphOntology:
    entity_index: Dict[str, List[OntologyEntityDefinition]]
    relationship_types: Dict[str, Dict[str, Any]]
    rejection_terms: Tuple[str, ...]
    document_class_patterns: Dict[str, Tuple[str, ...]]
    document_authority: Dict[str, int]
    pack_role_hints: Dict[str, Tuple[str, ...]]

    def all_entity_definitions(self) -> List[OntologyEntityDefinition]:
        items: List[OntologyEntityDefinition] = []
        for values in self.entity_index.values():
            items.extend(values)
        return items

    def is_supported_entity_type(self, entity_type: str) -> bool:
        return canonical_snake_case(entity_type) in self.entity_index

    def is_supported_relationship_type(self, relationship_type: str) -> bool:
        return canonical_snake_case(relationship_type) in self.relationship_types

    def resolve_entity_alias(
        self,
        text: str,
        *,
        allowed_types: Iterable[str] | None = None,
    ) -> List[OntologyEntityDefinition]:
        normalized = normalize_alias_key(text)
        if not normalized:
            return []
        allowed = {canonical_snake_case(item) for item in (allowed_types or [])}
        matches: List[OntologyEntityDefinition] = []
        for entity_type, definitions in self.entity_index.items():
            if allowed and entity_type not in allowed:
                continue
            for definition in definitions:
                if normalized in {normalize_alias_key(alias) for alias in definition.aliases}:
                    matches.append(definition)
        return matches


def load_graph_ontology(path: str | Path | None = None) -> GraphOntology:
    ontology_path = Path(path) if path else DEFAULT_ONTOLOGY_PATH
    raw = yaml.safe_load(ontology_path.read_text(encoding="utf-8")) or {}

    entity_index: Dict[str, List[OntologyEntityDefinition]] = {}
    for entity_type, config in (raw.get("entity_types") or {}).items():
        canonical_type = canonical_snake_case(str(entity_type))
        definitions: List[OntologyEntityDefinition] = []
        for canonical_name, payload in (config.get("values") or {}).items():
            canonical = canonical_snake_case(str(canonical_name))
            aliases = tuple(dict.fromkeys([canonical_name, canonical, *(payload.get("aliases") or [])]))
            definitions.append(
                OntologyEntityDefinition(
                    entity_type=canonical_type,
                    canonical_name=canonical,
                    aliases=aliases,
                    domain=payload.get("domain"),
                )
            )
        entity_index[canonical_type] = sorted(definitions, key=lambda item: item.canonical_name)

    relationship_types = {
        canonical_snake_case(name): dict(config or {})
        for name, config in (raw.get("relationship_types") or {}).items()
    }
    rejection_terms = tuple(canonical_snake_case(value) for value in (raw.get("rejection_terms") or []))

    document_class_patterns = {
        canonical_snake_case(name): tuple(str(item).lower() for item in patterns)
        for name, patterns in (raw.get("document_class_patterns") or {}).items()
    }
    document_authority = {
        canonical_snake_case(name): int(value)
        for name, value in (raw.get("document_authority") or {}).items()
    }
    pack_role_hints = {
        canonical_snake_case(name): tuple(str(item).lower() for item in hints)
        for name, hints in (raw.get("pack_role_hints") or {}).items()
    }

    return GraphOntology(
        entity_index=entity_index,
        relationship_types=relationship_types,
        rejection_terms=rejection_terms,
        document_class_patterns=document_class_patterns,
        document_authority=document_authority,
        pack_role_hints=pack_role_hints,
    )


def normalize_alias_key(value: str) -> str:
    return canonical_snake_case(value)


def build_entity_id(entity_type: str, canonical_name: str) -> str:
    return f"{canonical_snake_case(entity_type)}:{canonical_snake_case(canonical_name)}"


def reject_candidate(value: str, ontology: GraphOntology) -> bool:
    normalized = canonical_snake_case(value)
    if not normalized:
        return True
    return normalized in set(ontology.rejection_terms)


def infer_document_class(
    source_document: str,
    ontology: GraphOntology,
    *,
    metadata: Dict[str, Any] | None = None,
) -> str:
    provided = canonical_snake_case(str((metadata or {}).get("document_class") or ""))
    if provided:
        return provided

    lowered = source_document.lower()
    for doc_class, patterns in ontology.document_class_patterns.items():
        if any(pattern in lowered for pattern in patterns):
            return doc_class
    return "unknown"


def document_authority_for_class(
    document_class: str,
    ontology: GraphOntology,
    *,
    metadata: Dict[str, Any] | None = None,
) -> int:
    provided = (metadata or {}).get("document_authority")
    if provided is not None:
        return int(provided)
    return int(ontology.document_authority.get(canonical_snake_case(document_class), 60))


def infer_pack_role(pack_target: str | None, ontology: GraphOntology) -> str | None:
    if not pack_target:
        return None
    lowered = pack_target.lower()
    for role, hints in ontology.pack_role_hints.items():
        if any(hint in lowered for hint in hints):
            return role
    return None


def entity_mentions_in_text(
    text: str,
    ontology: GraphOntology,
    *,
    allowed_types: Iterable[str] | None = None,
) -> List[Tuple[OntologyEntityDefinition, str]]:
    lowered = text.lower()
    allowed = {canonical_snake_case(item) for item in (allowed_types or [])}
    matches: Dict[Tuple[str, str], Tuple[OntologyEntityDefinition, str]] = {}
    for definition in ontology.all_entity_definitions():
        if allowed and definition.entity_type not in allowed:
            continue
        for alias in sorted(definition.aliases, key=len, reverse=True):
            alias_text = str(alias).strip()
            if not alias_text:
                continue
            pattern = r"(?<![a-z0-9])" + re.escape(alias_text.lower()) + r"(?![a-z0-9])"
            if re.search(pattern, lowered):
                key = (definition.entity_type, definition.canonical_name)
                matches[key] = (definition, alias_text)
                break
    return sorted(matches.values(), key=lambda item: (item[0].entity_type, item[0].canonical_name))
