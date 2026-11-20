import { config } from "../config.js";

type TokenState = {
  accessToken: string;
  expiresAt: number;
};

const tokenState: TokenState = {
  accessToken: "",
  expiresAt: 0,
};

type OAuthResponse = {
  access_token: string;
  expires_in?: number;
  token_type?: string;
};

export async function getGatewayToken(): Promise<string> {
  if (config.gatewayStaticToken) {
    return config.gatewayStaticToken;
  }

  if (!config.gatewayOauthTokenUrl) {
    return "";
  }

  const now = Date.now();
  if (tokenState.accessToken && tokenState.expiresAt > now + 60_000) {
    return tokenState.accessToken;
  }

  const body = new URLSearchParams();
  body.set("grant_type", "client_credentials");
  if (config.gatewayOauthClientId) {
    body.set("client_id", config.gatewayOauthClientId);
  }
  if (config.gatewayOauthClientSecret) {
    body.set("client_secret", config.gatewayOauthClientSecret);
  }
  if (config.gatewayOauthScope) {
    body.set("scope", config.gatewayOauthScope);
  }

  const response = await fetch(config.gatewayOauthTokenUrl, {
    method: "POST",
    headers: {
      "content-type": "application/x-www-form-urlencoded",
    },
    body: body.toString(),
  });

  if (!response.ok) {
    throw new Error(`Token request failed: ${response.status}`);
  }

  const payload = (await response.json()) as OAuthResponse;
  if (!payload.access_token) {
    throw new Error("Token response missing access_token.");
  }

  const expiresIn = payload.expires_in ?? 3600;
  tokenState.accessToken = payload.access_token;
  tokenState.expiresAt = now + expiresIn * 1000;
  return tokenState.accessToken;
}
