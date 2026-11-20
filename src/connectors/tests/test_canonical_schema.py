from cwi_connectors.canonical_schema import validate_canonical_record


def test_canonical_record_schema_valid():
    payload = {
        "record_id": "rec-1",
        "tenant_id": "tenant-1",
        "user_id": "connector-1",
        "canonical": {"title": "Doc"},
        "sources": [
            {
                "source_id": "connector-1:doc-1",
                "updated_utc": "2026-01-01T00:00:00Z",
                "attributes": {"priority": 50},
            }
        ],
    }
    validate_canonical_record(payload)
