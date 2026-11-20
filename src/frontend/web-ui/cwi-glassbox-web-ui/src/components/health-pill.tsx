"use client";

import { useEffect, useState } from "react";
import { getRuntimeConfigStatus } from "@/lib/config";
import { createApiClient } from "@/lib/api/client";

type HealthState = "loading" | "ok" | "down";

export default function HealthPill() {
  const status = getRuntimeConfigStatus();
  const [state, setState] = useState<HealthState>(status.config ? "loading" : "down");

  useEffect(() => {
    if (!status.config) {
      return;
    }

    const client = createApiClient({ baseUrl: status.config.apiBaseUrl });
    client
      .health()
      .then((payload) => setState(payload.ok ? "ok" : "down"))
      .catch(() => setState("down"));
  }, [status.config]);

  if (state === "loading") {
    return (
      <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold text-slate-500">
        Checking...
      </span>
    );
  }

  if (state === "ok") {
    return (
      <span className="rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold text-emerald-700">
        Stable
      </span>
    );
  }

  return (
    <span className="rounded-full bg-rose-100 px-3 py-1 text-xs font-semibold text-rose-700">
      Attention
    </span>
  );
}
