# Connector Specs (Planned)

## XML Connector
- Input: XML documents or feeds (file or URL).
- Discover: identify top-level record nodes via XPath configuration.
- Fetch: stream records, emit one document per record node.
- Normalize: map record fields to canonical record attributes; include source_id, updated_utc.
- Privacy: allow redaction rules for PII fields (names, emails, phone numbers) before ingestion.

## Email Connector
- Input: mailbox API (IMAP/Graph/Gmail).
- Discover: folders/labels configured with read-only scopes.
- Fetch: pull message bodies + attachments metadata (no attachment content by default).
- Normalize: emit document per email thread with subject/body + metadata.
- Privacy: strip/obfuscate recipient lists by default; redact signature blocks.

## Document Connector (Docs/PDF)
- Input: document store export or API.
- Discover: list document ids; fetch metadata + content.
- Normalize: extract text + metadata, store content hash.
- Privacy: ensure classification labels are mapped to metadata; redact sensitive sections if tagged.

## Shared Requirements
- Support source_priority configuration for reconciliation ordering.
- Emit provenance envelope + idempotency key per run.
- Ensure retention_days is enforced with delete callbacks.
