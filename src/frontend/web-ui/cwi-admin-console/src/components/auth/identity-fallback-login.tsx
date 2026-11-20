"use client";

import { useState } from "react";
import { useAuth } from "@/lib/auth/context";
import { resolveIdentityBaseUrl } from "@/lib/identity/base-url";

export default function IdentityFallbackLogin() {
  const { signInWithIdentity, canUseOidc } = useAuth();
  const [showForm, setShowForm] = useState(false);
  const [tenantId, setTenantId] = useState("");
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const identityBaseUrl = resolveIdentityBaseUrl();

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setLoading(true);
    setMessage(null);
    const result = await signInWithIdentity({
      tenantId: tenantId.trim() || undefined,
      identifier,
      password,
    });
    if (!result.ok) {
      setMessage(result.error || "Login failed.");
    } else {
      setTenantId("");
      setIdentifier("");
      setPassword("");
      setShowForm(false);
    }
    setLoading(false);
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h3 className="text-base font-semibold text-white">Identity login fallback</h3>
          <p className="text-sm text-slate-300">
            Use this only if OIDC is unavailable. Tokens are stored in session storage and cleared
            on sign out. Administration access requires a platform-admin entitlement.
          </p>
        </div>
        <button
          className="rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.2em] text-slate-200"
          type="button"
          onClick={() => setShowForm((prev) => !prev)}
        >
          {showForm ? "Hide" : "Open"}
        </button>
      </header>

      {!identityBaseUrl && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          Identity base URL is not configured. Set `NEXT_PUBLIC_IDENTITY_BASE_URL` to enable fallback
          login.
        </div>
      )}

      {canUseOidc && (
        <div className="mt-4 rounded-2xl border border-sky-500/40 bg-sky-500/10 p-3 text-sm text-sky-100">
          OIDC is configured and preferred. Use fallback only for bootstrap or emergency access.
        </div>
      )}

      {showForm && identityBaseUrl && (
        <form className="mt-6 grid gap-4 md:grid-cols-[1fr_1fr_auto]" onSubmit={handleSubmit}>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Tenant ID (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={tenantId}
              onChange={(event) => setTenantId(event.target.value)}
              placeholder="tenant-1"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Admin username or email
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={identifier}
              onChange={(event) => setIdentifier(event.target.value)}
              placeholder="admin@tenant.local"
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Password
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              type="password"
              required
            />
          </label>
          <div className="md:col-span-3 flex flex-wrap items-center gap-3">
            <button
              className="rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
              type="submit"
              disabled={loading}
            >
              {loading ? "Signing in..." : "Sign in with identity"}
            </button>
            <span className="text-xs text-slate-400">
              Credentials are sent directly to the identity API.
            </span>
          </div>
        </form>
      )}

      {message && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          {message}
        </div>
      )}
    </section>
  );
}
