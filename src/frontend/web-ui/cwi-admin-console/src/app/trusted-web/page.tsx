import AdminGate from "@/components/admin-gate";
import TrustedWebConfigPanel from "@/components/trusted-web/trusted-web-config-panel";

export default function TrustedWebPage() {
  return (
    <main className="min-h-screen">
      <div className="mx-auto flex max-w-6xl flex-col gap-10 px-6 py-12">
        <header className="space-y-4">
          <p className="inline-flex w-fit items-center gap-2 rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.3em] text-slate-200">
            Trusted Web
          </p>
          <h1 className="text-4xl font-semibold leading-tight text-white sm:text-5xl">
            Configure trusted sites.
          </h1>
          <p className="max-w-2xl text-base leading-7 text-slate-300">
            Manage the global database-backed site allowlist used by the GPT web include toggle.
          </p>
        </header>

        <AdminGate>
          <TrustedWebConfigPanel />
        </AdminGate>
      </div>
    </main>
  );
}
