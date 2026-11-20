"use client";

import { useCallback, useEffect, useState } from "react";
import { apiGet, apiPost } from "@/lib/identity/client";
import { useAuth } from "@/lib/auth/context";

type Role = {
  role_id: string;
  name: string;
};

export default function RolesPanel({ tenantId }: { tenantId: string }) {
  const { accessToken } = useAuth();
  const [roles, setRoles] = useState<Role[]>([]);
  const [roleName, setRoleName] = useState("");
  const [message, setMessage] = useState<string | null>(null);

  const loadRoles = useCallback(
    async (targetTenant: string) => {
      if (!targetTenant || !accessToken) {
        return;
      }
      const response = await apiGet<Role[]>(
        `/admin/roles?tenant_id=${encodeURIComponent(targetTenant)}`,
        accessToken,
      );
      if (response.ok && response.data) {
        setRoles(response.data);
        setMessage(null);
      } else {
        setMessage(response.error || "Failed to load roles.");
      }
    },
    [accessToken],
  );

  useEffect(() => {
    if (tenantId) {
      void loadRoles(tenantId);
    }
  }, [tenantId, loadRoles]);

  const createRole = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!accessToken) {
      setMessage("Sign in with an admin account to create roles.");
      return;
    }
    if (!tenantId) {
      setMessage("Select a tenant before creating roles.");
      return;
    }
    const response = await apiPost<Role>(
      "/admin/roles",
      { tenant_id: tenantId, name: roleName.trim() },
      accessToken,
    );
    if (response.ok) {
      setRoleName("");
      await loadRoles(tenantId);
    } else {
      setMessage(response.error || "Failed to create role.");
    }
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Roles</h2>
          <p className="text-sm text-slate-300">Create tenant roles and review role lists.</p>
        </div>
        <button
          className="rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.2em] text-slate-200"
          type="button"
          onClick={() => void loadRoles(tenantId)}
        >
          Refresh
        </button>
      </header>

      {!tenantId && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          Enter a tenant ID above to manage roles.
        </div>
      )}

      <form className="mt-6 grid gap-4 md:grid-cols-[1fr_auto]" onSubmit={createRole}>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Role name
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={roleName}
            onChange={(e) => setRoleName(e.target.value)}
            placeholder="admin"
            required
            disabled={!tenantId}
          />
        </label>
        <button
          className="h-fit rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
          type="submit"
          disabled={!tenantId}
        >
          Add role
        </button>
      </form>

      {message && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          {message}
        </div>
      )}

      <div className="mt-6 space-y-2">
        <div className="grid grid-cols-[1fr_auto] gap-3 text-xs uppercase tracking-[0.2em] text-slate-400">
          <div>Role</div>
          <div>ID</div>
        </div>
        {roles.length === 0 ? (
          <div className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
            No roles loaded.
          </div>
        ) : (
          roles.map((role) => (
            <div
              key={role.role_id}
              className="grid grid-cols-[1fr_auto] gap-3 rounded-2xl border border-white/10 bg-white/5 p-3 text-sm text-slate-200"
            >
              <div className="font-semibold text-white">{role.name}</div>
              <div className="text-xs text-slate-400">{role.role_id}</div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}
