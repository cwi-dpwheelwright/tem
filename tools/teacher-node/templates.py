import json
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional


def load_template(path: Path) -> Dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def validate_template(template: Dict[str, Any], schema_path: Path) -> None:
    try:
        import jsonschema  # type: ignore
    except ImportError as exc:
        raise RuntimeError("jsonschema is required for template validation.") from exc

    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    jsonschema.validate(instance=template, schema=schema)


def resolve_path(payload: Dict[str, Any], path: str) -> Any:
    current: Any = payload
    for token in _tokenize_path(path):
        if current is None:
            return None
        if isinstance(token, int):
            if not isinstance(current, list) or token >= len(current):
                return None
            current = current[token]
        else:
            if not isinstance(current, dict):
                return None
            current = current.get(token)
    return current


def apply_semantic_mapping(
    template: Dict[str, Any],
    input_data: Dict[str, Any],
    rules: Optional[List[Dict[str, Any]]] = None,
) -> Dict[str, Any]:
    mapping_rules = rules or template.get("mapping_rules") or []
    mapped: Dict[str, Any] = {}
    for rule in mapping_rules:
        field_id = rule.get("field_id")
        source_path = rule.get("source_path")
        if not field_id or not source_path:
            continue
        value = resolve_path(input_data, source_path)
        if value is None:
            value = rule.get("default")
        value = _apply_transform(value, rule.get("transform"), rule)
        if value is None and rule.get("required"):
            raise ValueError(f"required field missing: {field_id}")
        mapped[field_id] = value

    for field in iter_template_fields(template):
        field_id = field.get("field_id")
        if not field_id or field_id in mapped:
            continue
        if "default" in field:
            mapped[field_id] = field.get("default")
    return mapped


def validate_required_fields(template: Dict[str, Any], mapped: Dict[str, Any]) -> List[str]:
    required = set(template.get("constraints", {}).get("required_fields") or [])
    for field in iter_template_fields(template):
        if field.get("required"):
            required.add(field.get("field_id"))
    missing = [field_id for field_id in required if not mapped.get(field_id)]
    return sorted([field_id for field_id in missing if field_id])


def iter_template_fields(template: Dict[str, Any]) -> Iterable[Dict[str, Any]]:
    for section in template.get("sections", []) or []:
        for field in section.get("fields", []) or []:
            yield field


def _apply_transform(value: Any, transform: Optional[str], rule: Dict[str, Any]) -> Any:
    if transform is None:
        return value
    if value is None:
        return None
    if transform == "upper":
        return str(value).upper()
    if transform == "lower":
        return str(value).lower()
    if transform == "title":
        return str(value).title()
    if transform == "stringify":
        return json.dumps(value, ensure_ascii=True)
    if transform == "join":
        if isinstance(value, list):
            sep = rule.get("join_separator") or ", "
            return sep.join(str(item) for item in value)
    return value


def _tokenize_path(path: str) -> Iterable[Any]:
    token = ""
    index = ""
    in_index = False
    for char in path:
        if char == "." and not in_index:
            if token:
                yield token
                token = ""
            continue
        if char == "[":
            in_index = True
            if token:
                yield token
                token = ""
            continue
        if char == "]":
            in_index = False
            if index:
                yield int(index)
                index = ""
            continue
        if in_index:
            index += char
        else:
            token += char
    if token:
        yield token
    if index:
        yield int(index)
