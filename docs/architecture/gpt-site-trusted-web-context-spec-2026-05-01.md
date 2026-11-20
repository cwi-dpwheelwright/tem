# GPT Site Trusted Web Context Specification

Date: 2026-05-01
Status: Draft implementation specification
Scope: CWI GPT web UI, gateway/router ask path, orchestrator ask workflow

## Summary

Add a `Use web` toggle to the GPT site that includes supplemental context from preconfigured trusted websites. The toggle must not route around or replace the native CWI answer path. The GPT site continues to use the normal CWI flow for routing, packs, memory, graph, connectors, response mode selection, communication core, refinement, and final output.

When enabled, trusted web results are added as an additional evidence layer. The model and response orchestration may use that evidence when it is relevant, but web context cannot override CWI identity, runtime/system packs, user memory, connector data, graph evidence, or required response templates.

No paid search provider is required for the first implementation. The first version uses configured trusted-site sources only: seed URLs, sitemaps, RSS/Atom feeds, and explicitly allowed URL patterns. Paid search providers remain a later extension behind the same internal provider interface.

## Goals

- Add a visible `Use web` toggle above the GPT prompt input.
- Preserve the native CWI ask path for every request.
- Include trusted web context only when the toggle is enabled.
- Use configured trusted sites instead of broad web search for the first release.
- Keep trusted web context supplemental, labeled, cited, and auditable.
- Allow administrators to define source profiles and allowed domains without code changes.
- Degrade cleanly when trusted web context is unavailable.
- Keep paid providers out of the initial runtime path while leaving a future extension point.

## Non-Goals

- Do not add broad open-web search in the first release.
- Do not call paid providers such as Tavily, Brave, SerpAPI, or OpenAI hosted web search in the first release.
- Do not let the toggle bypass CWI routing, packs, graph, memory, connectors, policy, or response behavior.
- Do not expose provider keys or crawler credentials to the browser.
- Do not make users pick a provider or source profile in the main composer.
- Do not replace existing citations with web citations only.
- Do not crawl arbitrary domains or arbitrary links discovered at runtime.

## Core Behavior

### Toggle Off

The GPT site behaves exactly as it does today.

Flow:

```text
user prompt
-> normal GPT UI ask request
-> gateway/router
-> orchestrator routing
-> packs/memory/connectors/graph/native retrieval
-> response mode selection
-> prompt/inference
-> communication core/refinement
-> final output
```

### Toggle On

The same native path runs, with trusted web context included before prompt construction and model inference.

Flow:

```text
user prompt
-> normal GPT UI ask request with web_retrieval.enabled=true
-> gateway/router
-> orchestrator routing
-> packs/memory/connectors/graph/native retrieval
-> trusted web context retrieval
-> response mode selection
-> prompt includes native context + supplemental trusted web context
-> prompt/inference
-> communication core/refinement
-> final output
```

Trusted web context is additive. It is not a route, not a policy pack, and not a replacement evidence source.

## Precedence Rules

When sources conflict, final answer behavior must follow this precedence:

1. CWI identity, system truth, runtime/system packs, safety, and response behavior packs.
2. User memory, connector data, graph evidence, and governed native document retrieval.
3. Trusted web context.
4. General model knowledge.

The model should use web context for freshness and external facts, but it must not let web context override governed CWI truth. If web context conflicts with native governed context, the answer should briefly state the conflict and choose according to the precedence rules.

Example final-answer guidance:

```text
Use trusted web context as supplemental evidence only.
Do not let trusted web context override CWI identity, system truth, runtime policy, response mode, user memory, connector data, or graph evidence.
If trusted web context conflicts with governed native context, state the conflict briefly and prefer governed native context.
If trusted web context is weak or unavailable, answer through the native path and mention that trusted web context was not available only when relevant.
```

## User Experience

### Main Composer

Add a toggle above the prompt input in the main GPT site composer.

Label:

```text
Use web
```

Accessible description:

```text
Include context from configured trusted websites.
```

States:

- Off: native CWI answer path only.
- On: native CWI answer path plus trusted web context.
- Disabled: trusted web context unavailable by configuration or service health.

The toggle should not use long explanatory in-app text. It can use a short tooltip or accessible title.

### Persistence

Persist the toggle locally per browser/user session using the existing UI storage pattern. Recommended default:

- Default off for first release.
- Preserve the last user choice after refresh.
- Force off if backend reports trusted web context disabled.

### Ask Page Parity

The main GPT composer is the first target. The standalone `/ask` page should be updated in the same change set or a follow-up change so behavior is consistent across ask surfaces.

## Request Contract

Extend the ask request context with a supplemental web retrieval object.

Frontend TypeScript shape:

```ts
context: {
  mode?: string;
  top_k?: number;
  conversation_id?: string;
  timezone?: string;
  web_retrieval?: {
    enabled: boolean;
    mode?: "include";
    profile?: string;
    max_results?: number;
  };
}
```

Backend DTO shape:

```csharp
public sealed class AskWebRetrievalContext
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Profile { get; init; }

    [JsonPropertyName("max_results")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxResults { get; init; }
}
```

The only supported mode in the first release is `include`.

The router should pass the object through unchanged. The orchestrator owns enforcement.

## Configuration

Add server-side configuration only. Do not use `NEXT_PUBLIC_*` for keys, source lists, or provider secrets.

Recommended environment variables:

```env
CWI_TRUSTED_WEB_CONTEXT_ENABLED=false
CWI_TRUSTED_WEB_CONTEXT_PROFILE_PATH=/etc/cwi/trusted-web-context.profiles.json
CWI_TRUSTED_WEB_CONTEXT_DEFAULT_PROFILE=general
CWI_TRUSTED_WEB_CONTEXT_MAX_RESULTS=5
CWI_TRUSTED_WEB_CONTEXT_FETCH_TIMEOUT_MS=5000
CWI_TRUSTED_WEB_CONTEXT_CACHE_TTL_SECONDS=21600
CWI_TRUSTED_WEB_CONTEXT_MAX_SNIPPET_CHARS=1200
CWI_TRUSTED_WEB_CONTEXT_RESPECT_ROBOTS=true
CWI_TRUSTED_WEB_CONTEXT_USER_AGENT=CWI-TrustedWebContext/1.0
CWI_TRUSTED_WEB_CONTEXT_PAID_PROVIDERS_ENABLED=false
```

Recommended config file:

```json
{
  "version": 1,
  "default_profile": "general",
  "profiles": {
    "general": {
      "description": "Default trusted web context for broad questions.",
      "mode": "trusted_sites_only",
      "max_results": 5,
      "sources": [
        {
          "id": "openai",
          "display_name": "OpenAI",
          "allowed_domains": ["openai.com", "platform.openai.com", "developers.openai.com", "help.openai.com"],
          "seed_urls": ["https://platform.openai.com/docs"],
          "sitemap_urls": [],
          "rss_urls": [],
          "include_path_prefixes": ["/docs", "/api", "/help"],
          "exclude_path_prefixes": ["/blog/rss"],
          "refresh_interval_minutes": 1440,
          "max_pages": 500,
          "priority": 80
        }
      ]
    },
    "news": {
      "description": "Trusted public news wires and official releases.",
      "mode": "trusted_sites_only",
      "max_results": 5,
      "sources": []
    },
    "finance": {
      "description": "Regulatory and official financial sources.",
      "mode": "trusted_sites_only",
      "max_results": 5,
      "sources": [
        {
          "id": "sec",
          "display_name": "SEC",
          "allowed_domains": ["sec.gov", "www.sec.gov"],
          "seed_urls": ["https://www.sec.gov/"],
          "sitemap_urls": [],
          "rss_urls": [],
          "include_path_prefixes": ["/news", "/edgar", "/files"],
          "exclude_path_prefixes": [],
          "refresh_interval_minutes": 360,
          "max_pages": 1000,
          "priority": 100
        }
      ]
    }
  }
}
```

The exact default source list should be reviewed separately. The implementation should ship with sample config and no overly broad default allowlist.

## Trusted Site Discovery Without Paid Providers

Because the first release avoids paid search providers, trusted web context cannot behave like arbitrary Google-style search. It can only retrieve from configured trusted sources.

Supported discovery mechanisms:

- Explicit seed URLs.
- Sitemaps listed in config.
- RSS/Atom feeds listed in config.
- Links discovered from seed pages only when:
  - the target host is allowlisted,
  - the URL path matches configured include rules,
  - the path does not match exclude rules,
  - crawl depth and max page limits are respected.

Unsupported in first release:

- Arbitrary domain crawling.
- Arbitrary user-provided web search.
- Search engine scraping.
- Paid API-based search.
- JavaScript-heavy browser rendering.

This means the toggle is best described internally as trusted web context, not open-web search.

## Trusted Web Index

The orchestrator should query a local trusted-web corpus instead of fetching many pages during each ask.

Recommended approach:

1. A background refresher reads configured profiles.
2. It fetches seed URLs, sitemaps, and feeds.
3. It normalizes HTML/feed content into text.
4. It chunks content.
5. It stores chunk metadata and text in a trusted web corpus.
6. It optionally stores embeddings through the existing vector retrieval infrastructure.
7. Ask-time retrieval searches that corpus and returns the most relevant chunks.

MVP storage options:

- Preferred: reuse the existing document/vector retrieval pipeline with `source_type=trusted_web`.
- Acceptable short-term: in-memory cache plus lexical scoring for small configured source lists.

The preferred implementation is more durable and aligns with current retrieval/trace behavior.

## Backend Components

### Options

Add an options model:

```csharp
public sealed class TrustedWebContextOptions
{
    public bool Enabled { get; init; }
    public string ProfilePath { get; init; } = string.Empty;
    public string DefaultProfile { get; init; } = "general";
    public int MaxResults { get; init; } = 5;
    public int FetchTimeoutMs { get; init; } = 5000;
    public int CacheTtlSeconds { get; init; } = 21600;
    public int MaxSnippetChars { get; init; } = 1200;
    public bool RespectRobots { get; init; } = true;
    public string UserAgent { get; init; } = "CWI-TrustedWebContext/1.0";
    public bool PaidProvidersEnabled { get; init; } = false;
}
```

### Source Profile Loader

Responsibilities:

- Load JSON profile config at startup.
- Validate source IDs, domains, URLs, and path rules.
- Reject wildcard domains unless explicitly supported later.
- Normalize hosts to lower case.
- Expose active profiles to retrieval and health checks.

### Corpus Refresher

Responsibilities:

- Refresh configured sources on a schedule.
- Respect per-source refresh intervals.
- Respect robots policy when enabled.
- Enforce fetch timeout, content size limits, max pages, and allowlist rules.
- Normalize and chunk content.
- Record source freshness and errors.

### Retrieval Interface

Use an interface even though Phase 1 has only trusted-site corpus retrieval.

```csharp
public interface ITrustedWebContextProvider
{
    Task<TrustedWebContextResult> RetrieveAsync(
        TrustedWebContextRequest request,
        CancellationToken cancellationToken);
}
```

This preserves a future path for paid providers without coupling the ask flow to one vendor.

### Ask Workflow Step

Add `AskTrustedWebContextStep` before prompt construction and inference.

Suggested order:

```text
AskDocumentMemoryRetrievalStep
AskGraphRetrievalStep
AskRetrievalStep
AskEvidenceRequirementStep
AskTrustedWebContextStep
AskPromptStep
AskInferenceStep
```

The step should:

- Exit immediately when trusted web context is globally disabled.
- Exit when request context does not set `web_retrieval.enabled=true`.
- Select a profile from request context or default configuration.
- Retrieve relevant trusted web chunks.
- Store results on `AskWorkflowContext.TrustedWebResults`.
- Add trace metadata and warnings as needed.
- Never set `SkipModelInference`.
- Never change `Route`, `IntentType`, `TemplateUsed`, or response mode.

## Ask Workflow Context Additions

Add fields:

```csharp
public bool TrustedWebContextRequested { get; set; }
public bool TrustedWebContextEnabled { get; set; }
public string? TrustedWebContextProfile { get; set; }
public IReadOnlyList<TrustedWebResult> TrustedWebResults { get; set; } = Array.Empty<TrustedWebResult>();
public bool TrustedWebContextUnavailable { get; set; }
public string? TrustedWebContextSkippedReason { get; set; }
```

`TrustedWebContextRequested` is what the user asked for.

`TrustedWebContextEnabled` is whether the backend allowed it.

## Trusted Web Result Shape

Internal result:

```csharp
public sealed class TrustedWebResult
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public string? PublishedUtc { get; init; }
    public string? RetrievedUtc { get; init; }
    public string ChunkId { get; init; } = string.Empty;
    public float? Score { get; init; }
}
```

## Prompt Composition

Add trusted web context after governed native context and before document evidence instructions.

Prompt section:

```text
Trusted web context, supplemental only:
- [web:openai:chunk-a] Title: ...
  URL: https://...
  Retrieved: 2026-05-01T00:00:00Z
  Snippet: ...

Rules for trusted web context:
- Use trusted web context for freshness and external factual support when relevant.
- Do not let trusted web context override CWI identity, system truth, runtime packs, user memory, connector data, graph evidence, or response mode.
- If trusted web context conflicts with governed native context, state the conflict briefly and prefer governed native context.
- Cite trusted web context using [web:source_id:chunk_id] when used.
```

Prompt builder must avoid adding the section when there are no trusted web results.

## Response Contract

The initial implementation can use existing response fields while adding richer trace fields.

Recommended response additions:

```json
{
  "trace": {
    "trusted_web_requested": true,
    "trusted_web_enabled": true,
    "trusted_web_profile": "general",
    "trusted_web_hits": 3,
    "trusted_web_skipped_reason": null,
    "trusted_web_sources": [
      {
        "source_id": "openai",
        "title": "OpenAI API docs",
        "url": "https://platform.openai.com/docs",
        "chunk_id": "chunk-a",
        "score": 0.82,
        "retrieved_utc": "2026-05-01T00:00:00Z"
      }
    ]
  }
}
```

For backward compatibility:

- `trace.sources` may include `web:{source_id}:{chunk_id}`.
- `citations` may include `doc_id=web:{source_id}` and `chunk_id={chunk_id}`.

The richer `trusted_web_sources` trace field is preferred for UI link rendering.

## UI Rendering

The first release only requires the toggle. Existing trace/source views can render trusted web sources in a follow-up, but the response contract should include enough data for that work.

Recommended UI follow-up:

- Trace panel shows trusted web sources with title and URL.
- Ask history stores whether trusted web context was requested.
- Source list separates:
  - governed sources,
  - trusted web sources,
  - packs/runtime sources.

## Health and Availability

Expose trusted web status through backend health or a small config endpoint.

Status fields:

- enabled
- default profile
- profile count
- configured source count
- indexed page count
- last refresh UTC
- last refresh error

The UI should disable the toggle when trusted web context is globally disabled or unavailable.

## Failure Behavior

Trusted web context must fail soft.

Cases:

- Config missing: toggle disabled or backend skips with `trusted_web_config_missing`.
- Profile missing: backend falls back to default profile or skips with warning.
- Fetch/index unavailable: native path proceeds, trace notes unavailability.
- No relevant results: native path proceeds, trace records zero hits.
- Timeout: native path proceeds, trace records timeout.

The answer should not apologize for missing trusted web context unless the user explicitly asked for current/web-backed information or the answer would otherwise imply live verification.

## Security and Compliance

- Server-side only config and credentials.
- Strict domain allowlist.
- No arbitrary URL fetching from user prompts in first release.
- Respect robots.txt when enabled.
- Enforce content size and fetch timeout limits.
- Strip scripts, styles, tracking pixels, forms, and non-text content.
- Do not store cookies from fetched sites.
- Do not send user identity or tenant secrets to trusted sites.
- Use a clear CWI user agent.
- Log source IDs and URLs, not full prompt text, unless existing safe logging policy permits it.
- Maintain per-tenant isolation if the corpus becomes tenant-specific.

## Observability

Add metrics:

- `trusted_web_context_requested_total`
- `trusted_web_context_enabled_total`
- `trusted_web_context_retrieval_duration_ms`
- `trusted_web_context_hits`
- `trusted_web_context_skipped_total{reason}`
- `trusted_web_context_refresh_success_total`
- `trusted_web_context_refresh_failure_total`
- `trusted_web_context_indexed_pages`

Add trace metadata:

- requested/enabled flags
- profile
- hit count
- skipped reason
- retrieval duration
- selected source IDs

## Testing Plan

### Frontend Unit Tests

- Toggle renders above prompt input.
- Toggle state persists.
- Ask request includes `context.web_retrieval.enabled=true` when on.
- Ask request omits or disables web context when off.
- Disabled state renders when backend config says unavailable.

### Backend Unit Tests

- `AskContext` deserializes `web_retrieval`.
- Ask service maps request fields into `AskWorkflowContext`.
- `AskTrustedWebContextStep` calls provider only when requested and enabled.
- Step does not alter route, intent, template, or response mode.
- Provider failure records warning and native path continues.
- Prompt builder includes trusted web section only when results exist.
- Prompt builder includes precedence instructions.

### Integration Tests

- End-to-end ask with toggle off matches native behavior.
- End-to-end ask with toggle on includes trusted web trace metadata.
- Configured trusted site result appears in prompt and trace.
- Missing profile falls back or skips according to spec.
- No result path still returns a normal answer.

### Security Tests

- User prompt containing a URL does not trigger arbitrary fetch.
- Non-allowlisted redirect target is rejected.
- Overlarge content is truncated or rejected.
- Disallowed path prefix is rejected.
- Provider secrets do not appear in client bundle or logs.

## Deployment Plan

1. Add backend config with trusted web context disabled by default.
2. Deploy backend support and no-op disabled behavior.
3. Add sample profile config to deployment bundles.
4. Enable on staging/internal environment.
5. Validate corpus refresh and ask-time retrieval.
6. Add UI toggle and health-driven availability state.
7. Enable on production after smoke tests.

## Rollback Plan

- Set `CWI_TRUSTED_WEB_CONTEXT_ENABLED=false`.
- UI should disable the toggle after config refresh or next load.
- Orchestrator should ignore `web_retrieval.enabled=true` when globally disabled.
- Native ask path remains unchanged.

## Future Paid Provider Extension

Paid providers can be added later behind the same internal provider interface.

Future provider modes:

- `trusted_sites_only`: first-release mode.
- `trusted_first`: query trusted corpus first; optionally fall back to provider.
- `broad_web`: open web provider, still policy controlled.

Future provider config should remain server-side:

```env
CWI_TRUSTED_WEB_CONTEXT_PAID_PROVIDERS_ENABLED=true
CWI_WEB_SEARCH_PROVIDER=tavily
CWI_WEB_SEARCH_API_KEY=...
```

The UI toggle should not change when a paid provider is introduced. The toggle remains `Use web`, meaning include supplemental web context.

## Acceptance Criteria

- The GPT site always uses the native CWI ask path.
- Toggle off produces current behavior.
- Toggle on includes trusted web context when configured results are available.
- Trusted web context never replaces native retrieval, packs, routing, response mode, or refinement.
- No paid provider is required for the first release.
- Only configured trusted domains and paths are fetched.
- Web context is visible in trace metadata.
- Provider/crawler failures do not fail the ask request.
- Tests cover toggle request shape, backend parsing, workflow inclusion, failure fallback, and prompt composition.

## Open Questions

- Which trusted source profiles should ship first: general, CWI docs, finance, weather, news, technical docs, or vendor docs?
- Should profiles be global, tenant-scoped, or both?
- Should the first implementation reuse vector retrieval or start with a smaller lexical cache?
- Should the UI expose a subtle "trusted web used" indicator in the answer footer?
- Should admins manage trusted source profiles through a UI, config file, or pack-like artifact?
- What retention period should apply to cached trusted web content?
