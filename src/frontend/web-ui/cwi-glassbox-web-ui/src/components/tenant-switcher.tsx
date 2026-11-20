"use client";

import { useTenant } from "@/lib/tenant/context";

export default function TenantSwitcher() {
  const { tenantId, setTenantId } = useTenant();

  return (
    <label className="flex items-center gap-2 rounded-full border border-white/70 bg-white/70 px-3 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
      <span className="hidden sm:inline">Tenant</span>
      <input
        value={tenantId}
        onChange={(event) => setTenantId(event.target.value)}
        className="w-28 bg-transparent text-xs font-semibold uppercase tracking-[0.2em] text-slate-700 focus:outline-none"
        aria-label="Tenant ID"
      />
    </label>
  );
}
