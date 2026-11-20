import { useEffect } from "react";

export const REMOTE_HISTORY_REFRESH_INTERVAL_MS = 15000;

export function shouldRefreshRemoteHistory(options: {
  enabled: boolean;
  isRemoteHistory: boolean;
  visibilityState?: string | null;
}): boolean {
  if (!options.enabled || !options.isRemoteHistory) {
    return false;
  }

  return options.visibilityState !== "hidden";
}

export function useRemoteHistoryRefresh(options: {
  enabled: boolean;
  isRemoteHistory: boolean;
  refresh: () => void | Promise<void>;
  intervalMs?: number;
}): void {
  const { enabled, isRemoteHistory, refresh, intervalMs = REMOTE_HISTORY_REFRESH_INTERVAL_MS } = options;

  useEffect(() => {
    if (typeof window === "undefined" || typeof document === "undefined") {
      return;
    }

    if (!enabled || !isRemoteHistory) {
      return;
    }

    const refreshIfVisible = () => {
      if (!shouldRefreshRemoteHistory({
        enabled,
        isRemoteHistory,
        visibilityState: document.visibilityState,
      })) {
        return;
      }

      void refresh();
    };

    const onVisibilityChange = () => {
      if (document.visibilityState === "visible") {
        refreshIfVisible();
      }
    };

    window.addEventListener("focus", refreshIfVisible);
    window.addEventListener("online", refreshIfVisible);
    document.addEventListener("visibilitychange", onVisibilityChange);

    const intervalId = window.setInterval(refreshIfVisible, intervalMs);

    return () => {
      window.clearInterval(intervalId);
      window.removeEventListener("focus", refreshIfVisible);
      window.removeEventListener("online", refreshIfVisible);
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [enabled, intervalMs, isRemoteHistory, refresh]);
}
