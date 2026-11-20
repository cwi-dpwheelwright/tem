import { decodeJwtPayload } from "@/lib/auth/jwt";
import { resolveIdentityBaseUrl } from "@/lib/identity/base-url";

export type IdentityProfile = {
  email?: string;
  username?: string;
  name?: string;
  subject?: string;
  tenantId?: string;
  isSuperAdmin?: boolean;
  cwiAdmin?: boolean;
};

export type IdentityLoginInput = {
  tenantId?: string;
  identifier: string;
  password: string;
};

export type IdentityLoginResult = {
  ok: boolean;
  token?: string;
  profile?: IdentityProfile;
  error?: string;
};

const TOKEN_KEY = "cwi.admin.console.identity.token";
const GPT_USER_TOKEN_KEY = "cwi.user.web.token.access";
const GPT_USER_TOKEN_LAST_USED_KEY = "cwi.user.web.token.lastUsed";

function readError(text: string): string {
  if (!text) {
    return "Request failed.";
  }
  try {
    const parsed = JSON.parse(text) as { error?: string; message?: string; details?: string[] };
    if (parsed.message) {
      return parsed.message;
    }
    if (parsed.error) {
      return parsed.error;
    }
    if (parsed.details && parsed.details.length > 0) {
      return parsed.details.join(" ");
    }
  } catch {
    return text;
  }
  return text;
}

function readStringClaim(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

export function readBooleanClaim(value: unknown): boolean {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    return normalized === "true" || normalized === "1" || normalized === "yes";
  }
  return false;
}

export function parseIdentityProfile(token: string): IdentityProfile {
  const claims = decodeJwtPayload(token) ?? {};
  return {
    email: readStringClaim(claims.email),
    username: readStringClaim(claims.preferred_username ?? claims.username),
    name: readStringClaim(claims.name),
    subject: readStringClaim(claims.sub),
    tenantId: readStringClaim(claims.tenant_id ?? claims.tenant ?? claims.tid ?? claims.tenantId),
    isSuperAdmin: readBooleanClaim(claims.is_superadmin),
    cwiAdmin: readBooleanClaim(claims.cwi_admin ?? claims.can_administer),
  };
}

export function getIdentityToken(): string | null {
  if (typeof window === "undefined") {
    return null;
  }
  return window.sessionStorage.getItem(TOKEN_KEY) ?? window.sessionStorage.getItem(GPT_USER_TOKEN_KEY);
}

export function setIdentityToken(token: string): void {
  if (typeof window === "undefined") {
    return;
  }
  window.sessionStorage.setItem(TOKEN_KEY, token);
}

export function clearIdentityToken(options: { clearSharedUserSession?: boolean } = {}): void {
  if (typeof window === "undefined") {
    return;
  }
  window.sessionStorage.removeItem(TOKEN_KEY);
  if (options.clearSharedUserSession) {
    window.sessionStorage.removeItem(GPT_USER_TOKEN_KEY);
    window.sessionStorage.removeItem(GPT_USER_TOKEN_LAST_USED_KEY);
  }
}

export async function loginWithIdentity(input: IdentityLoginInput): Promise<IdentityLoginResult> {
  const baseUrl = resolveIdentityBaseUrl();
  if (!baseUrl) {
    return { ok: false, error: "Identity base URL is not configured." };
  }

  const trimmedIdentifier = input.identifier.trim();
  if (!trimmedIdentifier || !input.password.trim()) {
    return { ok: false, error: "Provide username/email and password." };
  }

  const isEmail = trimmedIdentifier.includes("@");
  const payload = {
    tenant_id: input.tenantId?.trim() ? input.tenantId.trim() : undefined,
    email: isEmail ? trimmedIdentifier : undefined,
    username: isEmail ? undefined : trimmedIdentifier,
    password: input.password,
  };

  const response = await fetch(`${baseUrl}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });

  const text = await response.text();
  if (!response.ok) {
    return { ok: false, error: readError(text) };
  }

  if (!text) {
    return { ok: false, error: "Login did not return a token." };
  }

  try {
    const parsed = JSON.parse(text) as { access_token?: string };
    if (!parsed.access_token) {
      return { ok: false, error: "Login did not return a token." };
    }
    return {
      ok: true,
      token: parsed.access_token,
      profile: parseIdentityProfile(parsed.access_token),
    };
  } catch {
    return { ok: false, error: "Unable to parse login response." };
  }
}
