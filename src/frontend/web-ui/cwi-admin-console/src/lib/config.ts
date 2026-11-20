export interface RuntimeConfig {
  apiBaseUrl: string;
  identityBaseUrl: string;
  adminTenantId: string;
  packsBaseUrl?: string;
  oidcAuthority?: string;
  oidcClientId?: string;
  oidcRedirectUri?: string;
  oidcPostLogoutRedirectUri?: string;
  oidcScopes: string[];
}

export interface ConfigValidationErrors {
  apiBaseUrl?: string;
  identityBaseUrl?: string;
  packsBaseUrl?: string;
  oidcAuthority?: string;
  oidcRedirectUri?: string;
  oidcPostLogoutRedirectUri?: string;
}

export interface RuntimeConfigStatus {
  config: RuntimeConfig | null;
  missing: string[];
  errors: ConfigValidationErrors;
}

const REQUIRED_PUBLIC_ENV = ["NEXT_PUBLIC_API_BASE_URL", "NEXT_PUBLIC_IDENTITY_BASE_URL"];
const OIDC_ENV = [
  "NEXT_PUBLIC_OIDC_AUTHORITY",
  "NEXT_PUBLIC_OIDC_CLIENT_ID",
  "NEXT_PUBLIC_OIDC_REDIRECT_URI",
  "NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI",
];

const PUBLIC_ENV = {
  NEXT_PUBLIC_API_BASE_URL: process.env.NEXT_PUBLIC_API_BASE_URL,
  NEXT_PUBLIC_IDENTITY_BASE_URL: process.env.NEXT_PUBLIC_IDENTITY_BASE_URL,
  NEXT_PUBLIC_PACKS_BASE_URL: process.env.NEXT_PUBLIC_PACKS_BASE_URL,
  NEXT_PUBLIC_ADMIN_TENANT_ID: process.env.NEXT_PUBLIC_ADMIN_TENANT_ID,
  NEXT_PUBLIC_CWI_ADMIN_TENANT: process.env.NEXT_PUBLIC_CWI_ADMIN_TENANT,
  NEXT_PUBLIC_OIDC_AUTHORITY: process.env.NEXT_PUBLIC_OIDC_AUTHORITY,
  NEXT_PUBLIC_OIDC_CLIENT_ID: process.env.NEXT_PUBLIC_OIDC_CLIENT_ID,
  NEXT_PUBLIC_OIDC_REDIRECT_URI: process.env.NEXT_PUBLIC_OIDC_REDIRECT_URI,
  NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI: process.env.NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI,
  NEXT_PUBLIC_OIDC_SCOPES: process.env.NEXT_PUBLIC_OIDC_SCOPES,
} as const;

function readEnv(name: string): string | null {
  const value =
    typeof window === "undefined"
      ? process.env[name]
      : (PUBLIC_ENV as Record<string, string | undefined>)[name];
  if (!value || !value.trim()) {
    return null;
  }
  return value.trim();
}

function normalizeUrl(value: string): string {
  return value.replace(/\/$/, "");
}

function validateUrl(label: string, value: string): string | null {
  if (!value.trim()) {
    return `${label} is required.`;
  }
  try {
    const parsed = new URL(value);
    if (!parsed.protocol.startsWith("http")) {
      return `${label} must start with http or https.`;
    }
  } catch {
    return `${label} must be a valid URL.`;
  }
  return null;
}


export function getRuntimeConfig(): RuntimeConfig {
  const status = getRuntimeConfigStatus();
  if (!status.config) {
    throw new Error(`Missing required config: ${status.missing.join(", ")}`);
  }
  return status.config;
}

export function getRuntimeConfigStatus(): RuntimeConfigStatus {
  const apiBaseUrl = readEnv("NEXT_PUBLIC_API_BASE_URL");
  const identityBaseUrl = readEnv("NEXT_PUBLIC_IDENTITY_BASE_URL");
  const packsBaseUrl = readEnv("NEXT_PUBLIC_PACKS_BASE_URL");
  const adminTenantId =
    readEnv("NEXT_PUBLIC_ADMIN_TENANT_ID") ??
    readEnv("NEXT_PUBLIC_CWI_ADMIN_TENANT") ??
    "CWI-ops";
  const oidcAuthority = readEnv("NEXT_PUBLIC_OIDC_AUTHORITY");
  const oidcClientId = readEnv("NEXT_PUBLIC_OIDC_CLIENT_ID");
  const oidcRedirectUri = readEnv("NEXT_PUBLIC_OIDC_REDIRECT_URI");
  const oidcPostLogoutRedirectUri = readEnv("NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI");
  const oidcScopesRaw = readEnv("NEXT_PUBLIC_OIDC_SCOPES");

  const missing = new Set(REQUIRED_PUBLIC_ENV.filter((key) => !readEnv(key)));

  const hasAnyOidc =
    !!oidcAuthority || !!oidcClientId || !!oidcRedirectUri || !!oidcPostLogoutRedirectUri;
  if (hasAnyOidc) {
    for (const key of OIDC_ENV) {
      if (!readEnv(key)) {
        missing.add(key);
      }
    }
  }

  const errors: ConfigValidationErrors = {};
  if (apiBaseUrl) {
    const error = validateUrl("API base URL", apiBaseUrl);
    if (error) {
      errors.apiBaseUrl = error;
    }
  }
  if (identityBaseUrl) {
    const error = validateUrl("Identity base URL", identityBaseUrl);
    if (error) {
      errors.identityBaseUrl = error;
    }
  }
  if (packsBaseUrl) {
    const error = validateUrl("Packs base URL", packsBaseUrl);
    if (error) {
      errors.packsBaseUrl = error;
    }
  }
  if (oidcAuthority) {
    const error = validateUrl("OIDC authority", oidcAuthority);
    if (error) {
      errors.oidcAuthority = error;
    }
  }
  if (oidcRedirectUri) {
    const error = validateUrl("OIDC redirect URI", oidcRedirectUri);
    if (error) {
      errors.oidcRedirectUri = error;
    }
  }
  if (oidcPostLogoutRedirectUri) {
    const error = validateUrl("OIDC post-logout redirect URI", oidcPostLogoutRedirectUri);
    if (error) {
      errors.oidcPostLogoutRedirectUri = error;
    }
  }

  const missingList = Array.from(missing.values());
  if (missingList.length > 0) {
    return { config: null, missing: missingList, errors };
  }

  return {
    config: {
      apiBaseUrl: normalizeUrl(apiBaseUrl ?? ""),
      identityBaseUrl: normalizeUrl(identityBaseUrl ?? ""),
      adminTenantId,
      packsBaseUrl: packsBaseUrl ? normalizeUrl(packsBaseUrl) : undefined,
      oidcAuthority: oidcAuthority ? normalizeUrl(oidcAuthority) : undefined,
      oidcClientId: oidcClientId ?? undefined,
      oidcRedirectUri: oidcRedirectUri ? normalizeUrl(oidcRedirectUri) : undefined,
      oidcPostLogoutRedirectUri: oidcPostLogoutRedirectUri
        ? normalizeUrl(oidcPostLogoutRedirectUri)
        : undefined,
      oidcScopes: oidcScopesRaw ? oidcScopesRaw.split(/\s+/).filter(Boolean) : [],
    },
    missing: [],
    errors,
  };
}
