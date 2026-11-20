"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { resolveRuntimeConfig } from "@/lib/config";
import { decodeJwtPayload, getBooleanClaim, getFirstStringClaim } from "@/lib/jwt";
import { clearAuthSession, loadOverrides, loadUserLabel } from "@/lib/storage";
import { resolveAuthToken } from "@/lib/auth";
import { logout } from "@/lib/oidc";
import { resolveSessionIdentity } from "@/lib/session-identity";

const HIDE_PATHS = new Set(["/login", "/auth/callback"]);
const DISPLAY_USER_CLAIMS = ["name", "preferred_username", "email", "upn", "user_id", "sub"];

function normalizeTenantId(value?: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed.toLowerCase() : null;
}

function isGuidLike(value: string | null): boolean {
  if (!value) {
    return false;
  }
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.trim());
}

function resolveUserLabel(token: string | null): string | null {
  const claims = decodeJwtPayload(token);
  return getFirstStringClaim(claims, DISPLAY_USER_CLAIMS);
}

function hasRoleClaim(claims: Record<string, unknown> | null, expectedRoles: string[]): boolean {
  if (!claims) {
    return false;
  }

  const expected = new Set(expectedRoles.map((role) => role.toLowerCase()));
  const rawRoles = claims.roles ?? claims.role ?? claims.permissions ?? claims.cwi_roles;
  const values = Array.isArray(rawRoles) ? rawRoles : typeof rawRoles === "string" ? rawRoles.split(/[\s,]+/) : [];
  return values.some((value) => typeof value === "string" && expected.has(value.trim().toLowerCase()));
}

export function useSessionIdentity() {
  const router = useRouter();
  const pathname = usePathname();
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const [token, setToken] = useState<string | null>(null);
  const [userLabel, setUserLabel] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const syncToken = async () => {
      const current = await resolveAuthToken(
        runtime.config.authMode,
        runtime.config.authIdleTimeoutMs,
      );
      if (!active) {
        return;
      }
      setToken(current);
      setUserLabel(resolveUserLabel(current));
    };
    void syncToken();

    const onStorage = (event: StorageEvent) => {
      if (!event.key || event.key.startsWith("cwi.user.web.token")) {
        void syncToken();
      }
    };
    window.addEventListener("storage", onStorage);
    return () => {
      active = false;
      window.removeEventListener("storage", onStorage);
    };
  }, [runtime.config.authIdleTimeoutMs, runtime.config.authMode]);

  const isHidden = pathname ? HIDE_PATHS.has(pathname) : false;
  const isVisible = runtime.config.authMode !== "none" && !isHidden;

  const storedLabel = loadUserLabel();
  const safeUserLabel = isGuidLike(userLabel) ? null : userLabel;
  const claims = decodeJwtPayload(token);
  const isSuperAdmin = getBooleanClaim(claims, ["is_superadmin"]);
  const resolvedIdentity = resolveSessionIdentity({
    authMode: runtime.config.authMode,
    tenantId: runtime.config.tenantId,
    runtimeUserId: runtime.config.userId,
    token,
  });
  const isAdminTenant =
    normalizeTenantId(resolvedIdentity.tenantId) === normalizeTenantId(runtime.config.adminTenantId);
  const isCwiAdmin = getBooleanClaim(claims, ["cwi_admin", "can_administer"]);
  const canAccessAdministration =
    isSuperAdmin || isCwiAdmin || hasRoleClaim(claims, ["platform_admin", "system_admin"]);
  const safeResolvedUserId = isGuidLike(resolvedIdentity.userId) ? null : resolvedIdentity.userId;
  const displayName = safeUserLabel || storedLabel || safeResolvedUserId || "Active session";

  const handleLogout = useCallback(async () => {
    clearAuthSession();
    if (runtime.config.authMode === "oidc") {
      try {
        await logout();
        return;
      } catch {
        router.replace("/login");
        return;
      }
    }
    router.replace("/login");
  }, [router, runtime.config.authMode]);

  return {
    authMode: runtime.config.authMode,
    displayName,
    isVisible,
    isSuperAdmin,
    isAdminTenant,
    canAccessAdministration,
    handleLogout,
    tenantId: resolvedIdentity.tenantId,
    userId: resolvedIdentity.userId,
  };
}

export function SessionBadge() {
  const { displayName, isVisible, canAccessAdministration, handleLogout } = useSessionIdentity();
  if (!isVisible) {
    return null;
  }

  const sessionInitial = displayName.trim().charAt(0).toUpperCase() || "A";

  return (
    <div className="session-badge" role="status" aria-live="polite">
      <Link className="session-badge-link" href="/settings">
        <span className="session-badge-avatar" aria-hidden="true">{sessionInitial}</span>
        <span className="session-badge-copy">
          <span className="session-badge-title">Account</span>
          <span className="session-badge-user">{displayName}</span>
          <span className="session-badge-meta">Open account settings</span>
        </span>
      </Link>
      <div className="session-badge-actions">
        {canAccessAdministration ? (
          <Link className="session-badge-button session-badge-button--secondary" href="/administration">
            Go to Administration
          </Link>
        ) : null}
        <button className="session-badge-button" type="button" onClick={() => void handleLogout()}>
          Logout
        </button>
      </div>
    </div>
  );
}
