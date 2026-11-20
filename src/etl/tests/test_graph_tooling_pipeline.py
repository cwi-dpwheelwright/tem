from cwi_etl.pipeline import (
    build_age_load_artifacts,
    build_crosspack_graph_edges,
    build_pack_graph_rollup,
    curate_pack_graph_rollup,
    extract_document_graph_artifacts,
)


def test_pipeline_helpers_build_end_to_end_artifacts():
    ops_entities, ops_relationships = extract_document_graph_artifacts(
        "Micromat Amperage\nRafter GPM",
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )
    manual_entities, manual_relationships = extract_document_graph_artifacts(
        "Micromat maintenance reference",
        source_document="HDPE Material Test Report 073125V2.docx",
        pack_target="Equipment-Manuals-Pack",
    )

    ops_rollup = curate_pack_graph_rollup(
        build_pack_graph_rollup("Operations-Pack", [ops_entities], [ops_relationships])
    )
    manual_rollup = curate_pack_graph_rollup(
        build_pack_graph_rollup("Equipment-Manuals-Pack", [manual_entities], [manual_relationships])
    )

    crosswalk = build_crosspack_graph_edges([manual_rollup, ops_rollup])
    sql_text, summary = build_age_load_artifacts([manual_rollup, ops_rollup], crosswalk, graph_name="cwi_graph")

    assert crosswalk["crosspack_relationship_count"] >= 1
    assert "MERGE" in sql_text
    assert summary["rollup_file_count"] == 2
