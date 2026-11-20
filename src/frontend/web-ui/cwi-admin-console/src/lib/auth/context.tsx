"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type { User } from "oidc-client-ts";
import { getAccessToken, getCurrentUser, login, logout } from "@/lib/auth/oidc";
import { extractRoles, type CwiRole, hasRole as hasRoleCheck } from "@/lib/auth/roles";
import { decodeJwtPayload } from "@/lib/auth/jwt";
import {
  clearIdentityToken,
  getIdentityToken,
  loginWithIdentity,
  parseIdentityProfile,
  readBooleanClaim,
  setIdentityToken,
  type IdentityLoginInput,
  type IdentityProfile,
} from "@/lib/auth/identity";
import { getRuntimeConfigStatus } from "@/lib/config";

export type AuthSource = "none" | "oidc" | "identity";

interface AuthContextValue {
  user: User | null;
  identityProfile: IdentityProfile | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  authSource: AuthSource;
  accessToken: string | null;
  roles: CwiRole[];
  isSuperAdmin: boolean;
  isAdminTenant: boolean;
  canAccessAdministration: boolean;
  hasRole: (role: CwiRole) => boolean;
  canUseOidc: boolean;
  signIn: () => Promise<void>;
  signInWithIdentity: (input: IdentityLoginInput) => Promise<{ ok: boolean; error?: string }>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function resolveIsSuperAdmin(user: User | null, accessToken?: string | null): boolean {
  const tokenClaims = decodeJwtPayload(accessToken ?? undefined);
  if (readBooleanClaim(tokenClaims?.is_superadmin)) {
    return true;
  }

  const profile = user?.profile as Record<string, unknown> | undefined;
  return readBooleanClaim(profile?.is_superadmin);
}

function resolveCanAccessAdministration(
  user: User | null,
  accessToken: string | null | undefined,
  roles: CwiRole[],
  identityProfile?: IdentityProfile | null,
): boolean {
  const tokenClaims = decodeJwtPayload(accessToken ?? undefined);
  if (readBooleanClaim(tokenClaims?.cwi_admin ?? tokenClaims?.can_administer)) {
    return true;
  }
  if (readBooleanClaim(tokenClaims?.is_superadmin) || Boolean(identityProfile?.isSuperAdmin)) {
    return true;
  }
  if (identityProfile?.cwiAdmin) {
    return true;
  }

  const profile = user?.profile as Record<string, unknown> | undefined;
  if (readBooleanClaim(profile?.cwi_admin ?? profile?.can_administer)) {
    return true;
  }

  return hasRoleCheck(roles, "platform_admin");
}

function normalizeTenantId(value?: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed.toLowerCase() : null;
}

function readTenantFromToken(accessToken?: string | null): string | null {
  const tokenClaims = decodeJwtPayload(accessToken ?? undefined);
  for (const claim of ["tenant_id", "tenant", "tid", "tenantId"]) {
    const value = tokenClaims?.[claim];
    if (typeof value === "string" && value.trim()) {
      return normalizeTenantId(value);
    }
  }

  return null;
}

function resolveAdminTenantId(): string {
  return getRuntimeConfigStatus().config?.adminTenantId ?? "CWI-ops";
}

function resolveIsAdminTenant(
  user: User | null,
  accessToken?: string | null,
  identityProfile?: IdentityProfile | null,
): boolean {
  const adminTenant = normalizeTenantId(resolveAdminTenantId());
  if (!adminTenant) {
    return false;
  }

  const profile = user?.profile as Record<string, unknown> | undefined;
  const profileTenantId = typeof profile?.tenant_id === "string" ? profile.tenant_id : null;
  const profileTenant = typeof profile?.tenant === "string" ? profile.tenant : null;
  const tenantId =
    normalizeTenantId(identityProfile?.tenantId) ??
    readTenantFromToken(accessToken) ??
    normalizeTenantId(profileTenantId) ??
    normalizeTenantId(profileTenant);

  return tenantId === adminTenant;
}

function hasOidcConfig(): boolean {
  const status = getRuntimeConfigStatus();
  const config = status.config;
  return !!(
    config?.oidcAuthority &&
    config?.oidcClientId &&
    config?.oidcRedirectUri &&
    config?.oidcPostLogoutRedirectUri
  );
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [identityProfile, setIdentityProfile] = useState<IdentityProfile | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [roles, setRoles] = useState<CwiRole[]>(["viewer"]);
  const [isSuperAdmin, setIsSuperAdmin] = useState(false);
  const [isAdminTenant, setIsAdminTenant] = useState(false);
  const [authSource, setAuthSource] = useState<AuthSource>("none");
  const [canUseOidc, setCanUseOidc] = useState(false);

  const refresh = useCallback(async () => {
    setIsLoading(true);
    const oidcEnabled = hasOidcConfig();
    setCanUseOidc(oidcEnabled);

    if (oidcEnabled) {
      try {
        const current = await getCurrentUser();
        const token = await getAccessToken();
        if (current && token) {
          clearIdentityToken();
          setUser(current);
          setIdentityProfile(null);
          setAccessToken(token);
          const nextRoles = extractRoles(current, token);
          setRoles(nextRoles);
          setIsSuperAdmin(resolveIsSuperAdmin(current, token));
          setIsAdminTenant(resolveIsAdminTenant(current, token));
          setAuthSource("oidc");
          setIsLoading(false);
          return;
        }
      } catch {
        // Fall through to identity token or unauthenticated state.
      }
    }

    const identityToken = getIdentityToken();
    if (identityToken) {
      const profile = parseIdentityProfile(identityToken);
      setUser(null);
      setIdentityProfile(profile);
      setAccessToken(identityToken);
      const nextRoles = extractRoles(null, identityToken);
      setRoles(nextRoles);
      setIsSuperAdmin(Boolean(profile.isSuperAdmin));
      setIsAdminTenant(resolveIsAdminTenant(null, identityToken, profile));
      setAuthSource("identity");
      setIsLoading(false);
      return;
    }

    setUser(null);
    setIdentityProfile(null);
    setAccessToken(null);
    setRoles(["viewer"]);
    setIsSuperAdmin(false);
    setIsAdminTenant(false);
    setAuthSource("none");
    setIsLoading(false);
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const signIn = useCallback(async () => {
    if (!hasOidcConfig()) {
      throw new Error("OIDC is not configured for this environment.");
    }
    await login(window.location.pathname);
  }, []);

  const signInWithIdentity = useCallback(async (input: IdentityLoginInput) => {
    const result = await loginWithIdentity(input);
    if (!result.ok || !result.token) {
      return { ok: false, error: result.error || "Login failed." };
    }

    const identityProfile = result.profile ?? parseIdentityProfile(result.token);
    const nextRoles = extractRoles(null, result.token);
    const nextIsSuperAdmin = Boolean(identityProfile.isSuperAdmin);
    const nextIsAdminTenant = resolveIsAdminTenant(null, result.token, identityProfile);
    const nextCanAccessAdministration = resolveCanAccessAdministration(
      null,
      result.token,
      nextRoles,
      identityProfile,
    );
    if (!nextCanAccessAdministration) {
      clearIdentityToken();
      setUser(null);
      setIdentityProfile(null);
      setAccessToken(null);
      setRoles(["viewer"]);
      setIsSuperAdmin(false);
      setIsAdminTenant(false);
      setAuthSource("none");
      return { ok: false, error: "Administrative access requires a platform-admin entitlement." };
    }

    setIdentityToken(result.token);
    setUser(null);
    setIdentityProfile(identityProfile);
    setAccessToken(result.token);
    setRoles(nextRoles);
    setIsSuperAdmin(nextIsSuperAdmin);
    setIsAdminTenant(nextIsAdminTenant);
    setAuthSource("identity");
    return { ok: true };
  }, []);

  const signOut = useCallback(async () => {
    clearIdentityToken({ clearSharedUserSession: true });
    setUser(null);
    setIdentityProfile(null);
    setAccessToken(null);
    setRoles(["viewer"]);
    setIsSuperAdmin(false);
    setIsAdminTenant(false);
    setAuthSource("none");

    if (hasOidcConfig()) {
      await logout();
    }
  }, []);

  const value = useMemo(
    () => ({
      user,
      identityProfile,
      isLoading,
      isAuthenticated: authSource !== "none",
      authSource,
      accessToken,
      roles,
      isSuperAdmin,
      isAdminTenant,
      canAccessAdministration: resolveCanAccessAdministration(user, accessToken, roles, identityProfile),
      hasRole: (role: CwiRole) => hasRoleCheck(roles, role),
      canUseOidc,
      signIn,
      signInWithIdentity,
      signOut,
    }),
    [
      user,
      identityProfile,
      isLoading,
      authSource,
      accessToken,
      roles,
      isSuperAdmin,
      isAdminTenant,
      canUseOidc,
      signIn,
      signInWithIdentity,
      signOut,
    ],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within AuthProvider.");
  }
  return ctx;
}
