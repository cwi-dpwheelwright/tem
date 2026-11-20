import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  images: {
    unoptimized: true,
  },
  env: {
    NEXT_PUBLIC_API_BASE_URL: process.env.NEXT_PUBLIC_API_BASE_URL,
    NEXT_PUBLIC_OIDC_AUTHORITY: process.env.NEXT_PUBLIC_OIDC_AUTHORITY,
    NEXT_PUBLIC_OIDC_CLIENT_ID: process.env.NEXT_PUBLIC_OIDC_CLIENT_ID,
    NEXT_PUBLIC_OIDC_REDIRECT_URI: process.env.NEXT_PUBLIC_OIDC_REDIRECT_URI,
    NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI:
      process.env.NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI,
    NEXT_PUBLIC_OIDC_SCOPES: process.env.NEXT_PUBLIC_OIDC_SCOPES,
    NEXT_PUBLIC_PACKS_BASE_URL: process.env.NEXT_PUBLIC_PACKS_BASE_URL,
  },
  async headers() {
    const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL;
    const apiOrigin = apiBaseUrl ? new URL(apiBaseUrl).origin : null;
    const packsBaseUrl = process.env.NEXT_PUBLIC_PACKS_BASE_URL;
    let packsOrigin: string | null = null;
    if (packsBaseUrl) {
      try {
        packsOrigin = new URL(packsBaseUrl).origin;
      } catch {
        packsOrigin = null;
      }
    }
    const isProd = process.env.NODE_ENV === "production";
    const connectSrcValues = ["'self'", apiOrigin, packsOrigin].filter(Boolean);
    if (!isProd) {
      connectSrcValues.push("ws:", "wss:");
    }
    const connectSrc = connectSrcValues.join(" ");
    const scriptSrc = isProd
      ? "script-src 'self' 'unsafe-inline'"
      : "script-src 'self' 'unsafe-inline' 'unsafe-eval'";
    const csp = [
      "default-src 'self'",
      "img-src 'self' data:",
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
          { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
        ],
      },
    ];
  },
};

export default nextConfig;
