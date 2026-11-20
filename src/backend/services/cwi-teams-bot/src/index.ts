import restify from "restify";
import crypto from "crypto";
import { BotFrameworkAdapter } from "botbuilder";
import { config } from "./config.js";
import { ApprovalsBot } from "./bot/ApprovalsBot.js";
import { buildApprovalCard } from "./services/approvalService.js";
import { notifyBreakGlass } from "./services/breakGlassNotifications.js";
import { notifyApproval } from "./services/approvalNotifications.js";
import { notifyReport } from "./services/reportNotifications.js";
import { startApprovalPolling } from "./services/approvalPolling.js";
import { validateBotToken } from "./auth/botTokenValidator.js";
import { recordRequest, renderMetrics } from "./observability/metrics.js";
import { logInfo, logError, redactHeaders } from "./observability/logger.js";
import { runHealthChecks } from "./health.js";
import { publishEvent } from "./eventBus.js";

const server = restify.createServer();
server.use(restify.plugins.bodyParser());

server.pre((req, res, next) => {
  const requestId =
    (req.header("x-request-id") as string) ||
    (req.header("x-correlation-id") as string) ||
    crypto.randomUUID();
  req.headers["x-request-id"] = requestId;
  res.setHeader("x-request-id", requestId);
  logInfo("request", {
    path: req.url,
    method: req.method,
    requestId,
    headers: redactHeaders(req.headers as Record<string, unknown>),
  });
  return next();
});

server.listen(config.port, () => {
  // eslint-disable-next-line no-console
  console.log(`cwi-teams-bot listening on ${config.port}`);
  void publishEvent({
    type: "service.start",
    polarity: "positive",
    severity: "info",
    message: "Teams bot started.",
  });
  startApprovalPolling();
});

const adapter = new BotFrameworkAdapter({
  appId: config.appId,
  appPassword: config.appPassword,
});

const bot = new ApprovalsBot();

adapter.onTurnError = async (context, error) => {
  await bot.handleError(context, error as Error);
};

const publishShutdown = (reason: string, severity: "warn" | "error") =>
  publishEvent({
    type: "service.stop",
    polarity: "negative",
    severity,
    message: `Teams bot stopping: ${reason}`,
  });

process.on("SIGTERM", () => {
  void publishShutdown("SIGTERM", "warn").finally(() => process.exit(0));
});

process.on("SIGINT", () => {
  void publishShutdown("SIGINT", "warn").finally(() => process.exit(0));
});

process.on("uncaughtException", (err) => {
  void publishEvent({
    type: "service.error",
    polarity: "negative",
    severity: "error",
    message: err instanceof Error ? err.message : "uncaught exception",
    data: err instanceof Error ? { stack: err.stack } : {},
  }).finally(() => process.exit(1));
});

process.on("unhandledRejection", (reason) => {
  void publishEvent({
    type: "service.error",
    polarity: "negative",
    severity: "error",
    message: "unhandled rejection",
    data: { reason: String(reason) },
  });
});

server.get("/health", async (_req, res) => {
  const result = await runHealthChecks();
  res.send(result.ok ? 200 : 503, result);
});

server.get("/metrics", async (_req, res) => {
  res.setHeader("content-type", "text/plain; version=0.0.4");
  res.send(200, renderMetrics());
});

server.post("/api/messages", async (req, res) => {
  const hasAuthHeader =
    typeof req.header("authorization") === "string" &&
    req.header("authorization")!.startsWith("Bearer ");
  if (config.appId && !config.allowUnauthenticated && !hasAuthHeader) {
    res.send(401, { error: "Missing Authorization header." });
    return;
  }

  if (config.appId && !config.allowUnauthenticated) {
    void validateBotToken(req.header("authorization") as string | undefined)
      .then((result) => {
        if (!result.ok) {
          res.send(401, { error: result.reason });
          recordRequest(req.url ?? "unknown", 401);
          return;
        }
        adapter.processActivity(req, res, async (context) => {
          await bot.run(context);
          recordRequest(req.url ?? "unknown", 200);
        });
      })
      .catch((err) => {
        logError("token validation failed", { error: err instanceof Error ? err.message : "unknown" });
        res.send(401, { error: "Token validation failed." });
        recordRequest(req.url ?? "unknown", 401);
      });
    return;
  }

  adapter.processActivity(req, res, async (context) => {
    await bot.run(context);
    recordRequest(req.url ?? "unknown", 200);
  });
});

server.post("/api/approvals/card", async (req, res) => {
  const payload = req.body as {
    ticket_id?: string;
    risk?: string;
    reason?: string;
    ticket_type?: string;
    trace_id?: string;
    trace_url?: string;
    details?: Array<{ title: string; value: string }>;
  };

  if (!payload?.ticket_id || !payload.risk || !payload.reason) {
    res.send(400, { error: "ticket_id, risk, and reason are required." });
    recordRequest(req.url ?? "unknown", 400);
    return;
  }

  const card = buildApprovalCard({
    ticketId: payload.ticket_id,
    risk: payload.risk,
    reason: payload.reason,
    ticketType: payload.ticket_type,
    traceId: payload.trace_id,
    traceUrl: payload.trace_url,
    details: payload.details,
  });

  res.send(200, { card });
  recordRequest(req.url ?? "unknown", 200);
});

server.post("/api/approvals/events", async (req, res) => {
  const secret = req.header("x-webhook-secret") as string | undefined;
  if (config.approvalWebhookSecret && secret !== config.approvalWebhookSecret) {
    res.send(401, { error: "Invalid webhook secret." });
    recordRequest(req.url ?? "unknown", 401);
    return;
  }

  try {
    const payload = req.body as {
      ticket_id: string;
      status: string;
      risk: string;
      reason: string;
      tenant_id?: string;
      ticket_type?: string;
      trace_id?: string;
      trace_url?: string;
      details?: Array<{ title: string; value: string }>;
    };
    const result = await notifyApproval(payload);
    res.send(200, result);
    recordRequest(req.url ?? "unknown", 200);
  } catch (err) {
    res.send(500, { error: err instanceof Error ? err.message : "Notify failed." });
    recordRequest(req.url ?? "unknown", 500);
  }
});

server.post("/api/break-glass/events", async (req, res) => {
  const secret = req.header("x-webhook-secret") as string | undefined;
  if (config.breakGlassWebhookSecret && secret !== config.breakGlassWebhookSecret) {
    res.send(401, { error: "Invalid webhook secret." });
    recordRequest(req.url ?? "unknown", 401);
    return;
  }

  try {
    const payload = req.body as {
      event_type: string;
      token_id: string;
      tenant_id: string;
      user_id: string;
      role: string;
      scope: string;
      occurred_utc: string;
    };
    const result = await notifyBreakGlass(payload);
    res.send(200, result);
    recordRequest(req.url ?? "unknown", 200);
  } catch (err) {
    res.send(500, { error: err instanceof Error ? err.message : "Notify failed." });
    recordRequest(req.url ?? "unknown", 500);
  }
});

server.post("/api/reports/events", async (req, res) => {
  const secret = req.header("x-webhook-secret") as string | undefined;
  if (config.reportWebhookSecret && secret !== config.reportWebhookSecret) {
    res.send(401, { error: "Invalid webhook secret." });
    recordRequest(req.url ?? "unknown", 401);
    return;
  }

  try {
    const payload = req.body as {
      event_type: string;
      tenant_id: string;
      report_id?: string;
      document_id?: string;
      title?: string;
      summary?: string;
      artifact_url?: string;
      trace_id?: string;
      trace_url?: string;
      occurred_utc?: string;
    };
    if (!payload?.event_type || !payload?.tenant_id) {
      res.send(400, { error: "event_type and tenant_id are required." });
      recordRequest(req.url ?? "unknown", 400);
      return;
    }
    const result = await notifyReport(payload);
    res.send(200, result);
    recordRequest(req.url ?? "unknown", 200);
  } catch (err) {
    res.send(500, { error: err instanceof Error ? err.message : "Notify failed." });
    recordRequest(req.url ?? "unknown", 500);
  }
});
