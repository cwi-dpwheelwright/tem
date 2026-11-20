from cwi_etl.cwi_age_graph_writer import build_age_artifacts
from cwi_etl.cwi_entity_extractor import extract_entities
from cwi_etl.cwi_graph_crosswalk import build_crosswalk
from cwi_etl.cwi_graph_curate import curate_rollup
from cwi_etl.cwi_graph_rollup import build_rollup
from cwi_etl.cwi_relationship_extractor import extract_relationships


def test_age_writer_generates_sql_and_summary():
    operations_text = "Micromat Amperage"
    operations_entities = extract_entities(
        operations_text,
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )
    operations_relationships = extract_relationships(
        operations_text,
        operations_entities,
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )
    operations_rollup = curate_rollup(build_rollup("Operations-Pack", [operations_entities], [operations_relationships]))

    manual_entities = extract_entities(
        "Micromat maintenance reference",
        source_document="HDPE Material Test Report 073125V2.docx",
        pack_target="Equipment-Manuals-Pack",
    )
    manual_rollup = curate_rollup(build_rollup("Equipment-Manuals-Pack", [manual_entities], []))

    crosswalk = build_crosswalk([manual_rollup, operations_rollup])
    sql_text, summary = build_age_artifacts(
        [manual_rollup, operations_rollup],
        crosswalk,
        graph_name="cwi_graph",
    )

    assert "create_graph('cwi_graph')" in sql_text
    assert "MERGE (n:Equipment:GraphEntity" in sql_text
    assert "MERGE (from)-[r:MEASURED_BY" in sql_text
    assert summary["graph_name"] == "cwi_graph"
    assert summary["vertex_count"] >= 1
    assert summary["edge_count"] >= 1
    assert summary["crosspack_loaded"] is True
