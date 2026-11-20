import Link from "next/link";
import ConfigDiagnostics from "@/components/config-diagnostics";
import HealthStatus from "@/components/health-status";
import AdminGate from "@/components/admin-gate";

export default function Home() {
  return (
    <main className="min-h-screen">
      <div className="mx-auto flex max-w-5xl flex-col gap-10 px-6 py-12">
        <header className="space-y-4">
          <p className="inline-flex w-fit items-center gap-2 rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.3em] text-slate-200">
            CWI Identity Administration
          </p>
          <h1 className="text-4xl font-semibold leading-tight text-white sm:text-5xl">
            Govern packs and identity with confidence.
          </h1>
          <p className="max-w-2xl text-base leading-7 text-slate-300">
            This console is reserved for the configured superadmin account. Configure the
            runtime endpoints and sign-in settings below to enable pack lifecycle and
            identity governance modules.
          </p>
        </header>

        <AdminGate>
          <div className="grid gap-6 md:grid-cols-2">
            <ConfigDiagnostics />
            <HealthStatus />
          </div>

          <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 text-sm text-slate-200 shadow-xl shadow-black/30">
            <h2 className="text-lg font-semibold text-white">Next steps</h2>
            <ul className="mt-3 space-y-2 text-slate-300">
              <li>Provide the admin API, identity, and packs base URLs in env.</li>
              <li>Register the admin console OIDC client and set redirect URIs.</li>
              <li>Validate that /api/health reports ready before onboarding admins.</li>
            </ul>
          </section>

          <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 text-sm text-slate-200 shadow-xl shadow-black/30">
            <h2 className="text-lg font-semibold text-white">Identity admin</h2>
            <p className="mt-2 text-sm text-slate-300">
              Manage tenants, users, and roles in the identity service.
            </p>
            <Link
              href="/identity"
              className="mt-4 inline-flex rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
            >
              Open identity admin
            </Link>
          </section>

          <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 text-sm text-slate-200 shadow-xl shadow-black/30">
            <h2 className="text-lg font-semibold text-white">Pack catalog</h2>
            <p className="mt-2 text-sm text-slate-300">
              Browse published packs and review metadata before activation.
            </p>
            <Link
              href="/packs"
              className="mt-4 inline-flex rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
            >
              Open pack catalog
            </Link>
          </section>

          <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 text-sm text-slate-200 shadow-xl shadow-black/30">
            <h2 className="text-lg font-semibold text-white">Trusted web</h2>
            <p className="mt-2 text-sm text-slate-300">
              Configure the database-backed trusted sites used by the GPT web include toggle.
            </p>
            <Link
              href="/trusted-web"
              className="mt-4 inline-flex rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
            >
              Open trusted web
            </Link>
          </section>
        </AdminGate>
      </div>
    </main>
  );
}
