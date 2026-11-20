# CWI Framework Response Memory Test

This fixture is for response-framework validation after chat-history upload. It is intentionally different from the earlier broad memory smoke fixture.

Uploadable source:

- `cwi_framework_response_memory_upload.md`
- generated ZIP: `CWI_Framework_Response_Memory_Test.zip`

The fixture tests these framework behaviors:

- **Memory reconciliation:** duplicate and superseded memory must not become competing current truth.
- **Conflict resolution:** Riley/Morgan launch ownership must be surfaced as unresolved conflict, not selected as a safe fact.
- **Active context:** Project Cedar Lens, OPEN-CEDAR-441, Demitrius, Chad, and CEDAR-LENS-914 are current active memories.
- **Temporal awareness:** Project Amber Bridge is historical, Project Glass Finch is superseded, Project Dust Archive is deleted, RETRACT-CEDAR-555 is retracted, and TANGERINE-OLD-223 is expired.
- **Contextual scope:** HOME-CEDAR-118 should appear for home/broad profile questions, but should not dominate work-only answers.
- **Comprehension/expression:** self-description must be grounded in activated memory, not generic personality filler.
- **Presentation:** memory-backed responses should report grouped source-pill mode in trace metadata.

The intended run sequence is:

1. Clear superadmin memory and current chat state.
2. Ask the pre-upload control prompts from `expected-framework-prompts.json`.
3. Upload this fixture through the same `/api/ingest/document` contract used by the Import Chat History button.
4. Ask the post-upload prompts from `expected-framework-prompts.json`.
5. Verify both answer text and trace fields.

Run artifacts should be stored under `testing/runs/chat-history-upload/<run-id>/`, with each request folder containing its own `payload.json`, `response.json`, and `meta.json`.
