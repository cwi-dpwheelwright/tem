"use client";

import { useEffect } from "react";
import AdminGate from "@/components/admin-gate";
import TenantsPanel from "@/components/identity/tenants";
import UsersPanel from "@/components/identity/users";
import RolesPanel from "@/components/identity/roles";
import { IdentityTenantProvider, useIdentityTenant } from "@/lib/identity/context";

function IdentityAdminContent() {
  const { tenantId, setTenantId, clearTenant } = useIdentityTenant();

  useEffect(() => {
    if (!tenantId) {
      return;
    }

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        clearTenant();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [tenantId, clearTenant]);

  return (
    <main className="min-h-screen">
      <div className="mx-auto flex max-w-6xl flex-col gap-10 px-6 py-12">
        <header className="space-y-4">
          <p className="inline-flex w-fit items-center gap-2 rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.3em] text-slate-200">
            CWI Identity Administration
          </p>
          <h1 className="text-4xl font-semibold leading-tight text-white sm:text-5xl">
            Manage tenants, users, and roles.
          </h1>
          <p className="max-w-2xl text-base leading-7 text-slate-300">
            Use the identity API to provision tenants, invite users, and assign roles. All changes are
            audited by the identity service and restricted to the configured superadmin account.
          </p>
        </header>

        <AdminGate>
          <div className="grid gap-6">
            <TenantsPanel selectedTenantId={tenantId} onSelectTenant={setTenantId} />

            {!tenantId && (
              <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
                <h2 className="text-lg font-semibold text-white">Select a tenant to continue</h2>
                <p className="mt-2 max-w-2xl text-sm leading-7 text-slate-300">
                  User invites, password resets, and role management stay hidden until a tenant is chosen.
                  Pick a tenant above, then this page will open the management tools for that workspace.
                </p>
              </section>
            )}
          </div>

          {tenantId && (
            <div
              className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/80 p-4 backdrop-blur-md"
              role="presentation"
              onMouseDown={(event) => {
                if (event.target === event.currentTarget) {
                  clearTenant();
                }
              }}
            >
              <section
                className="flex max-h-[92vh] w-full max-w-7xl flex-col overflow-hidden rounded-3xl border border-white/10 bg-slate-950 shadow-2xl shadow-black/50"
                role="dialog"
                aria-modal="true"
                aria-labelledby="tenant-management-title"
              >
                <header className="flex flex-wrap items-start justify-between gap-4 border-b border-white/10 bg-cyan-500/10 p-6">
                  <div>
                    <p className="text-xs uppercase tracking-[0.3em] text-cyan-100">Tenant management</p>
                    <h2 id="tenant-management-title" className="mt-2 text-2xl font-semibold text-white">
                      {tenantId}
                    </h2>
                    <p className="mt-2 max-w-3xl text-sm leading-7 text-slate-200">
                      Manage users, passwords, tenant movement, removals, and role configuration for this workspace.
                    </p>
                  </div>
                  <button
                    className="rounded-full border border-white/15 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.2em] text-white"
                    type="button"
                    onClick={clearTenant}
                  >
                    Close
                  </button>
                </header>

                <div className="grid gap-6 overflow-y-auto p-6">
                  <UsersPanel tenantId={tenantId} />
                  <RolesPanel tenantId={tenantId} />
                </div>
              </section>
            </div>
          )}
        </AdminGate>
      </div>
    </main>
  );
}

export default function IdentityAdminPage() {
  return (
    <IdentityTenantProvider>
      <IdentityAdminContent />
    </IdentityTenantProvider>
  );
}
