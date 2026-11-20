from cwi_etl.cwi_entity_extractor import extract_entities
from cwi_etl.cwi_relationship_extractor import extract_relationships


SAMPLE_TEXT = "\n".join(
    [
        "Micromat Amperage",
        "Rafter GPM",
        "TDU 500 Process Temp",
    ]
)


def test_entity_extractor_builds_expected_entities():
    artifact = extract_entities(
        SAMPLE_TEXT,
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )

    entity_ids = {item["entity_id"] for item in artifact["entities"]}
    assert "equipment:micromat" in entity_ids
    assert "equipment:rafter" in entity_ids
    assert "equipment:tdu_500" in entity_ids
    assert "metric:amperage" in entity_ids
    assert "metric:gpm" in entity_ids
    assert "metric:process_temp" in entity_ids
    assert "procedure:operations_parameter_check" in entity_ids
    assert artifact["document_class"] == "pm_record"
    assert artifact["document_authority"] == 82


def test_relationship_extractor_builds_direct_relationships():
    entities = extract_entities(
        SAMPLE_TEXT,
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )
    relationships = extract_relationships(
        SAMPLE_TEXT,
        entities,
        source_document="QC Operations Parameter Check List.xlsx",
        pack_target="Operations-Pack",
    )

    pairs = {
        (item["source_entity_id"], item["relationship_type"], item["target_entity_id"])
        for item in relationships["relationships"]
    }
    assert ("equipment:micromat", "measured_by", "metric:amperage") in pairs
    assert ("equipment:rafter", "measured_by", "metric:gpm") in pairs
    assert ("equipment:tdu_500", "measured_by", "metric:process_temp") in pairs
    assert ("procedure:operations_parameter_check", "tracks", "metric:amperage") in pairs
