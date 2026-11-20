import json

from cwi_etl.cwi_graph_backfill import backfill_graph_artifacts


def test_backfill_generates_entity_and_relationship_artifacts(tmp_path):
    input_path = tmp_path / "sample.json"
    input_path.write_text(
        json.dumps(
            {
                "source_document": "QC Operations Parameter Check List.xlsx",
                "text": "Micromat Amperage\nRafter GPM",
            }
        ),
        encoding="utf-8",
    )

    summary = backfill_graph_artifacts([input_path], out_dir=tmp_path / "out", pack_target="Operations-Pack")

    assert summary["entity_generated_count"] >= 2
    assert summary["relationship_generated_count"] >= 1
    assert summary["skipped_count"] == 0
