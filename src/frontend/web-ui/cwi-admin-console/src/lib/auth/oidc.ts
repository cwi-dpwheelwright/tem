import {
  User,
  UserManager,
  WebStorageStateStore,
  type UserManagerSettings,
} from "oidc-client-ts";
import { getRuntimeConfig } from "@/lib/config";

let manager: UserManager | null = null;

function getUserManager(): UserManager {
  if (manager) {
    return manager;
  }

  if (typeof window === "undefined") {
    throw new Error("OIDC user manager is only available in the browser.");
  }

  const config = getRuntimeConfig();
  if (
    !config.oidcAuthority ||
    !config.oidcClientId ||
    !config.oidcRedirectUri ||
    !config.oidcPostLogoutRedirectUri
  ) {
    throw new Error("OIDC configuration is missing required values.");
  }

  const settings: UserManagerSettings = {
    authority: config.oidcAuthority,
    client_id: config.oidcClientId,
    redirect_uri: config.oidcRedirectUri,
    post_logout_redirect_uri: config.oidcPostLogoutRedirectUri,
    response_type: "code",
    scope: config.oidcScopes.length > 0 ? config.oidcScopes.join(" ") : "openid profile email",
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  };

  manager = new UserManager(settings);
  return manager;
}

export async function getCurrentUser(): Promise<User | null> {
  return getUserManager().getUser();
}

export async function login(returnTo?: string): Promise<void> {
  await getUserManager().signinRedirect({
    state: { returnTo: returnTo ?? window.location.pathname },
  });
}

export async function handleLoginCallback(): Promise<User | null> {
  const user = await getUserManager().signinRedirectCallback();
  return user;
}

export async function logout(): Promise<void> {
  await getUserManager().signoutRedirect();
}

export async function getAccessToken(): Promise<string | null> {
  const user = await getCurrentUser();
  if (!user || user.expired) {
    return null;
  }
  return user.access_token;
}
