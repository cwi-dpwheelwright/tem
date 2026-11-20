export type JwtClaims = Record<string, unknown>;

function decodeBase64Url(input: string): string | null {
  const normalized = input.replace(/-/g, "+").replace(/_/g, "/");
  const padding = normalized.length % 4;
  const padded = padding ? normalized + "=".repeat(4 - padding) : normalized;

  try {
    if (typeof window !== "undefined" && typeof window.atob === "function") {
      return window.atob(padded);
    }
    if (typeof globalThis !== "undefined" && "Buffer" in globalThis) {
      const buffer = (globalThis as { Buffer?: typeof Buffer }).Buffer;
      if (buffer) {
        return buffer.from(padded, "base64").toString("utf-8");
      }
    }
  } catch {
    return null;
  }

  return null;
}

export function decodeJwtPayload(token?: string | null): JwtClaims | null {
  if (!token) {
    return null;
  }

  const parts = token.split(".");
  if (parts.length < 2) {
    return null;
  }

  const decoded = decodeBase64Url(parts[1]);
  if (!decoded) {
    return null;
  }

  try {
    return JSON.parse(decoded) as JwtClaims;
  } catch {
    return null;
  }
}
