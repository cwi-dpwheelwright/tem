from cwi_etl.cwi_entity_extractor import extract_entities
from cwi_etl.cwi_graph_crosswalk import build_crosswalk
from cwi_etl.cwi_graph_curate import curate_rollup
from cwi_etl.cwi_graph_rollup import build_rollup
from cwi_etl.cwi_relationship_extractor import extract_relationships


OPS_TEXT = "\n".join(
    [
        "Micromat Amperage",
        "Rafter GPM",
    ]
)

MANUAL_TEXT = "Micromat maintenance reference"


def test_rollup_curate_and_crosswalk_pipeline():
    operations_entities = extract_entities(
        OPS_TEXT,
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )
    operations_relationships = extract_relationships(
        OPS_TEXT,
        operations_entities,
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )
    operations_rollup = build_rollup("Operations-Pack", [operations_entities], [operations_relationships])
    curated_operations = curate_rollup(operations_rollup)

    manual_entities = extract_entities(
        MANUAL_TEXT,
        source_document="HDPE Material Test Report 073125V2.docx",
        pack_target="Equipment-Manuals-Pack",
    )
    manual_rollup = build_rollup("Equipment-Manuals-Pack", [manual_entities], [])
    curated_manual = curate_rollup(manual_rollup)

    crosswalk = build_crosswalk([curated_manual, curated_operations])

    assert curated_operations["entity_count"] >= 4
    assert curated_operations["curation_summary"]["curated_entity_count"] == curated_operations["entity_count"]
    assert crosswalk["crosspack_relationship_count"] >= 1
    relationship_types = {item["relationship_type"] for item in crosswalk["relationships"]}
    assert "monitored_in" in relationship_types
