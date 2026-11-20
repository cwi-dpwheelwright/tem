import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

import graph_cli  # noqa: E402


def test_graph_cli_entity_extract(tmp_path):
    input_path = tmp_path / "sample.txt"
    input_path.write_text("Micromat Amperage\n", encoding="utf-8")
    output_path = tmp_path / "entities.json"

    result = graph_cli.main(
        [
            "entity-extract",
            "--input",
            str(input_path),
            "--out",
            str(output_path),
            "--source-document",
            "QC Operations Parameter Check List.xlsx",
            "--pack-target",
            "Operations-Pack",
        ]
    )

    assert result == 0
    payload = json.loads(output_path.read_text(encoding="utf-8"))
    entity_ids = {item["entity_id"] for item in payload["entities"]}
    assert "equipment:micromat" in entity_ids
    assert "metric:amperage" in entity_ids
