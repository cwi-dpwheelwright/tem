import type { AuthMode } from "./config";
import { expireAuthSession, loadAuthToken, loadAuthTokenLastUsed, touchAuthToken } from "./storage";
import { getOidcUser } from "./oidc";
import { ApiError } from "./api/client";
import { isJwtExpired } from "./jwt";

export const AUTH_NOTICE_MESSAGE = "Session expired. Please sign in again.";
const EXPIRY_SKEW_MS = 30000;

function isSessionIdleExpired(authIdleTimeoutMs: number): boolean {
  if (authIdleTimeoutMs <= 0) {
    return false;
  }
  const lastUsed = loadAuthTokenLastUsed();
  return Boolean(lastUsed && Date.now() - lastUsed > authIdleTimeoutMs);
}

export async function peekAuthToken(
  authMode: AuthMode,
  authIdleTimeoutMs: number,
): Promise<string | null> {
  if (authMode === "none") {
    return null;
  }

  const token =
    authMode === "oidc"
      ? (await getOidcUser())?.access_token ?? null
      : loadAuthToken();

  if (!token) {
    return null;
  }

  if (isJwtExpired(token, EXPIRY_SKEW_MS)) {
    expireAuthSession(AUTH_NOTICE_MESSAGE);
    return null;
  }

  if (isSessionIdleExpired(authIdleTimeoutMs)) {
    expireAuthSession(AUTH_NOTICE_MESSAGE);
    return null;
  }

  return token;
}

export async function resolveAuthToken(
  authMode: AuthMode,
  authIdleTimeoutMs: number,
): Promise<string | null> {
  const token = await peekAuthToken(authMode, authIdleTimeoutMs);
  if (!token) {
    return null;
  }
  touchAuthToken();
  return token;
}

export function handleAuthRedirect(
  error: unknown,
  redirect: (href: string) => void,
  notice: string,
  authMode: AuthMode,
): boolean {
  if (authMode === "none") {
    return false;
  }
  if (error instanceof ApiError) {
    if (error.status === 401) {
      expireAuthSession(notice);
      redirect("/login");
      return true;
    }
  }

  if (authMode === "oidc" && error instanceof Error) {
    if (error.message.toLowerCase().includes("login")) {
      expireAuthSession(notice);
      redirect("/login");
      return true;
    }
  }

  return false;
}
