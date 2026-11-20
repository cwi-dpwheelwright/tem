import { UserManager, WebStorageStateStore, type User, type UserManagerSettings } from "oidc-client-ts";
import { saveAuthToken } from "./storage";

let manager: UserManager | null = null;

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

function readEnv(name: string): string | null {
  const value = process.env[name];
  if (!value || !value.trim()) {
    return null;
  }
  return value.trim();
}

function buildSettings(): UserManagerSettings | null {
  const authority = readEnv("NEXT_PUBLIC_CWI_OIDC_AUTHORITY");
  const clientId = readEnv("NEXT_PUBLIC_CWI_OIDC_CLIENT_ID");
  const redirectUri = readEnv("NEXT_PUBLIC_CWI_OIDC_REDIRECT_URI");
  const postLogoutRedirectUri = readEnv("NEXT_PUBLIC_CWI_OIDC_POST_LOGOUT_REDIRECT_URI");
  const scope = readEnv("NEXT_PUBLIC_CWI_OIDC_SCOPES") ?? "openid profile email";

  if (!authority || !clientId || !redirectUri) {
    return null;
  }

  return {
    authority,
    client_id: clientId,
    redirect_uri: redirectUri,
    post_logout_redirect_uri: postLogoutRedirectUri ?? redirectUri,
    response_type: "code",
    scope,
    userStore: hasWindow() ? new WebStorageStateStore({ store: window.localStorage }) : undefined,
  };
}

function getManager(): UserManager | null {
  if (!hasWindow()) {
    return null;
  }
  if (manager) {
    return manager;
  }
  const settings = buildSettings();
  if (!settings) {
    return null;
  }
  manager = new UserManager(settings);
  return manager;
}

export function isOidcConfigured(): boolean {
  return Boolean(buildSettings());
}

export async function login(returnTo?: string): Promise<void> {
  const mgr = getManager();
  if (!mgr) {
    throw new Error("OIDC is not configured.");
  }
  await mgr.signinRedirect({ state: { returnTo } });
}

export async function handleLoginCallback(): Promise<User | null> {
  const mgr = getManager();
  if (!mgr) {
    throw new Error("OIDC is not configured.");
  }
  const user = await mgr.signinRedirectCallback();
  if (user?.access_token) {
    saveAuthToken(user.access_token);
  }
  return user;
}

export async function logout(): Promise<void> {
  const mgr = getManager();
  if (!mgr) {
    return;
  }
  await mgr.signoutRedirect();
}

export async function getOidcUser(): Promise<User | null> {
  const mgr = getManager();
  if (!mgr) {
    return null;
  }
  return await mgr.getUser();
}
