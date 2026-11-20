import { getScopedStorageKey } from "@/lib/session-identity";

const KEY = "cwi.user.web.pillar.selected";

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

export function loadSelectedPillar(): string | null {
  if (!hasWindow()) {
    return null;
  }
  try {
    const value = window.localStorage.getItem(getScopedStorageKey(KEY));
    return value && value.trim() ? value : null;
  } catch {
    return null;
  }
}

export function saveSelectedPillar(value: string | null): void {
  if (!hasWindow()) {
    return;
  }
  try {
    const key = getScopedStorageKey(KEY);
    if (!value) {
      window.localStorage.removeItem(key);
      return;
    }
    window.localStorage.setItem(key, value);
  } catch {
    return;
  }
}
