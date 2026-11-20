"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import AuthControls from "@/components/auth-controls";
import BreakGlassBanner from "@/components/break-glass-banner";
import TenantSwitcher from "@/components/tenant-switcher";

const NAV_LINKS = [
  { href: "/", label: "Overview" },
  { href: "/reports", label: "Reports" },
  { href: "/documents", label: "Documents" },
  { href: "/artifacts", label: "Artifacts" },
  { href: "/analytics", label: "Analytics" },
  { href: "/ownership", label: "Ownership" },
  { href: "/mesh", label: "Mesh" },
];

type AppShellProps = {
  children: ReactNode;
  title?: string;
  subtitle?: string;
  badge?: string;
};

export default function AppShell({ children, title, subtitle, badge }: AppShellProps) {
  const pathname = usePathname();

  return (
    <div className="relative min-h-screen overflow-hidden">
      <BreakGlassBanner />
      <div className="pointer-events-none absolute -top-32 left-10 h-80 w-80 rounded-full bg-sky-200/70 blur-3xl" />
      <div className="pointer-events-none absolute right-0 top-20 h-72 w-72 rounded-full bg-sky-100/60 blur-3xl" />
      <div className="pointer-events-none absolute bottom-0 left-1/3 h-64 w-64 rounded-full bg-sky-300/50 blur-3xl" />

      <header className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-6 pt-8 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex flex-wrap items-center gap-4">
          <div className="flex items-center gap-3">
            <div className="grid h-10 w-10 place-items-center rounded-xl bg-slate-950 text-sm tracking-[0.2em] text-white">
              CWI
            </div>
            <div>
              <p className="text-xs uppercase tracking-[0.3em] text-slate-500">Glassbox Web UI</p>
              <p className="text-lg font-semibold text-slate-900">Governed Ops</p>
            </div>
          </div>
          <nav className="flex flex-wrap items-center gap-2">
            {NAV_LINKS.map((link) => {
              const isActive = link.href === "/" ? pathname === "/" : pathname.startsWith(link.href);
              return (
                <Link
                  key={link.href}
                  href={link.href}
                  className={`rounded-full px-4 py-2 text-xs font-semibold uppercase tracking-[0.18em] transition ${
                    isActive
                      ? "bg-slate-900 text-white shadow-lg shadow-slate-900/20"
                      : "border border-white/80 bg-white/70 text-slate-600 hover:border-slate-200"
                  }`}
                >
                  {link.label}
                </Link>
              );
            })}
          </nav>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <TenantSwitcher />
          <AuthControls />
        </div>
      </header>

      {(title || subtitle) && (
        <section className="mx-auto mt-10 w-full max-w-6xl px-6">
          <div className="flex flex-wrap items-center gap-3">
            {badge ? (
              <span className="rounded-full border border-amber-200 bg-amber-50 px-4 py-1 text-xs font-semibold uppercase tracking-[0.3em] text-amber-700">
                {badge}
              </span>
            ) : null}
            {title ? (
              <h1 className="text-3xl font-semibold text-slate-950 sm:text-4xl">{title}</h1>
            ) : null}
          </div>
          {subtitle ? <p className="mt-3 max-w-2xl text-sm text-slate-600">{subtitle}</p> : null}
        </section>
      )}

      <main className="mx-auto w-full max-w-6xl px-6 pb-20 pt-12">{children}</main>
    </div>
  );
}

