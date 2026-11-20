# DEPLOY: cwi-teams-bot

## Purpose
Teams approvals surface built on the Microsoft Bot Framework.

## Files
- docker-compose.yml
- .env.template (copy to .env)

## Setup
1. Copy `.env.template` to `.env` and update values.
2. Start service:
   ```
   docker compose up -d
   ```

## App registration (Entra ID)
1. Register a Bot/Entra ID app and capture the App ID + secret.
2. Enable the Teams channel.
3. Set the messaging endpoint to `https://<host>/api/messages`.
4. Configure token validation env vars (issuer/audience/JWKS URL).
5. Optionally restrict allowed tenants with `BOT_TENANT_ALLOWLIST`.

## Health/metrics
- Health: `GET /health`
- Metrics: `GET /metrics`

## Break-glass notifications
Configure `BREAK_GLASS_WEBHOOK_URL` (Teams incoming webhook) and send break-glass events to:
`POST /api/break-glass/events` with JSON payload:
```
{
  "event_type": "issued|revoked",
  "token_id": "bg-...",
  "tenant_id": "...",
  "user_id": "...",
  "role": "admin",
  "scope": "admin",
  "occurred_utc": "2026-01-30T00:00:00Z"
}
```
If `BREAK_GLASS_WEBHOOK_SECRET` is set, include `X-Webhook-Secret` header.
