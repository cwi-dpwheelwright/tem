import { getScopedStorageKey } from "@/lib/session-identity";

const KEY = "cwi.user.web.pack.selected";

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

export function loadSelectedPackName(): string | null {
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

export function saveSelectedPackName(name: string | null): void {
  if (!hasWindow()) {
    return;
  }
  try {
    const key = getScopedStorageKey(KEY);
    if (!name) {
      window.localStorage.removeItem(key);
      return;
    }
    window.localStorage.setItem(key, name);
  } catch {
    return;
  }
}
