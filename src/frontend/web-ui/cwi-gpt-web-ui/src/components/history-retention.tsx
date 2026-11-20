"use client";

import { useEffect } from "react";
import { loadAskHistorySettings, purgeLocalAskHistory } from "@/lib/history";
import { resolveRuntimeConfig } from "@/lib/config";
import { loadOverrides } from "@/lib/storage";

export function HistoryRetentionGate() {
  useEffect(() => {
    const runtime = resolveRuntimeConfig(loadOverrides());
    if (runtime.config.authMode !== "none") {
      return;
    }
    const settings = loadAskHistorySettings();
    purgeLocalAskHistory(settings);
  }, []);

  return null;
}
