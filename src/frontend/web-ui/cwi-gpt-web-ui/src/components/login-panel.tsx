"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useCallback, useMemo, useState, type FormEvent } from "react";
import { resolveRuntimeConfig, type AuthMode } from "@/lib/config";
import {
  consumeAuthNotice,
  loadOverrides,
  saveAuthToken,
  saveUserLabel,
  saveOverrides,
} from "@/lib/storage";
import { isOidcConfigured, login } from "@/lib/oidc";

interface LoginPanelProps {
  onAuthenticated?: () => void;
}

interface LoginResponse {
  access_token: string;
  refresh_token?: string;
  expires_utc?: string;
}

export function LoginPanel({ onAuthenticated }: LoginPanelProps) {
  const router = useRouter();
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const [notice] = useState(() => consumeAuthNotice());
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const authMode: AuthMode = runtime.config.authMode;
  const oidcConfigured = authMode === "oidc" && isOidcConfigured();
  const configError =
    authMode === "oidc" && !oidcConfigured
      ? "Missing OIDC configuration. Contact your administrator."
      : null;

  const handlePasswordLogin = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      const trimmedIdentifier = identifier.trim();
      if (!trimmedIdentifier) {
        setError("Username, email, or phone is required.");
        return;
      }
      if (!password) {
        setError("Password is required.");
        return;
      }

      setBusy(true);
      setError(null);

      try {
        const isEmail = trimmedIdentifier.includes("@");
        const response = await fetch("/api/login", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            username: isEmail ? undefined : trimmedIdentifier,
            email: isEmail ? trimmedIdentifier : undefined,
            password,
          }),
        });

        if (!response.ok) {
          const payload = await response.json().catch(() => null);
          const message = payload?.message || payload?.error || "Login failed.";
          throw new Error(message);
        }

        const data = (await response.json()) as LoginResponse;
        if (!data.access_token) {
          throw new Error("Login failed: token missing.");
        }

        saveAuthToken(data.access_token);
        saveUserLabel(trimmedIdentifier);

        const existingOverrides = loadOverrides();
        const rest = { ...existingOverrides };
        delete rest.userId;
        saveOverrides(rest);

        setPassword("");
        onAuthenticated?.();
      } catch (err) {
        setError(err instanceof Error ? err.message : "Login failed.");
      } finally {
        setBusy(false);
      }
    },
    [identifier, onAuthenticated, password],
  );

  const handleOidcLogin = useCallback(async () => {
    if (!oidcConfigured) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await login("/");
    } catch (err) {
      setBusy(false);
      setError(err instanceof Error ? err.message : "Sign-in failed.");
    }
  }, [oidcConfigured]);

  return (
    <div className="login-shell">
      <div className="login-card">
        <div className="login-brand">
          <Image className="login-logo" src="/cwi-logo.png" alt="CWI" width={263} height={84} priority />
          <div className="login-brand-copy">
            <div className="login-title">Welcome to CWI</div>
            <div className="login-subtitle">Sign in to continue.</div>
          </div>
        </div>

        <>
        {notice ? (
          <div className="login-alert login-alert--notice" role="status" aria-live="polite">
            {notice}
          </div>
        ) : null}

        {error ? (
          <div className="login-alert login-alert--error" role="alert">
            {error}
          </div>
        ) : null}

        {configError ? (
          <div className="login-alert login-alert--error" role="alert">
            {configError}
          </div>
        ) : null}

        {authMode === "oidc" ? (
          <div className="login-section">
            <div className="login-label">Identity sign-in</div>
            <p className="login-hint">
              Authenticate with the CWI Identity service to access the gateway.
            </p>
            <div className="login-actions">
              <button
                className="login-button"
                type="button"
                onClick={() => void handleOidcLogin()}
                disabled={!oidcConfigured || busy || Boolean(configError)}
              >
                {busy ? "Redirecting..." : "Sign in with Identity"}
              </button>
              <button
                className="login-button login-button--secondary"
                type="button"
                onClick={() => router.push("/register")}
                disabled={busy}
              >
                Register Now
              </button>
            </div>
          </div>
        ) : (
          <form className="login-section" onSubmit={handlePasswordLogin}>
            <label className="login-label" htmlFor="login-identifier">
              Username, email, or phone
            </label>
            <input
              id="login-identifier"
              className="login-input"
              type="text"
              autoComplete="username"
              placeholder="you@example.com or +15551234567"
              value={identifier}
              onChange={(event) => setIdentifier(event.target.value)}
            />
            <label className="login-label" htmlFor="login-password">
              Password
            </label>
            <input
              id="login-password"
              className="login-input"
              type="password"
              autoComplete="current-password"
              placeholder="Enter your password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <p className="login-hint">Your credentials are never stored locally.</p>
            <div className="login-actions">
              <button className="login-button" type="submit" disabled={busy || Boolean(configError)}>
                {busy ? "Signing in..." : "Sign in"}
              </button>
              <button
                className="login-button login-button--secondary"
                type="button"
                onClick={() => router.push("/register")}
                disabled={busy}
              >
                Register Now
              </button>
            </div>
          </form>
        )}

        </>
      </div>
    </div>
  );
}
