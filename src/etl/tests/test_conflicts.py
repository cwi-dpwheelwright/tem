from cwi_etl.conflicts import detect_conflicts
from cwi_etl.ir import IRAttribute, IRDocument, IREntity


def test_detects_value_conflict() -> None:
    doc_a = IRDocument(
        doc_id="doc-1",
        source_id="source-a",
        entities=[
            IREntity(
                entity_id="entity-1",
                entity_type="record",
                source_id="source-a",
                attributes=[IRAttribute(name="status", value="open", value_type="string", source_id="source-a")],
            )
        ],
    )
    doc_b = IRDocument(
        doc_id="doc-2",
        source_id="source-b",
        entities=[
            IREntity(
                entity_id="entity-1",
                entity_type="record",
                source_id="source-b",
                attributes=[IRAttribute(name="status", value="closed", value_type="string", source_id="source-b")],
            )
        ],
    )

    conflicts = detect_conflicts([doc_a, doc_b])
    assert len(conflicts) == 1
    assert conflicts[0].attribute_name == "status"
