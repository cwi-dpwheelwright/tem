import { createClient } from "redis";
import pg from "pg";
import { config } from "./config.js";

export type HealthStatus = "ok" | "down" | "skipped";

export type HealthCheck = {
  status: HealthStatus;
  detail?: string;
  latencyMs?: number;
};

export type HealthResult = {
  ok: boolean;
  checks: Record<string, HealthCheck>;
};

const DEFAULT_TIMEOUT_MS = 2000;

function withTimeout<T>(
  promise: Promise<T>,
  timeoutMs: number,
  label: string,
  onTimeout?: () => void,
): Promise<T> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      if (onTimeout) {
        try {
          onTimeout();
        } catch {
          // ignore abort errors
        }
      }
      reject(new Error(`${label} timeout`));
    }, timeoutMs);

    promise
      .then((value) => {
        clearTimeout(timer);
        resolve(value);
      })
      .catch((err) => {
        clearTimeout(timer);
        reject(err);
      });
  });
}

async function checkGateway(): Promise<HealthCheck> {
  const start = Date.now();
  try {
    const controller = new AbortController();
    const response = await withTimeout(
      fetch(`${config.gatewayBaseUrl}/api/health`, { signal: controller.signal }),
      DEFAULT_TIMEOUT_MS,
      "gateway",
      () => controller.abort(),
    );
    if (!response.ok) {
      return { status: "down", detail: `HTTP ${response.status}`, latencyMs: Date.now() - start };
    }
    return { status: "ok", latencyMs: Date.now() - start };
  } catch (err) {
    return {
      status: "down",
      detail: err instanceof Error ? err.name : "error",
      latencyMs: Date.now() - start,
    };
  }
}

async function checkRedis(): Promise<HealthCheck> {
  if (!config.redisUrl) {
    return { status: "skipped", detail: "not_configured" };
  }

  const start = Date.now();
  const client = createClient({
    url: config.redisUrl,
    socket: {
      connectTimeout: 1000,
    },
  });

  try {
    await withTimeout(client.connect(), 1500, "redis connect");
    await withTimeout(client.ping(), 1000, "redis ping");
    return { status: "ok", latencyMs: Date.now() - start };
  } catch (err) {
    return {
      status: "down",
      detail: err instanceof Error ? err.name : "error",
      latencyMs: Date.now() - start,
    };
  } finally {
    if (client.isOpen) {
      try {
        await client.quit();
      } catch {
        client.disconnect();
      }
    }
  }
}

async function checkPostgres(): Promise<HealthCheck> {
  if (!config.postgresUrl) {
    return { status: "skipped", detail: "not_configured" };
  }

  const start = Date.now();
  const pool = new pg.Pool({
    connectionString: config.postgresUrl,
    max: 1,
    connectionTimeoutMillis: 1000,
    statement_timeout: 1000,
  });

  try {
    await withTimeout(pool.query("SELECT 1"), 1500, "postgres query");
    return { status: "ok", latencyMs: Date.now() - start };
  } catch (err) {
    return {
      status: "down",
      detail: err instanceof Error ? err.name : "error",
      latencyMs: Date.now() - start,
    };
  } finally {
    await pool.end().catch(() => undefined);
  }
}

export async function runHealthChecks(): Promise<HealthResult> {
  const safeCheck = async (
    name: string,
    action: () => Promise<HealthCheck>,
  ): Promise<HealthCheck> => {
    try {
      return await action();
    } catch (err) {
      const fallback: HealthCheck = {
        status: "down",
        detail:
          err instanceof Error
            ? `${name}:${err.name}`
            : `${name}:error`,
      };
      return fallback;
    }
  };

  const [gateway, redis, postgres] = await Promise.all([
    safeCheck("gateway", checkGateway),
    safeCheck("redis", checkRedis),
    safeCheck("postgres", checkPostgres),
  ]);

  const ok =
    gateway.status == "ok" &&
    redis.status != "down" &&
    postgres.status != "down";

  return {
    ok,
    checks: {
      gateway,
      redis,
      postgres,
    },
  };
}
