const STORAGE_VERSION_KEY = "cwi.user.web.storage.version";
const CURRENT_VERSION = "1";

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

export function runStorageMigrations(): void {
  if (!hasWindow()) {
    return;
  }
  try {
    const current = window.localStorage.getItem(STORAGE_VERSION_KEY);
    if (current === CURRENT_VERSION) {
      return;
    }
    window.localStorage.setItem(STORAGE_VERSION_KEY, CURRENT_VERSION);
  } catch {
    return;
  }
}
