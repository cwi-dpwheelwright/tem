"use client";

import { useEffect, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import type { HealthResponse } from "@/lib/api/types";

interface ServiceStatus {
  name: string;
  status: string;
}

function normalizeStatus(value: string) {
  const lowered = value.toLowerCase();
  if (lowered === "ok") {
    return { label: "ok", accent: "bg-emerald-500" };
  }
  if (lowered === "degraded") {
    return { label: "degraded", accent: "bg-amber-400" };
  }
  return { label: "down", accent: "bg-rose-500" };
}

export default function HealthServices() {
  const status = getRuntimeConfigStatus();
  const hasConfig = Boolean(status.config);
  const [services, setServices] = useState<ServiceStatus[]>([]);
  const [isLoading, setIsLoading] = useState(hasConfig);

  useEffect(() => {
    if (!status.config) {
      return;
    }

    const client = createApiClient({ baseUrl: status.config.apiBaseUrl });
    client
      .health()
      .then((payload: HealthResponse) => {
        const map = payload.services ?? {};
        const entries = Object.entries(map).map(([name, value]) => ({
          name,
          status: value,
        }));
        setServices(entries);
      })
      .catch(() => setServices([]))
      .finally(() => setIsLoading(false));
  }, [status.config]);

  if (isLoading) {
    return (
      <div className="mt-6 space-y-4">
        {Array.from({ length: 3 }).map((_, index) => (
          <div key={index} className="h-5 rounded-full bg-slate-100" />
        ))}
      </div>
    );
  }

  if (services.length === 0) {
    return (
      <div className="mt-6 rounded-2xl border border-dashed border-slate-200 px-4 py-6 text-center text-sm text-slate-500">
        No health data available.
      </div>
    );
  }

  return (
    <div className="mt-6 space-y-4">
      {services.map((item) => {
        const status = normalizeStatus(item.status);
        return (
          <div key={item.name} className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <span className={`h-2.5 w-2.5 rounded-full ${status.accent}`} />
              <span className="text-sm font-medium text-slate-700">{item.name}</span>
            </div>
            <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
              {status.label}
            </span>
          </div>
        );
      })}
    </div>
  );
}
