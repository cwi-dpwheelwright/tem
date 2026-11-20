"use client";

import { useAuth } from "@/lib/auth/context";

export default function AuthControls() {
  const { user, identityProfile, isLoading, isSuperAdmin, canAccessAdministration, signIn, signOut, roles, canUseOidc, authSource } = useAuth();

  const label =
    user?.profile.email ??
    user?.profile.preferred_username ??
    identityProfile?.email ??
    identityProfile?.username ??
    identityProfile?.name ??
    (authSource !== "none" ? "Signed in" : "");

  return (
    <div className="flex flex-wrap items-center gap-3 text-sm font-semibold text-slate-200">
      {isLoading ? (
        <span className="rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs text-slate-300">
          Checking session…
        </span>
      ) : authSource !== "none" ? (
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs text-slate-300">{label}</span>
          <span className="rounded-full border border-white/10 bg-white/10 px-3 py-1 text-[10px] uppercase tracking-[0.2em] text-slate-300">
            {isSuperAdmin ? "superadmin" : canAccessAdministration ? "administrator" : roles.join(", ")}
          </span>
          <button
            className="rounded-full border border-white/20 bg-white/10 px-5 py-2 text-white"
            onClick={() => void signOut()}
          >
            Sign out
          </button>
        </div>
      ) : canUseOidc ? (
        <button className="rounded-full border border-white/20 bg-white/10 px-5 py-2 text-white" onClick={() => void signIn()}>
          Sign in with OIDC
        </button>
      ) : (
        <span className="rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs text-slate-300">
          OIDC not configured
        </span>
      )}
    </div>
  );
}
