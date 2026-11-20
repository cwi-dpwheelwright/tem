from cwi_etl.chunker import chunk_document


def test_chunker_produces_stable_chunks() -> None:
    text = "Alpha bravo charlie.\nDelta echo foxtrot.\nGolf hotel india."
    chunks = chunk_document("doc-1", text, max_chars=20, overlap_chars=5, min_chunk_chars=5)
    assert len(chunks) >= 2
    assert chunks[0].chunk_id == chunk_document("doc-1", text, max_chars=20, overlap_chars=5, min_chunk_chars=5)[0].chunk_id


def test_chunker_returns_empty_for_blank_text() -> None:
    assert chunk_document("doc-1", "   ") == []


def test_chunker_hash_changes_with_content() -> None:
    chunks_a = chunk_document("doc-1", "Alpha bravo charlie.", max_chars=50)
    chunks_b = chunk_document("doc-1", "Alpha bravo charlie!", max_chars=50)
    assert chunks_a[0].chunk_id != chunks_b[0].chunk_id
