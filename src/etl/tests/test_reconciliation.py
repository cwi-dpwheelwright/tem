from cwi_etl.ir import IRAttribute, IRDocument, IREntity
from cwi_etl.reconciliation import ConflictResolutionPolicy, reconcile_documents


def test_reconcile_prefers_priority_source() -> None:
    doc_a = IRDocument(
        doc_id="doc-1",
        source_id="primary",
        entities=[
            IREntity(
                entity_id="entity-1",
                entity_type="record",
                source_id="primary",
                attributes=[IRAttribute(name="status", value="open", value_type="string", source_id="primary")],
            )
        ],
    )
    doc_b = IRDocument(
        doc_id="doc-2",
        source_id="secondary",
        entities=[
            IREntity(
                entity_id="entity-1",
                entity_type="record",
                source_id="secondary",
                attributes=[IRAttribute(name="status", value="closed", value_type="string", source_id="secondary")],
            )
        ],
    )

    policy = ConflictResolutionPolicy(source_priority={"primary": 10})
    result = reconcile_documents([doc_a, doc_b], policy)
    status_attr = result.entities[0].attributes[0]
    assert status_attr.value == "open"


def test_reconcile_creates_approval_for_low_confidence() -> None:
    doc_a = IRDocument(
        doc_id="doc-1",
        source_id="a",
        entities=[
            IREntity(
                entity_id="entity-1",
                entity_type="record",
                source_id="a",
                attributes=[IRAttribute(name="amount", value=100, value_type="number", source_id="a")],
            )
        ],
    )
    doc_b = IRDocument(
        doc_id="doc-2",
        source_id="b",
        entities=[
            IREntity(
                entity_id="entity-1",
                entity_type="record",
                source_id="b",
                attributes=[IRAttribute(name="amount", value=120, value_type="number", source_id="b")],
            )
        ],
    )

    policy = ConflictResolutionPolicy(high_risk_attributes=["amount"], confidence_threshold=0.9)
    result = reconcile_documents([doc_a, doc_b], policy)
    assert result.approvals
