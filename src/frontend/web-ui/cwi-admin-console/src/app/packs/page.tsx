import AdminGate from "@/components/admin-gate";
import PackCatalog from "@/components/packs/pack-catalog";
import PackUpload from "@/components/packs/pack-upload";
import PackActivationPanel from "@/components/packs/pack-activation";
import PackRetireDeletePanel from "@/components/packs/pack-retire-delete";
import PackAuditLog from "@/components/packs/pack-audit-log";

export default function PacksPage() {
  return (
    <main className="min-h-screen">
      <div className="mx-auto flex max-w-6xl flex-col gap-10 px-6 py-12">
        <header className="space-y-4">
          <p className="inline-flex w-fit items-center gap-2 rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.3em] text-slate-200">
            Pack Catalog
          </p>
          <h1 className="text-4xl font-semibold leading-tight text-white sm:text-5xl">
            Inspect published packs.
          </h1>
          <p className="max-w-2xl text-base leading-7 text-slate-300">
            Review pack metadata, versions, and anchors before activation.
          </p>
        </header>

        <AdminGate>
          <div className="grid gap-6">
            <PackUpload />
            <PackActivationPanel />
            <PackRetireDeletePanel />
            <PackCatalog />
            <PackAuditLog />
          </div>
        </AdminGate>
      </div>
    </main>
  );
}
