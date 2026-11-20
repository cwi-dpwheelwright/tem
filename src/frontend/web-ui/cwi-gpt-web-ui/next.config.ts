import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  turbopack: {
    root: process.cwd(),
  },
  images: {
    unoptimized: true,
  },
  env: {
    NEXT_PUBLIC_CWI_GATEWAY_BASE_URL: process.env.NEXT_PUBLIC_CWI_GATEWAY_BASE_URL,
    NEXT_PUBLIC_CWI_OIDC_AUTHORITY: process.env.NEXT_PUBLIC_CWI_OIDC_AUTHORITY,
    NEXT_PUBLIC_CWI_OIDC_CLIENT_ID: process.env.NEXT_PUBLIC_CWI_OIDC_CLIENT_ID,
    NEXT_PUBLIC_CWI_OIDC_REDIRECT_URI: process.env.NEXT_PUBLIC_CWI_OIDC_REDIRECT_URI,
    NEXT_PUBLIC_CWI_OIDC_POST_LOGOUT_REDIRECT_URI:
      process.env.NEXT_PUBLIC_CWI_OIDC_POST_LOGOUT_REDIRECT_URI,
    NEXT_PUBLIC_CWI_OIDC_SCOPES: process.env.NEXT_PUBLIC_CWI_OIDC_SCOPES,
    NEXT_PUBLIC_CWI_AUTH_MODE: process.env.NEXT_PUBLIC_CWI_AUTH_MODE,
    NEXT_PUBLIC_CWI_ERROR_REPORT_URL: process.env.NEXT_PUBLIC_CWI_ERROR_REPORT_URL,
    NEXT_PUBLIC_CWI_REQUEST_TIMEOUT_MS: process.env.NEXT_PUBLIC_CWI_REQUEST_TIMEOUT_MS,
    NEXT_PUBLIC_CWI_BUILD_VERSION: process.env.NEXT_PUBLIC_CWI_BUILD_VERSION,
    NEXT_PUBLIC_CWI_BUILD_SHA: process.env.NEXT_PUBLIC_CWI_BUILD_SHA,
  },
  async headers() {
    const gatewayBaseUrl = process.env.NEXT_PUBLIC_CWI_GATEWAY_BASE_URL;
    let gatewayOrigin: string | null = null;
    if (gatewayBaseUrl) {
      try {
        gatewayOrigin = new URL(gatewayBaseUrl).origin;
      } catch {
        gatewayOrigin = null;
      }
    }
    const oidcAuthority = process.env.NEXT_PUBLIC_CWI_OIDC_AUTHORITY;
    const errorReportUrl = process.env.NEXT_PUBLIC_CWI_ERROR_REPORT_URL;
    let errorReportOrigin: string | null = null;
    if (errorReportUrl) {
      try {
        errorReportOrigin = new URL(errorReportUrl).origin;
      } catch {
        errorReportOrigin = null;
      }
    }
    let oidcOrigin: string | null = null;
    if (oidcAuthority) {
      try {
        oidcOrigin = new URL(oidcAuthority).origin;
      } catch {
        oidcOrigin = null;
      }
    }
    const isProd = process.env.NODE_ENV === "production";
    const connectSrcValues = ["'self'", gatewayOrigin, oidcOrigin, errorReportOrigin].filter(Boolean);
    if (!isProd) {
      connectSrcValues.push("ws:", "wss:");
    }
    const connectSrc = connectSrcValues.join(" ");
    const scriptSrc = isProd
      ? "script-src 'self' 'unsafe-inline'"
      : "script-src 'self' 'unsafe-inline' 'unsafe-eval'";
    const csp = [
      "default-src 'self'",
      "img-src 'self' data: blob:",
      "style-src 'self' 'unsafe-inline'",
      scriptSrc,
      `connect-src ${connectSrc}`,
      "frame-ancestors 'none'",
    ].join("; ");

    return [
      {
        source: "/(.*)",
        headers: [
          { key: "Content-Security-Policy", value: csp },
          { key: "Referrer-Policy", value: "no-referrer" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Permissions-Policy", value: "camera=(self), microphone=(), geolocation=()" },
        ],
      },
    ];
  },
};

export default nextConfig;
