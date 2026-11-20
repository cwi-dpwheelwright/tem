import dotenv from "dotenv";

dotenv.config();

const isProduction = process.env.NODE_ENV === "production";
const PLACEHOLDER_HOSTS = new Set(["localhost", "127.0.0.1", "0.0.0.0"]);
const PLACEHOLDER_SUFFIXES = ["example.com", "example.net", "example.org"];

function requireUrl(
  name: string,
  { allowLocalhost = false }: { allowLocalhost?: boolean } = {},
): string {
  const raw = (process.env[name] ?? "").trim();
  if (!raw) {
    throw new Error(`${name} is required.`);
  }
  let parsed: URL;
  try {
    parsed = new URL(raw);
  } catch {
    throw new Error(`${name} must be a valid URL.`);
  }
  if (!parsed.protocol.startsWith("http")) {
    throw new Error(`${name} must start with http or https.`);
  }
  const host = parsed.hostname.toLowerCase();
  const isPlaceholder =
    PLACEHOLDER_HOSTS.has(host) ||
    PLACEHOLDER_SUFFIXES.some((suffix) => host.endsWith(suffix));
  if (!allowLocalhost && isPlaceholder) {
    throw new Error(`${name} must not point to localhost or example domains in production.`);
  }
  return raw.replace(/\/$/, "");
}

export const config = {
  port: Number(process.env.PORT ?? "3978"),
  appId: process.env.MICROSOFT_APP_ID ?? "",
  appPassword: process.env.MICROSOFT_APP_PASSWORD ?? "",
  allowUnauthenticated:
    (process.env.ALLOW_UNAUTHENTICATED ?? "false").toLowerCase() === "true",
  tokenIssuer: process.env.BOT_TOKEN_ISSUER ?? "",
  tokenAudience: process.env.BOT_TOKEN_AUDIENCE ?? "",
  tokenJwksUrl: process.env.BOT_TOKEN_JWKS_URL ?? "",
  tenantAllowlist: process.env.BOT_TENANT_ALLOWLIST ?? "",
  gatewayBaseUrl: requireUrl("GATEWAY_BASE_URL", { allowLocalhost: !isProduction }),
  gatewayStaticToken: process.env.GATEWAY_STATIC_TOKEN ?? "",
  gatewayOauthTokenUrl: process.env.GATEWAY_OAUTH_TOKEN_URL ?? "",
  gatewayOauthClientId: process.env.GATEWAY_OAUTH_CLIENT_ID ?? "",
  gatewayOauthClientSecret: process.env.GATEWAY_OAUTH_CLIENT_SECRET ?? "",
  gatewayOauthScope: process.env.GATEWAY_OAUTH_SCOPE ?? "",
  approverAllowlist: process.env.APPROVER_ALLOWLIST ?? "",
  approverRoleMap: process.env.APPROVER_ROLE_MAP ?? "",
  redisUrl: process.env.REDIS_URL ?? "",
  postgresUrl: process.env.POSTGRES_URL ?? "",
  idempotencyTtlSeconds: Number(process.env.IDEMPOTENCY_TTL_SECONDS ?? "86400"),
  approvalWebhookUrl: process.env.APPROVAL_WEBHOOK_URL ?? "",
  approvalWebhookSecret: process.env.APPROVAL_WEBHOOK_SECRET ?? "",
  approvalPollIntervalSeconds: Number(process.env.APPROVAL_POLL_INTERVAL_SECONDS ?? "0"),
  approvalPollLimit: Number(process.env.APPROVAL_POLL_LIMIT ?? "50"),
  approvalTenantIds: (process.env.APPROVAL_TENANT_IDS ?? "")
    .split(",")
    .map((value) => value.trim())
    .filter((value) => value.length > 0),
  approvalDedupeTtlSeconds: Number(process.env.APPROVAL_DEDUPE_TTL_SECONDS ?? "900"),
  approvalRouteOverrides: process.env.APPROVAL_ROUTE_OVERRIDES ?? "",
  breakGlassWebhookUrl: process.env.BREAK_GLASS_WEBHOOK_URL ?? "",
  breakGlassWebhookSecret: process.env.BREAK_GLASS_WEBHOOK_SECRET ?? "",
  breakGlassDedupeTtlSeconds: Number(process.env.BREAK_GLASS_DEDUPE_TTL_SECONDS ?? "600"),
  breakGlassThrottleWindowSeconds: Number(
    process.env.BREAK_GLASS_THROTTLE_WINDOW_SECONDS ?? "300"
  ),
  reportWebhookUrl: process.env.REPORT_WEBHOOK_URL ?? "",
  reportWebhookSecret: process.env.REPORT_WEBHOOK_SECRET ?? "",
  reportDedupeTtlSeconds: Number(process.env.REPORT_DEDUPE_TTL_SECONDS ?? "900"),
  reportThrottleWindowSeconds: Number(process.env.REPORT_THROTTLE_WINDOW_SECONDS ?? "300"),
  eventBusEnabled: (process.env.EVENT_BUS_ENABLED ?? "false").toLowerCase() === "true",
  eventBusUrl: process.env.EVENT_BUS_URL ?? "",
  eventBusServiceName: process.env.EVENT_BUS_SERVICE_NAME ?? "cwi-teams-bot",
  eventBusEnv: process.env.EVENT_BUS_ENV ?? "unknown",
  eventBusToken: process.env.EVENT_BUS_TOKEN ?? "",
};
