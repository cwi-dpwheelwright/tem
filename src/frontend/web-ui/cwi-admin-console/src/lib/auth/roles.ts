import type { User } from "oidc-client-ts";
import { decodeJwtPayload } from "@/lib/auth/jwt";

export type CwiRole = "viewer" | "operator" | "admin" | "platform_admin";

const ROLE_ORDER: Record<CwiRole, number> = {
  viewer: 1,
  operator: 2,
  admin: 3,
  platform_admin: 4,
};

function normalizeRole(value: string): CwiRole | null {
  const cleaned = value.trim().toLowerCase();
  if (cleaned === "platform_admin" || cleaned === "system_admin") {
    return "platform_admin";
  }
  if (cleaned === "admin" || cleaned === "operator" || cleaned === "viewer") {
    return cleaned as CwiRole;
  }
  return null;
}

function collectRoles(source: unknown): CwiRole[] {
  if (!source) {
    return [];
  }
  if (Array.isArray(source)) {
    return source
      .map((item) => (typeof item === "string" ? normalizeRole(item) : null))
      .filter((role): role is CwiRole => Boolean(role));
  }
  if (typeof source === "string") {
    return source
      .split(/[\s,]+/)
      .map((item) => normalizeRole(item))
      .filter((role): role is CwiRole => Boolean(role));
  }
  return [];
}

export function extractRoles(user: User | null, accessToken?: string | null): CwiRole[] {
  const roles = new Set<CwiRole>();

  const claims = decodeJwtPayload(accessToken ?? undefined);
  const claimRoles = collectRoles(
    claims?.roles ?? claims?.role ?? claims?.permissions ?? claims?.cwi_roles,
  );
  claimRoles.forEach((role) => roles.add(role));

  const profile = user?.profile as Record<string, unknown> | undefined;
  const profileRoles = collectRoles(
    profile?.roles ?? profile?.role ?? profile?.permissions ?? profile?.cwi_roles,
  );
  profileRoles.forEach((role) => roles.add(role));

  if (roles.size === 0) {
    roles.add("viewer");
  }

  return Array.from(roles).sort((a, b) => ROLE_ORDER[b] - ROLE_ORDER[a]);
}

export function hasRole(roles: CwiRole[], required: CwiRole): boolean {
  const requiredRank = ROLE_ORDER[required];
  return roles.some((role) => ROLE_ORDER[role] >= requiredRank);
}
