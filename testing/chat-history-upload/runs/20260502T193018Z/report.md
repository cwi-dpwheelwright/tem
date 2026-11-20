# Chat History Upload Smoke Test - 20260502T193018Z

Result: FAIL

## Scope
- Endpoint under test: `https://cwigpt.com/api/ingest/document`
- Upload path matches the UI button behavior: ZIP entry expansion -> structured transcript chunks -> JSON `POST /api/ingest/document` requests.
- Source ZIP SHA-256: `a7311f7a8cabd7f4603c9ed190368177eee4c4e4d5520bd56497857dbd15bdb2`
- Message count: 2416
- Generated chunks: 2

## Pass / Fail
- upload_all_chunks_accepted: `False`
- importing_user_answers_changed_with_memory_markers: `False`
- importing_user_trace_memory_used: `False`
- control_user_did_not_receive_imported_markers: `True`
- overall_pass: `False`
- control_user_marker_hits: `[]`

## Upload Responses
- Chunk 1: status `403`, ok `False`, doc `None`, trace `None`

## Importing User Verification
### what do you know about me?
- status: `403`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-1xxx-errors/error-1010/","title":"Error 1010: Access denied","status":403,"detail":"The site owner has blocked access based on your browser's signature.","instance":"9f597109dd2a6ad0","error_code":1010,"error_name":"browser_signature_banned","error_category":"access_denied","ray_id":"9f597109dd2a6ad0","timestamp":"2026-05-02T19:30:37Z","zone":"cwigpt.com","cloudflare_error":true,"retryable":false,"own

### what projects am I working on?
- status: `403`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-1xxx-errors/error-1010/","title":"Error 1010: Access denied","status":403,"detail":"The site owner has blocked access based on your browser's signature.","instance":"9f59710a0f22391e","error_code":1010,"error_name":"browser_signature_banned","error_category":"access_denied","ray_id":"9f59710a0f22391e","timestamp":"2026-05-02T19:30:37Z","zone":"cwigpt.com","cloudflare_error":true,"retryable":false,"own

### who are the key people in my network?
- status: `403`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-1xxx-errors/error-1010/","title":"Error 1010: Access denied","status":403,"detail":"The site owner has blocked access based on your browser's signature.","instance":"9f59710a2f7a9e12","error_code":1010,"error_name":"browser_signature_banned","error_category":"access_denied","ray_id":"9f59710a2f7a9e12","timestamp":"2026-05-02T19:30:37Z","zone":"cwigpt.com","cloudflare_error":true,"retryable":false,"own

### what open loops do I have?
- status: `403`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-1xxx-errors/error-1010/","title":"Error 1010: Access denied","status":403,"detail":"The site owner has blocked access based on your browser's signature.","instance":"9f59710a481628d8","error_code":1010,"error_name":"browser_signature_banned","error_category":"access_denied","ray_id":"9f59710a481628d8","timestamp":"2026-05-02T19:30:37Z","zone":"cwigpt.com","cloudflare_error":true,"retryable":false,"own

### show sources for what you know about me
- status: `403`, memory_used: `None`, route: `None`, confidence: `None`
- marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-1xxx-errors/error-1010/","title":"Error 1010: Access denied","status":403,"detail":"The site owner has blocked access based on your browser's signature.","instance":"9f59710abd2aa6b6","error_code":1010,"error_name":"browser_signature_banned","error_category":"access_denied","ray_id":"9f59710abd2aa6b6","timestamp":"2026-05-02T19:30:37Z","zone":"cwigpt.com","cloudflare_error":true,"retryable":false,"own

## Control User Isolation
### control_user_profile
- status: `403`, memory_used: `None`, marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-1xxx-errors/error-1010/","title":"Error 1010: Access denied","status":403,"detail":"The site owner has blocked access based on your browser's signature.","instance":"9f59710b0e3870e1","error_code":1010,"error_name":"browser_signature_banned","error_category":"access_denied","ray_id":"9f59710b0e3870e1","timestamp":"2026-05-02T19:30:37Z","zone":"cwigpt.com","cloudflare_error":true,"retryable":false,"own

### control_user_projects
- status: `403`, memory_used: `None`, marker_hits: `[]`
- answer preview: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-1xxx-errors/error-1010/","title":"Error 1010: Access denied","status":403,"detail":"The site owner has blocked access based on your browser's signature.","instance":"9f59710b4fd60722","error_code":1010,"error_name":"browser_signature_banned","error_category":"access_denied","ray_id":"9f59710b4fd60722","timestamp":"2026-05-02T19:30:37Z","zone":"cwigpt.com","cloudflare_error":true,"retryable":false,"own

## Root Cause Notes
- One or more `/api/ingest/document` requests failed or not all generated chunks were accepted. Inspect `smoke-results.json` upload responses.

## Evidence Files
- `smoke-results.json`: full redacted API result set with answers, traces, citations, upload responses, and pass/fail flags.
- `payloads/`: exact JSON ingest payloads sent to `/api/ingest/document` with synthetic test content only.