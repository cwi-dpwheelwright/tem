"use client";

import Image from "next/image";
import { useCallback, useMemo, useState, type FormEvent } from "react";
import { resolveRuntimeConfig, type AuthMode } from "@/lib/config";
import { isOidcConfigured, login } from "@/lib/oidc";
import {
  loadOverrides,
  saveAuthToken,
  saveOverrides,
  saveUserLabel,
} from "@/lib/storage";

interface RegisterPanelProps {
  onBackToLogin?: () => void;
  onRegistered?: () => void;
}

interface RegisterStartResponse {
  verification_id: string;
  identifier_type: "email" | "phone";
  masked_target: string;
  expires_utc: string;
  delivery_method: string;
}

interface RegisterConfirmResponse {
  access_token: string;
  refresh_token?: string;
  expires_utc?: string;
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function RegisterPanel({ onBackToLogin, onRegistered }: RegisterPanelProps) {
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const [displayName, setDisplayName] = useState("");
  const [identifier, setIdentifier] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [verificationCode, setVerificationCode] = useState("");
  const [verification, setVerification] = useState<RegisterStartResponse | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const authMode: AuthMode = runtime.config.authMode;
  const oidcConfigured = authMode === "oidc" && isOidcConfigured();
  const configError =
    authMode === "oidc" && !oidcConfigured
      ? "Missing OIDC configuration. Contact your administrator."
      : null;

  const validateRegistrationForm = useCallback((): string | null => {
    if (!displayName.trim()) {
      return "Display name is required.";
    }
    if (!isValidContactIdentifier(identifier.trim())) {
      return "Enter a valid email address or phone number.";
    }
    if (!password) {
      return "Password is required.";
    }
    if (password !== confirmPassword) {
      return "Passwords do not match.";
    }
    return null;
  }, [confirmPassword, displayName, identifier, password]);

  const handleStartRegistration = useCallback(async () => {
    const validationError = validateRegistrationForm();
    if (validationError) {
      setError(validationError);
      return;
    }

    setBusy(true);
    setError(null);
    setNotice(null);

    try {
      const response = await fetch("/api/register/start", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          display_name: displayName.trim(),
          identifier: identifier.trim(),
          username: username.trim() || undefined,
        }),
      });

      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        const message = payload?.message || payload?.error || "Unable to send verification code.";
        throw new Error(message);
      }

      const data = (await response.json()) as RegisterStartResponse;
      setVerification(data);
      setVerificationCode("");
      setNotice(`Verification code sent to ${data.masked_target}.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to send verification code.");
    } finally {
      setBusy(false);
    }
  }, [displayName, identifier, username, validateRegistrationForm]);

  const handleConfirmRegistration = useCallback(async () => {
    const validationError = validateRegistrationForm();
    if (validationError) {
      setError(validationError);
      return;
    }
    if (!verification) {
      setError("Send a verification code first.");
      return;
    }
    if (!verificationCode.trim()) {
      setError("Verification code is required.");
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch("/api/register/confirm", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          verification_id: verification.verification_id,
          code: verificationCode.trim(),
          password,
        }),
      });

      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        const message = payload?.message || payload?.error || "Registration failed.";
        throw new Error(message);
      }

      const data = (await response.json()) as RegisterConfirmResponse;
      if (!data.access_token) {
        throw new Error("Registration failed: token missing.");
      }

      saveAuthToken(data.access_token);
      saveUserLabel(identifier.trim());

      const existingOverrides = loadOverrides();
      const rest = { ...existingOverrides };
      delete rest.userId;
      saveOverrides(rest);

      setPassword("");
      setConfirmPassword("");
      setVerificationCode("");
      onRegistered?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Registration failed.");
    } finally {
      setBusy(false);
    }
  }, [identifier, onRegistered, password, validateRegistrationForm, verification, verificationCode]);

  const handleRegister = useCallback(async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (verification) {
      await handleConfirmRegistration();
      return;
    }

    await handleStartRegistration();
  }, [handleConfirmRegistration, handleStartRegistration, verification]);

  const handleOidcRegister = useCallback(async () => {
    if (!oidcConfigured) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await login("/");
    } catch (err) {
      setBusy(false);
      setError(err instanceof Error ? err.message : "Registration failed.");
    }
  }, [oidcConfigured]);

  const resetVerification = useCallback(() => {
    setVerification(null);
    setVerificationCode("");
    setNotice(null);
    setError(null);
  }, []);

  return (
    <div className="login-shell">
      <div className="login-card">
        <div className="login-brand">
          <Image className="login-logo" src="/cwi-logo.png" alt="CWI" width={263} height={84} priority />
          <div className="login-brand-copy">
            <div className="login-title">Create your CWI account</div>
            <div className="login-subtitle">Verify your email or phone before your account is created.</div>
          </div>
        </div>

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
            <div className="login-label">Identity registration</div>
            <p className="login-hint">
              Registration is handled by the configured CWI Identity provider.
            </p>
            <div className="login-actions">
              <button
                className="login-button"
                type="button"
                onClick={() => void handleOidcRegister()}
                disabled={!oidcConfigured || busy || Boolean(configError)}
              >
                {busy ? "Redirecting..." : "Register with Identity"}
              </button>
              <button className="login-button login-button--secondary" type="button" onClick={() => onBackToLogin?.()}>
                Back to sign in
              </button>
            </div>
          </div>
        ) : (
          <form className="login-section" onSubmit={handleRegister}>
            <label className="login-label" htmlFor="register-display-name">
              Display name
            </label>
            <input
              id="register-display-name"
              className="login-input"
              type="text"
              autoComplete="name"
              placeholder="Christian Wheelwright"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
              disabled={Boolean(verification) || busy}
            />

            <label className="login-label" htmlFor="register-identifier">
              Email or phone
            </label>
            <input
              id="register-identifier"
              className="login-input"
              type="text"
              autoComplete="email tel"
              placeholder="you@example.com or +15551234567"
              value={identifier}
              onChange={(event) => setIdentifier(event.target.value)}
              disabled={Boolean(verification) || busy}
            />

            <label className="login-label" htmlFor="register-username">
              Username
            </label>
            <input
              id="register-username"
              className="login-input"
              type="text"
              autoComplete="username"
              placeholder="Optional"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              disabled={Boolean(verification) || busy}
            />

            <div className="login-form-grid">
              <div className="login-field">
                <label className="login-label" htmlFor="register-password">
                  Password
                </label>
                <input
                  id="register-password"
                  className="login-input"
                  type="password"
                  autoComplete="new-password"
                  placeholder="Create a password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  disabled={busy}
                />
              </div>
              <div className="login-field">
                <label className="login-label" htmlFor="register-confirm-password">
                  Confirm password
                </label>
                <input
                  id="register-confirm-password"
                  className="login-input"
                  type="password"
                  autoComplete="new-password"
                  placeholder="Confirm password"
                  value={confirmPassword}
                  onChange={(event) => setConfirmPassword(event.target.value)}
                  disabled={busy}
                />
              </div>
            </div>

            {verification ? (
              <>
                <label className="login-label" htmlFor="register-verification-code">
                  Verification code
                </label>
                <input
                  id="register-verification-code"
                  className="login-input"
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  placeholder="Enter the code"
                  value={verificationCode}
                  onChange={(event) => setVerificationCode(event.target.value)}
                  disabled={busy}
                />
              </>
            ) : null}

            <p className="login-hint">
              Phone numbers must include a valid country code or a 10-digit US number. Phone registration uses the normalized phone number for sign-in.
            </p>
            <div className="login-actions">
              <button className="login-button" type="submit" disabled={busy || Boolean(configError)}>
                {busy
                  ? verification ? "Verifying..." : "Sending code..."
                  : verification ? "Verify and create account" : "Send verification code"}
              </button>
              {verification ? (
                <button className="login-button login-button--secondary" type="button" onClick={resetVerification} disabled={busy}>
                  Change email or phone
                </button>
              ) : null}
              <button className="login-button login-button--secondary" type="button" onClick={() => onBackToLogin?.()} disabled={busy}>
                Back to sign in
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}

function isValidContactIdentifier(value: string): boolean {
  return EMAIL_PATTERN.test(value) || normalizePhoneNumber(value) !== null;
}

function normalizePhoneNumber(value: string): string | null {
  const trimmed = value.trim();
  const digits = trimmed.replace(/\D/g, "");
  if (trimmed.startsWith("+") && digits.length >= 10 && digits.length <= 15) {
    return `+${digits}`;
  }
  if (digits.length === 10) {
    return `+1${digits}`;
  }
  if (digits.length >= 11 && digits.length <= 15 && digits.startsWith("1")) {
    return `+${digits}`;
  }
  return null;
}
