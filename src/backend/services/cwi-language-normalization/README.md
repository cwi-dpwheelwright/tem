# CWI Language Normalization Service

Standalone .NET 8 prompt-normalization service for orchestrator intake routing.

## Purpose

This service owns deterministic language normalization before intent classification:

- raw prompt preservation
- routing-safe normalized prompt generation
- applied-rule reporting
- verification metadata for ambiguous corrections such as `capitol texas`
- fail-safe result payloads when normalization cannot complete

The orchestrator can run in `local`, `external`, or `external_with_local_fallback` mode using:

- `LANGUAGE_NORMALIZATION_MODE`
- `LANGUAGE_NORMALIZATION_BASE_URL`
- `LANGUAGE_NORMALIZATION_TIMEOUT_MS`

In compose stacks that include the `cwi-language-normalization` service, set `LANGUAGE_NORMALIZATION_MODE=external` or `external_with_local_fallback`.

## API

`POST /api/normalization/normalize`

```json
{
  "prompt": "What is the capitol of Texas?"
}
```

Response:

```json
{
  "raw_prompt": "What is the capitol of Texas?",
  "normalized_prompt": "what is the capital of texas",
  "applied": true,
  "applied_rules": ["semantic_spelling", "punctuation_case_whitespace"],
  "requires_verification": false,
  "engine_version": "cwi-rule-normalizer-v1"
}
```

Health:

`GET /health`

OpenAPI is available at `/api/dev/swagger` and `/api/dev/openapi/v1.json` by default. Set `LANGUAGE_NORMALIZATION_DEV_OPENAPI_ENABLED=false` to disable it outside Development.
