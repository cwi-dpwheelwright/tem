# CWI Teams Bot

Teams approvals surface built on the Microsoft Bot Framework.

## Quick start

1. Copy `.env.template` to `.env` and fill in the values.
2. Install dependencies:

```
npm install
```

3. Run locally:

```
npm run dev
```

## Configuration

- `MICROSOFT_APP_ID` / `MICROSOFT_APP_PASSWORD`: Bot Framework credentials.
- `ALLOW_UNAUTHENTICATED`: allow unsigned local traffic when `MICROSOFT_APP_ID` is set.
- `BOT_TOKEN_ISSUER` / `BOT_TOKEN_AUDIENCE` / `BOT_TOKEN_JWKS_URL`: validate Bot Framework JWTs.
- `BOT_TENANT_ALLOWLIST`: comma-separated tenant IDs allowed to use the bot.
- `GATEWAY_BASE_URL`: Router/Gateway base URL for approval completion.
- `GATEWAY_STATIC_TOKEN` or OAuth settings: outbound auth for router/gateway.
- `APPROVER_ALLOWLIST`: comma-separated user IDs/AAD object IDs allowed to approve.
- `APPROVER_ROLE_MAP`: JSON mapping `{ "aadObjectId": ["approver"] }`.
- `REDIS_URL` or `POSTGRES_URL`: idempotency store backing.
- `APPROVAL_WEBHOOK_URL`: Teams incoming webhook for approval tickets.
- `APPROVAL_WEBHOOK_SECRET`: shared secret header for approval event posts.
- `APPROVAL_POLL_INTERVAL_SECONDS`: enable polling for approvals (0 disables).
- `APPROVAL_POLL_LIMIT`: max approvals per poll request.
- `APPROVAL_TENANT_IDS`: comma-separated tenant IDs to poll.
- `APPROVAL_DEDUPE_TTL_SECONDS`: dedupe window for approval notifications.
- `APPROVAL_ROUTE_OVERRIDES`: JSON map of ticket_type -> approval completion endpoint.
- `BREAK_GLASS_WEBHOOK_URL`: Teams incoming webhook for break-glass alerts.
- `BREAK_GLASS_WEBHOOK_SECRET`: shared secret header for break-glass events.
- `REPORT_WEBHOOK_URL`: Teams incoming webhook for report/document notifications.
- `REPORT_WEBHOOK_SECRET`: shared secret header for report/document events.
- `REPORT_DEDUPE_TTL_SECONDS`: dedupe window for report/document notifications.
- `REPORT_THROTTLE_WINDOW_SECONDS`: throttle window per tenant for report/document notifications.

## Endpoints

- `POST /api/messages` Bot Framework endpoint for Teams.
- `POST /api/approvals/card` returns the Adaptive Card payload for a ticket.
- `POST /api/approvals/events` receives approval notifications and forwards to Teams.
- `POST /api/break-glass/events` receives break-glass notification payloads.
- `POST /api/reports/events` receives report/document notification payloads.
- `GET /metrics` Prometheus metrics.
- `GET /health` health check.

## Teams app registration
1. Register the bot in Entra ID (Azure AD) and record the App ID + secret.
2. Enable the Bot Framework channel for Teams.
3. Set the messaging endpoint to `https://<host>/api/messages`.
4. Configure the token issuer/audience/JWKS URL for token validation.

## QA checklist
- Load test `POST /api/messages` with bot auth enabled (validate rate limits at the gateway).
- Verify `/metrics` exposure in Prometheus and confirm request counters increment.
- Validate log redaction for Authorization headers and secret fields.

## Commands
- `/breakglass-audit <tenant_id> [page] [page_size]` fetches break-glass audit entries.
