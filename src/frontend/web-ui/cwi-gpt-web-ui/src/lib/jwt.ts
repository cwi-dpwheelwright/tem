export const DEFAULT_USER_ID_CLAIMS = ["sub", "email", "preferred_username", "upn", "name", "user_id"];
export const DEFAULT_TENANT_CLAIMS = ["tenant", "tenant_id", "tid", "org", "organization", "tenantId"];

type JwtPayload = Record<string, unknown> | null;

function decodeBase64Url(value: string): string {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), "=");
  if (typeof window !== "undefined" && typeof window.atob === "function") {
    return window.atob(padded);
  }
  return Buffer.from(padded, "base64").toString("utf8");
}

export function decodeJwtPayload(token?: string | null): JwtPayload {
  if (!token) {
    return null;
  }
  const parts = token.split(".");
  if (parts.length < 2) {
    return null;
  }
  try {
    const json = decodeBase64Url(parts[1]);
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return null;
  }
}

export function getJwtExpiration(token?: string | null): number | null {
  const payload = decodeJwtPayload(token);
  if (!payload) {
    return null;
  }
  const raw = payload.exp;
  if (typeof raw === "number" && Number.isFinite(raw)) {
    return raw * 1000;
  }
  if (typeof raw === "string") {
    const parsed = Number(raw);
    if (Number.isFinite(parsed)) {
      return parsed * 1000;
    }
  }
  return null;
}

export function isJwtExpired(token?: string | null, skewMs = 30000): boolean {
  const expiresAt = getJwtExpiration(token);
  if (!expiresAt) {
    return false;
  }
  return Date.now() >= expiresAt - skewMs;
}

export function getFirstStringClaim(payload: JwtPayload, claimNames: string[]): string | null {
  if (!payload) {
    return null;
  }
  for (const claim of claimNames) {
    const value = payload[claim];
    if (typeof value === "string" && value.trim()) {
      return value.trim();
    }
    if (Array.isArray(value)) {
      const first = value.find((entry) => typeof entry === "string" && entry.trim());
      if (typeof first === "string") {
        return first.trim();
      }
    }
  }
  return null;
}

export function getBooleanClaim(payload: JwtPayload, claimNames: string[]): boolean {
  if (!payload) {
    return false;
  }

  for (const claim of claimNames) {
    const value = payload[claim];
    if (typeof value === "boolean") {
      return value;
    }
    if (typeof value === "number") {
      return value !== 0;
    }
    if (typeof value === "string") {
      const normalized = value.trim().toLowerCase();
      if (normalized === "true" || normalized === "1" || normalized === "yes") {
        return true;
      }
    }
  }

  return false;
}
