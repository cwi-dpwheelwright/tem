import { createRemoteJWKSet, jwtVerify } from "jose";
import { config } from "../config.js";

type BotTokenClaims = {
  tid?: string;
  iss?: string;
  aud?: string | string[];
  appid?: string;
  azp?: string;
};

type ValidationResult =
  | { ok: true; claims: BotTokenClaims }
  | { ok: false; reason: string };

function parseAllowlist(raw: string): Set<string> {
  return new Set(
    raw
      .split(",")
      .map((entry) => entry.trim())
      .filter(Boolean)
  );
}

const tenantAllowlist = parseAllowlist(config.tenantAllowlist);

export function validateTokenClaims(claims: BotTokenClaims): ValidationResult {
  if (config.tokenIssuer && claims.iss !== config.tokenIssuer) {
    return { ok: false, reason: "Invalid token issuer." };
  }

  if (config.tokenAudience) {
    const audiences = Array.isArray(claims.aud) ? claims.aud : [claims.aud];
    if (!audiences.includes(config.tokenAudience)) {
      return { ok: false, reason: "Invalid token audience." };
    }
  }

  if (tenantAllowlist.size > 0) {
    const tenantId = claims.tid ?? "";
    if (!tenantAllowlist.has(tenantId)) {
      return { ok: false, reason: "Tenant not allowed." };
    }
  }

  return { ok: true, claims };
}

let jwks: ReturnType<typeof createRemoteJWKSet> | null = null;

export async function validateBotToken(
  authHeader: string | undefined
): Promise<ValidationResult> {
  if (!authHeader || !authHeader.startsWith("Bearer ")) {
    return { ok: false, reason: "Missing Authorization bearer token." };
  }

  if (!config.tokenJwksUrl) {
    return { ok: false, reason: "JWKS URL not configured." };
  }

  const token = authHeader.slice("Bearer ".length).trim();
  if (!jwks) {
    jwks = createRemoteJWKSet(new URL(config.tokenJwksUrl));
  }

  try {
    const { payload } = await jwtVerify(token, jwks, {
      issuer: config.tokenIssuer || undefined,
      audience: config.tokenAudience || undefined,
    });
    return validateTokenClaims(payload as BotTokenClaims);
  } catch (err) {
    return { ok: false, reason: err instanceof Error ? err.message : "Token validation failed." };
  }
}
