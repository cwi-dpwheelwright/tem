# Chat History Upload Smoke Test - 20260502T194727Z

Result: PASS

## Scope
- Endpoint under test: `https://cwigpt.com/api/ingest/document`
- Upload path matches the UI button behavior: ZIP entry expansion -> structured transcript chunks -> JSON `POST /api/ingest/document` requests.
- Source ZIP SHA-256: `a7311f7a8cabd7f4603c9ed190368177eee4c4e4d5520bd56497857dbd15bdb2`
- Message count: 2416
- Generated chunks: 2

## Pass / Fail
- upload_all_chunks_accepted: `True`
- importing_user_answers_changed_with_memory_markers: `True`
- importing_user_trace_memory_used: `True`
- control_user_did_not_receive_imported_markers: `True`
- overall_pass: `True`
- control_user_marker_hits: `[]`

## Upload Responses
- Chunk 1: status `200`, ok `True`, doc `web-upload-chat-history-smoke-20260502t194727z-file-001-input-zip-conversations-json-part-00001`, trace `9a87faecf3434f7dae75af6ee0d445e2`
- Chunk 2: status `200`, ok `True`, doc `web-upload-chat-history-smoke-20260502t194727z-file-001-input-zip-conversations-json-part-00002`, trace `ce08c790b83647708b768f4645f53a99`

## Importing User Verification
### what do you know about me?
- status: `200`, memory_used: `True`, route: `activated_memory_retrieval`, confidence: `0.86`
- marker_hits: `['Project Quartz Lantern', 'OPEN-LOOP-GAMMA-17', 'OPEN-LOOP-NEBULA-88', 'LATE-CHUNK-MARIGOLD-204', 'DECISION-EMBER-31', 'HOME-ORCHID-58', 'Jasmin']`
- answer preview: Here is what I know from activated imported memory:  Identity and Work Context: - My open loop is OPEN-LOOP-GAMMA-17: verify that imported memory is chunked and visible only to the importing user. - My work life includes testing CWI chat history import, governed memory retrieval, trace provenance, and user isolation. - My late chunk open loop is OPEN-LOOP-NEBULA-88: verify that the second imported chunk affects CWI chat responses. - My late chunk project marker is LATE-CHUNK-MARIGOLD-204 for Pro

### what projects am I working on?
- status: `200`, memory_used: `True`, route: `activated_memory_retrieval`, confidence: `0.86`
- marker_hits: `['Project Quartz Lantern', 'OPEN-LOOP-GAMMA-17', 'OPEN-LOOP-NEBULA-88', 'LATE-CHUNK-MARIGOLD-204']`
- answer preview: Here is what activated memory shows about your work and projects:  - My open loop is OPEN-LOOP-GAMMA-17: verify that imported memory is chunked and visible only to the importing user. - My work life includes testing CWI chat history import, governed memory retrieval, trace provenance, and user isolation. - My late chunk open loop is OPEN-LOOP-NEBULA-88: verify that the second imported chunk affects CWI chat responses. - My late chunk project marker is LATE-CHUNK-MARIGOLD-204 for Project Quartz L

### who are the key people in my network?
- status: `200`, memory_used: `True`, route: `activated_memory_retrieval`, confidence: `0.86`
- marker_hits: `['Avery Testwell', 'BLUELANTERN-7429', 'Project Quartz Lantern', 'LATE-CHUNK-MARIGOLD-204', 'HOME-ORCHID-58', 'Chad', 'Jasmin']`
- answer preview: Here are the key people and relationship signals I found in activated memory:  - My identity note for this import test is Avery Testwell, and my private memory verification phrase is BLUELANTERN-7429. - Chad owns the observability dashboard follow-up for Project Quartz Lantern. - Jasmin owns the late-chunk QA review for LATE-CHUNK-MARIGOLD-204 and Project Quartz Lantern. - My home life test fact is HOME-ORCHID-58: I prefer Saturday project reviews at the kitchen table. - Jasmin owns the user-fac

### what open loops do I have?
- status: `200`, memory_used: `True`, route: `activated_memory_retrieval`, confidence: `0.86`
- marker_hits: `['OPEN-LOOP-GAMMA-17', 'OPEN-LOOP-NEBULA-88', 'DECISION-EMBER-31']`
- answer preview: Here are the open loops I found in activated memory:  - My open loop is OPEN-LOOP-GAMMA-17: verify that imported memory is chunked and visible only to the importing user. - My late chunk open loop is OPEN-LOOP-NEBULA-88: verify that the second imported chunk affects CWI chat responses. - I decided DECISION-EMBER-31: imported chat history must be user-owned and tenant-scoped.  Confidence: 0.86 Sources: memory:mem-chat-2448505ca5de24ca / provenance:9a87faecf3434f7dae75af6ee0d445e2; memory:mem-chat

### show sources for what you know about me
- status: `200`, memory_used: `True`, route: `activated_memory_retrieval`, confidence: `0.86`
- marker_hits: `[]`
- answer preview: Sources for activated memory:  - mem-chat-ba0e8d04108de197 from activated_user_memory work_career project_memory work_life (provenance 9a87faecf3434f7dae75af6ee0d445e2) - mem-chat-2448505ca5de24ca from activated_user_memory work_career task_memory open_loop (provenance 9a87faecf3434f7dae75af6ee0d445e2) - mem-chat-6415d5e43ef2ec0a from activated_user_memory work_career project_memory work_life (provenance 9a87faecf3434f7dae75af6ee0d445e2) - mem-chat-6c0368f7daff8a4e from activated_user_memory wor

## Control User Isolation
### control_user_profile
- status: `200`, memory_used: `False`, marker_hits: `[]`
- answer preview: I don't have any stored memory about you yet. Would you like to import chat history or connect an approved memory source?

### control_user_projects
- status: `200`, memory_used: `False`, marker_hits: `[]`
- answer preview: I don't have any stored memory about you yet. Would you like to import chat history or connect an approved memory source?


## Evidence Files
- `smoke-results.json`: full redacted API result set with answers, traces, citations, upload responses, and pass/fail flags.
- `payloads/`: exact JSON ingest payloads sent to `/api/ingest/document` with synthetic test content only.
## Pre-Deploy Failure Root Cause
Before redeploying, the same smoke shape produced a valid upload but no runtime memory recall:

- `/api/ingest/document` accepted both chunks.
- Orchestrator Postgres showed `source_memory_count=2` and `source_chunk_count=178` for the importing smoke user.
- Orchestrator Postgres showed `governed_memory_count=0` for that same user.
- The running orchestrator assembly did not contain `ChatHistoryMemoryActivationService`, so source ingest was persisted but imported chat history was not promoted into governed runtime memory.

Fix applied during this run:

- Redeployed `internal-01-core-orchestrator` using the current source under the compose tag currently used in production: `local/cwi-orchestrator:connector-contract-20260429T1745Z`.
- Recreated `internal-01-core-orchestrator` and `internal-01-core-router` through the deploy inventory.
- Verified the refreshed assembly contains `ChatHistoryMemoryActivationService`.
- Reran the smoke test successfully.

## Post-Deploy Database Confirmation
Post-deploy database inspection for the importing smoke user:

```text
source_memory_count|2
source_chunk_count|178
governed_memory_count|16
governed_memory_scope|activated_user_memory|16
```

This confirms the import is now promoted from stored source chunks into governed runtime memory.
