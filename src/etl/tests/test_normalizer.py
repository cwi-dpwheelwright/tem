from cwi_etl.normalizer import normalize_csv, normalize_document, normalize_json
from cwi_etl.parsers import ParsedDocument


def test_normalize_json_dict() -> None:
    doc = normalize_json("doc-1", {"name": "alpha", "score": 1})
    assert len(doc.entities) == 1
    attrs = {attr.name: attr.value for attr in doc.entities[0].attributes}
    assert attrs["name"] == "alpha"


def test_normalize_json_list() -> None:
    doc = normalize_json("doc-1", [{"id": "a"}, {"id": "b"}])
    assert len(doc.entities) == 2
    assert doc.entities[0].entity_id == "a"


def test_normalize_csv_rows() -> None:
    doc = normalize_csv("doc-1", [{"id": "1", "value": "x"}, {"id": "2", "value": "y"}])
    assert len(doc.entities) == 2
    assert doc.entities[0].entity_type == "row"


def test_normalize_document() -> None:
    parsed = ParsedDocument(
        doc_id="doc-1",
        title="Doc",
        mime_type="text/plain",
        text="hello world",
        source_path="/tmp/doc.txt",
    )
    doc = normalize_document(parsed, source_id="docs")
    assert doc.entities[0].entity_type == "document"
    attrs = {attr.name: attr.value for attr in doc.entities[0].attributes}
    assert "content_hash" in attrs
