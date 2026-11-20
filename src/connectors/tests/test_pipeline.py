from cwi_connectors.pipeline import ConnectorPipeline, PipelineConfig, PipelineContext
from cwi_connectors.provenance import ProvenanceSummary


class FakeReconciliation:
    def __init__(self):
        self.records = []

    def submit_batch(self, records):
        self.records.extend(records)
        return []


class FakeVector:
    def __init__(self):
        self.docs = []
        self.chunks = []

    def ingest_document(self, record, idempotency_key=None):
        self.docs.append(record.doc_id)

    def ingest_chunk(self, chunk, parent, idempotency_key=None):
        self.chunks.append(chunk.chunk_id)


class FakeProvenance:
    def __init__(self):
        self.summary = None

    def emit(self, summary: ProvenanceSummary):
        self.summary = summary


def test_pipeline_chunks_and_dedupes():
    vector = FakeVector()
    provenance = FakeProvenance()
    reconciliation = FakeReconciliation()
    pipeline = ConnectorPipeline(
        PipelineConfig(chunk_size=5, chunk_overlap=0, redaction_tokens=[]),
        vector_client=vector,
        provenance_client=provenance,
        reconciliation_client=reconciliation,
    )
    context = PipelineContext(
        tenant_id="tenant-1",
        workspace_id="ws-1",
        connector_id="connector-1",
        connector_type="google_drive",
        source="demo",
        source_priority=50,
        run_id="run-1",
    )
    items = [
        {"doc_id": "doc-1", "title": "Doc", "content": "abcdef", "source": "x"},
        {"doc_id": "doc-1", "title": "Doc", "content": "abcdef", "source": "x"},
    ]
    result = pipeline.run(items, context)
    assert len(result.documents) == 1
    assert len(result.chunks) == 2
    assert len(result.canonical_records) == 1
    assert vector.docs == ["doc-1"]
    assert vector.chunks == ["doc-1:0", "doc-1:1"]
    assert provenance.summary is not None
