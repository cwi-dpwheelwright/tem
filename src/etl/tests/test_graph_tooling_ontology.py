from cwi_etl.graph_ontology import build_entity_id, infer_document_class, infer_pack_role, load_graph_ontology


def test_graph_ontology_loads_defaults():
    ontology = load_graph_ontology()

    assert ontology.is_supported_entity_type("equipment")
    assert ontology.is_supported_relationship_type("measured_by")
    assert ontology.resolve_entity_alias("Micromat")[0].canonical_name == "micromat"
    assert build_entity_id("equipment", "micromat") == "equipment:micromat"


def test_document_and_pack_role_inference():
    ontology = load_graph_ontology()

    assert infer_document_class("QC Operations Parameter Check List.xlsx", ontology) == "pm_record"
    assert infer_pack_role("Equipment-Manuals-Pack", ontology) == "manual_reference"
    assert infer_pack_role("Operations-Pack", ontology) == "operations"
