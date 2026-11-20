import HealthPill from "@/components/health-pill";
import HealthServices from "@/components/health-services";
import TraceLookup from "@/components/trace-lookup";
import ApprovalsInbox from "@/components/approvals-inbox";
import KillSwitchStatus from "@/components/kill-switch-status";
import RollbackPanel from "@/components/rollback-panel";
import BreakGlassPanel from "@/components/break-glass-panel";
import BreakGlassAudit from "@/components/break-glass-audit";
import LedgerViewer from "@/components/ledger-viewer";
import HealthSlo from "@/components/health-slo";
import PackWorkspace from "@/components/pack-workspace";
import GovernanceInsights from "@/components/governance-insights";
import OutcomeFeedbackPanel from "@/components/outcome-feedback";
import AppShell from "@/components/app-shell";

export default function Home() {
  return (
    <AppShell>
      <div className="grid w-full grid-cols-1 gap-10 lg:grid-cols-[1.1fr_0.9fr]">
        <section className="space-y-8">
          <div className="inline-flex items-center gap-3 rounded-full border border-black/10 bg-white/70 px-4 py-2 text-xs font-semibold uppercase tracking-[0.25em] text-slate-600">
            Live Governance
            <span className="h-2 w-2 rounded-full bg-emerald-500" />
          </div>
          <div className="space-y-6">
            <h1 className="text-4xl font-semibold leading-tight text-slate-950 sm:text-5xl lg:text-6xl">
              Orchestrate autonomy with{" "}
              <span className="font-[var(--font-display)] text-5xl italic text-amber-700 sm:text-6xl lg:text-7xl">
                human-grade control
              </span>
              .
            </h1>
            <p className="max-w-xl text-lg leading-8 text-slate-600">
              The CWI node console keeps every ask, action, and approval governed. Monitor
              policy posture, audit trails, and real-time dependency health without losing
              speed.
            </p>
          </div>
          <div className="flex flex-wrap gap-4">
            <button className="rounded-full bg-slate-900 px-6 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/20">
              Open live console
            </button>
            <button className="rounded-full border border-slate-300 bg-white/70 px-6 py-3 text-sm font-semibold text-slate-700">
              View operator runbook
            </button>
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            {[
              { label: "Pending approvals", value: "14" },
              { label: "Active traces", value: "236" },
              { label: "Policy drift", value: "0.4%" },
            ].map((item) => (
              <div
                key={item.label}
                className="rounded-2xl border border-white/80 bg-white/80 p-4 shadow-sm"
              >
                <p className="text-xs uppercase tracking-[0.22em] text-slate-500">
                  {item.label}
                </p>
                <p className="mt-3 text-2xl font-semibold text-slate-900">{item.value}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-xl shadow-slate-200/60">
            <div className="flex items-center justify-between">
              <p className="text-sm font-semibold text-slate-800">Live stack health</p>
              <HealthPill />
            </div>
            <HealthServices />
            <div className="mt-6 rounded-2xl bg-slate-900 px-5 py-4 text-white">
              <p className="text-xs uppercase tracking-[0.2em] text-slate-300">Next review</p>
              <p className="mt-2 text-lg font-semibold">Policy gate audit in 2h 14m</p>
            </div>
          </div>

          <HealthSlo />

          <div className="grid gap-4 sm:grid-cols-2">
            {[
              { title: "Approval queue", value: "7 pending", note: "3 awaiting two-person sign-off" },
            ].map((item) => (
              <div
                key={item.title}
                className="rounded-2xl border border-white/70 bg-white/80 p-5"
              >
                <p className="text-xs uppercase tracking-[0.2em] text-slate-500">{item.title}</p>
                <p className="mt-3 text-xl font-semibold text-slate-900">{item.value}</p>
                <p className="mt-2 text-sm text-slate-600">{item.note}</p>
              </div>
            ))}
            <KillSwitchStatus />
          </div>
          <BreakGlassPanel />
          <BreakGlassAudit />
          <TraceLookup />
          <GovernanceInsights />
          <OutcomeFeedbackPanel />
          <LedgerViewer />
          <ApprovalsInbox />
          <RollbackPanel />
          <PackWorkspace />
        </section>
      </div>
    </AppShell>
  );
}
