"use client";

import AuthControls from "@/components/auth-controls";
import IdentityFallbackLogin from "@/components/auth/identity-fallback-login";
import { useAuth } from "@/lib/auth/context";

export default function AdminGate({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading, canAccessAdministration } = useAuth();

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="space-y-1">
          <p className="text-xs uppercase tracking-[0.25em] text-slate-400">Administration</p>
          <p className="text-sm text-slate-300">
            Platform administrators can access pack, identity, and node controls from their normal tenant workspace.
          </p>
        </div>
        <AuthControls />
      </div>

      {isLoading ? (
        <div className="rounded-2xl border border-white/10 bg-white/5 p-6 text-sm text-slate-300">
          Verifying your administration session...
        </div>
      ) : !isAuthenticated ? (
        <div className="space-y-4">
          <div className="rounded-2xl border border-white/10 bg-white/5 p-6 text-sm text-slate-300">
            Sign in with a platform-admin account to continue.
          </div>
          <IdentityFallbackLogin />
        </div>
      ) : !canAccessAdministration ? (
        <div className="rounded-2xl border border-red-500/40 bg-red-500/10 p-6 text-sm text-red-100">
          Your account is signed in, but it does not include the platform-admin entitlement.
        </div>
      ) : (
        children
      )}
    </section>
  );
}
