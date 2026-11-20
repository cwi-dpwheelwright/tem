# Chat History Upload Smoke Test - 20260502T193143Z

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
- Chunk 1: status `200`, ok `True`, doc `web-upload-chat-history-smoke-20260502t193143z-file-001-input-zip-conversations-json-part-00001`, trace `745f8061f78245b98846833403f08c53`
- Chunk 2: status `200`, ok `True`, doc `web-upload-chat-history-smoke-20260502t193143z-file-001-input-zip-conversations-json-part-00002`, trace `38f0e5a7ad884a1fa006fe671289a4c7`

## Importing User Verification
### what do you know about me?
- status: `524`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-524/","title":"Error 524: A timeout occurred","status":524,"detail":"The origin web server did not return a complete response within the 120-second Proxy Read Timeout window. The connection was established, but the origin took too long to respond.","instance":"9f5972f32cf0208f","error_code":524,"error_name":"origin_response_timeout","error_category":"origin","ray_id":"9f5972f32cf0208f

### what projects am I working on?
- status: `200`, memory_used: `False`, route: `general_model_knowledge`, confidence: `0.8`
- marker_hits: `[]`
- answer preview: Based on the information provided in your question and without any specific context or memory of past interactions, it seems like you're asking about projects you're currently working on. Since no previous project details have been shared, I can't provide a specific list. However, generally speaking, to determine what projects you are currently working on, you might want to check the following:  1. **Project Management Tools**: Platforms like Trello, Asana, Jira, or Microsoft Teams often keep tr

### who are the key people in my network?
- status: `200`, memory_used: `False`, route: `clarification_handler`, confidence: `0.7`
- marker_hits: `[]`
- answer preview: I do not currently have enough information to identify who you are referring to.  Can you clarify whether The Key People In My Network is: 1. A personal contact 2. A public figure 3. Someone related to your work  Once I have that context, I can answer precisely.

### what open loops do I have?
- status: `524`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-524/","title":"Error 524: A timeout occurred","status":524,"detail":"The origin web server did not return a complete response within the 120-second Proxy Read Timeout window. The connection was established, but the origin took too long to respond.","instance":"9f59785d6e9c307d","error_code":524,"error_name":"origin_response_timeout","error_category":"origin","ray_id":"9f59785d6e9c307d

### show sources for what you know about me
- status: `200`, memory_used: `False`, route: `pack_model_answer`, confidence: `0.2`
- marker_hits: `[]`
- answer preview: I do not have enough governed evidence to answer that safely. Please name the source domain, runbook, repo area, log, or CWI component you want me to use, and I can retry with governed retrieval.

## Control User Isolation
### control_user_profile
- status: `200`, memory_used: `False`, marker_hits: `[]`
- answer preview: I do not have enough governed evidence to answer that safely. Please name the source domain, runbook, repo area, log, or CWI component you want me to use, and I can retry with governed retrieval.

### control_user_projects
- status: `200`, memory_used: `False`, marker_hits: `[]`
- answer preview: Based on the information provided in your question and the context given, it seems you're asking about projects you are currently working on. Since no specific details or records are mentioned, I can't provide a precise answer. However, generally speaking, the projects you are working on could include any tasks or initiatives that you have been assigned or are collaborating on.  To determine what projects you are actively involved in, consider checking your:  1. **Task List**: Look at any task m

## Root Cause Notes
- Upload accepted but post-import answers did not contain expected synthetic memory markers. Likely break is runtime memory activation/retrieval after source ingest, not the upload endpoint.

## Evidence Files
- `smoke-results.json`: full redacted API result set with answers, traces, citations, upload responses, and pass/fail flags.
- `payloads/`: exact JSON ingest payloads sent to `/api/ingest/document` with synthetic test content only.