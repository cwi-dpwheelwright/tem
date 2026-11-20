import type { ApiError } from "./api/client";

const TOKEN_RE = /(bearer\s+)?[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{10,}/gi;
const SECRET_KEYS = new Set(["authorization", "token", "password", "secret"]);

function redactString(value: string): string {
  return value.replace(TOKEN_RE, "[redacted]");
}

function sanitizePayload(payload: Record<string, unknown> | null | undefined): Record<string, unknown> | null {
  if (!payload) {
    return null;
  }
  const cleaned: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(payload)) {
    if (SECRET_KEYS.has(key.toLowerCase())) {
      cleaned[key] = "[redacted]";
      continue;
    }
    if (typeof value === "string") {
      cleaned[key] = redactString(value);
      continue;
    }
    cleaned[key] = value;
  }
  return cleaned;
}

export function logApiError(error: ApiError): void {
  if (!error) {
    return;
  }
  const entry = {
    message: error.message,
    status: error.status,
    requestId: error.requestId,
    payload: sanitizePayload(error.payload),
  };
  // eslint-disable-next-line no-console
  console.error("api.error", entry);
}
