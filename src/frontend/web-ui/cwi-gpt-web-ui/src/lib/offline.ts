import { useEffect, useState } from "react";

export interface OfflineState {
  isOffline: boolean;
  reason: string | null;
  lastChangedAt: string | null;
}

let currentState: OfflineState = {
  isOffline: false,
  reason: null,
  lastChangedAt: null,
};

const listeners = new Set<(state: OfflineState) => void>();
let listenersInitialized = false;

function notify() {
  for (const listener of listeners) {
    listener(currentState);
  }
}

function setState(next: OfflineState) {
  currentState = next;
  notify();
}

export function initOfflineListeners(): void {
  if (listenersInitialized || typeof window === "undefined") {
    return;
  }
  listenersInitialized = true;

  window.addEventListener("offline", () => markOffline("network"));
  window.addEventListener("online", () => markOnline());
}

export function markOffline(reason: string = "network"): void {
  setState({
    isOffline: true,
    reason,
    lastChangedAt: new Date().toLocaleString(),
  });
}

export function markOnline(): void {
  setState({
    isOffline: false,
    reason: null,
    lastChangedAt: new Date().toLocaleString(),
  });
}

function subscribe(listener: (state: OfflineState) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useOfflineStatus(): OfflineState {
  const [state, setStateSnapshot] = useState(currentState);

  useEffect(() => {
    return subscribe(setStateSnapshot);
  }, []);

  return state;
}
