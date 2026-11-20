"use client";

import { useAuth } from "@/lib/auth/context";

export default function AuthControls() {
  const { user, isLoading, signIn, signOut, roles } = useAuth();

  return (
    <div className="flex flex-wrap items-center gap-3 text-sm font-semibold text-slate-700">
      <span className="hidden md:inline">Approvals</span>
      <span className="hidden md:inline">Trace Vault</span>
      <span className="hidden md:inline">Policy Desk</span>
      {isLoading ? (
        <span className="rounded-full bg-white/70 px-4 py-2 text-xs text-slate-500">
          Checking session...
        </span>
      ) : user ? (
        <div className="flex items-center gap-3">
          <span className="hidden text-xs text-slate-500 md:inline">
            {user.profile.email ?? user.profile.preferred_username ?? "Signed in"}
          </span>
          <span className="hidden rounded-full border border-slate-200 bg-white px-3 py-1 text-[10px] uppercase tracking-[0.2em] text-slate-500 md:inline">
            {roles.join(", ")}
          </span>
          <button
            className="rounded-full bg-black px-5 py-2 text-white"
            onClick={() => void signOut()}
          >
            Sign out
          </button>
        </div>
      ) : (
        <button
          className="rounded-full bg-black px-5 py-2 text-white"
          onClick={() => void signIn()}
        >
          Sign in
        </button>
      )}
    </div>
  );
}
