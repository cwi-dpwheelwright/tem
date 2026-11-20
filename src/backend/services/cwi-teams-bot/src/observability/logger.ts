const REDACT_PATTERNS = ["authorization", "password", "token", "secret"];

export function redactHeaders(headers: Record<string, unknown>): Record<string, unknown> {
  const redacted: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(headers)) {
    const lower = key.toLowerCase();
    if (REDACT_PATTERNS.some((pattern) => lower.includes(pattern))) {
      redacted[key] = "[REDACTED]";
    } else {
      redacted[key] = value;
    }
  }
  return redacted;
}

export function logInfo(message: string, data?: Record<string, unknown>) {
  if (data) {
    // eslint-disable-next-line no-console
    console.log(message, data);
  } else {
    // eslint-disable-next-line no-console
    console.log(message);
  }
}

export function logError(message: string, data?: Record<string, unknown>) {
  if (data) {
    // eslint-disable-next-line no-console
    console.error(message, data);
  } else {
    // eslint-disable-next-line no-console
    console.error(message);
  }
}
