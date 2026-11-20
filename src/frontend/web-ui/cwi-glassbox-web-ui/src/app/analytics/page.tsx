"use client";

import { useEffect } from "react";
import AppShell from "@/components/app-shell";
import TelemetrySettings from "@/components/telemetry-settings";
import TelemetryDashboard from "@/components/telemetry-dashboard";
import { useTelemetry } from "@/lib/telemetry/context";

export default function AnalyticsPage() {
  const { trackEvent } = useTelemetry();

  useEffect(() => {
    trackEvent("page_view", "analytics", { page: "analytics" });
  }, [trackEvent]);

  return (
    <AppShell
      title="Operator interaction analytics"
      subtitle="Capture opt-in interaction telemetry, monitor sampling health, and review aggregate signals."
      badge="Telemetry"
    >
      <div className="grid gap-6 lg:grid-cols-[1.05fr_0.95fr]">
        <TelemetrySettings />
        <TelemetryDashboard />
      </div>
    </AppShell>
  );
}
