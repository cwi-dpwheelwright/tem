# Chat History Upload Smoke Test - 20260502T193107Z

Result: FAIL

## Scope
- Endpoint under test: `https://cwigpt.com/api/ingest/document`
- Upload path matches the UI button behavior: ZIP entry expansion -> structured transcript chunks -> JSON `POST /api/ingest/document` requests.
- Source ZIP SHA-256: `a7311f7a8cabd7f4603c9ed190368177eee4c4e4d5520bd56497857dbd15bdb2`
- Message count: 2416
- Generated chunks: 2

## Pass / Fail
- upload_all_chunks_accepted: `True`
- importing_user_answers_changed_with_memory_markers: `False`
- importing_user_trace_memory_used: `False`
- control_user_did_not_receive_imported_markers: `True`
- overall_pass: `False`
- control_user_marker_hits: `[]`

## Upload Responses
- Chunk 1: status `200`, ok `True`, doc `web-upload-chat-history-smoke-20260502t193107z-file-001-input-zip-conversations-json-part-00001`, trace `f592d5f754584d5b9ab89b11b77cff6a`
- Chunk 2: status `200`, ok `True`, doc `web-upload-chat-history-smoke-20260502t193107z-file-001-input-zip-conversations-json-part-00002`, trace `16a0fae184a84216b104c61be4300261`

## Importing User Verification
### what do you know about me?
- status: `400`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"code":"invalid_request","message":"Pack anchor mismatch.","error":"invalid_request"}

### what projects am I working on?
- status: `400`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"code":"invalid_request","message":"Pack anchor mismatch.","error":"invalid_request"}

### who are the key people in my network?
- status: `200`, memory_used: `False`, route: `clarification_handler`, confidence: `0.7`
- marker_hits: `[]`
- answer preview: I do not currently have enough information to identify who you are referring to.  Can you clarify whether The Key People In My Network is: 1. A personal contact 2. A public figure 3. Someone related to your work  Once I have that context, I can answer precisely.

### what open loops do I have?
- status: `400`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"code":"invalid_request","message":"Pack anchor mismatch.","error":"invalid_request"}

### show sources for what you know about me
- status: `400`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"code":"invalid_request","message":"Pack anchor mismatch.","error":"invalid_request"}

## Control User Isolation
### control_user_profile
- status: `400`, memory_used: `None`, marker_hits: `[]`
- answer preview: {"code":"invalid_request","message":"Pack anchor mismatch.","error":"invalid_request"}

### control_user_projects
- status: `400`, memory_used: `None`, marker_hits: `[]`
- answer preview: {"code":"invalid_request","message":"Pack anchor mismatch.","error":"invalid_request"}

## Root Cause Notes
- Upload accepted but post-import answers did not contain expected synthetic memory markers. Likely break is runtime memory activation/retrieval after source ingest, not the upload endpoint.

## Evidence Files
- `smoke-results.json`: full redacted API result set with answers, traces, citations, upload responses, and pass/fail flags.
- `payloads/`: exact JSON ingest payloads sent to `/api/ingest/document` with synthetic test content only.