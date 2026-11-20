"use client";

import { useCallback, useEffect, useState } from "react";
import { apiGet, apiPatch, apiPost } from "@/lib/identity/client";
import { useAuth } from "@/lib/auth/context";

interface Tenant {
  tenant_id: string;
  name: string;
  status: string;
}

interface TenantsPanelProps {
  selectedTenantId?: string;
  onSelectTenant?: (tenantId: string) => void;
}

export default function TenantsPanel({
  selectedTenantId = "",
  onSelectTenant,
}: TenantsPanelProps) {
  const { accessToken } = useAuth();
  const [tenants, setTenants] = useState<Tenant[]>([]);
  const [tenantId, setTenantId] = useState("");
  const [name, setName] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to load tenants.");
      return;
    }
    setLoading(true);
    const response = await apiGet<Tenant[]>("/admin/tenants", accessToken);
    if (response.ok && response.data) {
      setTenants(response.data);
      setMessage(null);
    } else {
      setMessage(response.error || "Failed to load tenants.");
    }
    setLoading(false);
  }, [accessToken]);

  useEffect(() => {
    void load();
  }, [load]);

  const createTenant = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!accessToken) {
      setMessage("Sign in with an admin account to create tenants.");
      return;
    }
    const response = await apiPost<Tenant>(
      "/admin/tenants",
      { tenant_id: tenantId.trim(), name: name.trim() },
      accessToken,
    );
    if (response.ok) {
      setTenantId("");
      setName("");
      await load();
    } else {
      setMessage(response.error || "Failed to create tenant.");
    }
  };

  const disableTenant = async (tenant: Tenant) => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to disable tenants.");
      return;
    }
    const response = await apiPost<Tenant>(`/admin/tenants/${tenant.tenant_id}/disable`, undefined, accessToken);
    if (response.ok) {
      if (selectedTenantId === tenant.tenant_id) {
        onSelectTenant?.("");
      }
      await load();
    } else {
      setMessage(response.error || "Failed to disable tenant.");
    }
  };

  const updateTenant = async (tenant: Tenant, updates: Partial<Tenant>) => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to update tenants.");
      return;
    }
    const response = await apiPatch<Tenant>(`/admin/tenants/${tenant.tenant_id}`, updates, accessToken);
    if (response.ok) {
      await load();
    } else {
      setMessage(response.error || "Failed to update tenant.");
    }
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Tenants</h2>
          <p className="text-sm text-slate-300">
            Start here. Pick a tenant to open user and role management for that workspace.
          </p>
        </div>
        <button
          className="rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.2em] text-slate-200"
          type="button"
          onClick={() => void load()}
        >
          Refresh
        </button>
      </header>

      <form className="mt-6 grid gap-4 md:grid-cols-[1fr_1fr_auto]" onSubmit={createTenant}>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Tenant ID
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={tenantId}
            onChange={(e) => setTenantId(e.target.value)}
            placeholder="tenant-1"
            required
          />
        </label>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Tenant name
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="CWI Ops"
            required
          />
        </label>
        <button
          className="h-fit rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
          type="submit"
          disabled={!tenantId.trim() || !name.trim()}
        >
          Create
        </button>
      </form>

      {message && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          {message}
        </div>
      )}

      <div className="mt-6 space-y-3">
        {loading ? (
          <div className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
            Loading tenants…
          </div>
        ) : tenants.length === 0 ? (
          <div className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
            No tenants yet.
          </div>
        ) : (
          tenants.map((tenant) => (
            <div
              key={tenant.tenant_id}
              className={`rounded-2xl border p-4 text-sm text-slate-200 transition ${
                selectedTenantId === tenant.tenant_id
                  ? "border-cyan-400/60 bg-cyan-500/10 shadow-lg shadow-cyan-950/30"
                  : "border-white/10 bg-white/5"
              }`}
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="space-y-1">
                  <div className="text-base font-semibold text-white">{tenant.name}</div>
                  <div className="text-xs uppercase tracking-[0.2em] text-slate-400">{tenant.tenant_id}</div>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <span
                    className={`rounded-full border px-3 py-1 text-[11px] uppercase tracking-[0.2em] ${
                      tenant.status === "active"
                        ? "border-emerald-400/40 bg-emerald-500/10 text-emerald-100"
                        : "border-amber-400/40 bg-amber-500/10 text-amber-100"
                    }`}
                  >
                    {tenant.status}
                  </span>
                  <button
                    className={`rounded-full px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] ${
                      selectedTenantId === tenant.tenant_id
                        ? "border border-cyan-300/50 bg-cyan-400/20 text-cyan-50"
                        : "bg-sky-600/90 text-white"
                    }`}
                    type="button"
                    onClick={() => onSelectTenant?.(tenant.tenant_id)}
                    disabled={tenant.status !== "active"}
                  >
                    {selectedTenantId === tenant.tenant_id ? "Managing" : "Manage tenant"}
                  </button>
                  <button
                    className="rounded-full border border-red-500/40 bg-red-500/10 px-3 py-2 text-xs uppercase tracking-[0.2em] text-red-100"
                    type="button"
                    onClick={() => void disableTenant(tenant)}
                    disabled={tenant.status === "disabled"}
                  >
                    Disable
                  </button>
                </div>
              </div>

              <div className="mt-4 grid gap-4 md:grid-cols-[1.2fr_0.8fr]">
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Tenant name
                  <input
                    className="mt-2 w-full rounded-lg border border-white/10 bg-slate-950/40 px-3 py-2 text-sm text-white"
                    defaultValue={tenant.name}
                    onBlur={(event) => {
                      const next = event.target.value.trim();
                      if (next && next !== tenant.name) {
                        void updateTenant(tenant, { name: next });
                      }
                    }}
                  />
                </label>
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Status
                  <select
                    className="mt-2 w-full rounded-lg border border-white/10 bg-slate-950/40 px-3 py-2 text-sm text-white"
                    defaultValue={tenant.status}
                    onChange={(event) => void updateTenant(tenant, { status: event.target.value })}
                  >
                    <option value="active">active</option>
                    <option value="disabled">disabled</option>
                  </select>
                </label>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}
