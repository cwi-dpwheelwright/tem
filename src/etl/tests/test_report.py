import json

from cwi_etl.ir import IRAttribute, IRDocument, IREntity
from cwi_etl.reconciliation import ConflictResolutionPolicy, reconcile_documents
from cwi_etl.report import build_report, write_report


def test_report_writes_json(tmp_path) -> None:
    doc = IRDocument(
        doc_id="doc-1",
        source_id="source",
        entities=[
            IREntity(
                entity_id="entity-1",
                entity_type="record",
                source_id="source",
                attributes=[IRAttribute(name="status", value="open", value_type="string", source_id="source")],
            )
        ],
    )
    result = reconcile_documents([doc], ConflictResolutionPolicy())
    report = build_report(result, run_id="run-1")
    path = tmp_path / "report.json"
    write_report(report, str(path))

    payload = json.loads(path.read_text(encoding="utf-8"))
    assert payload["run_id"] == "run-1"
    assert payload["stats"]["entity_count"] == 1
