import type { VerifiedPack } from "./types";

const PREFIX = "cwi.user.web.pack.verified.";
const REQUIRED_VERIFICATION_SCOPE = "root_and_dependencies";

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

function buildKey(name: string): string {
  return `${PREFIX}${name}`;
}

function isVerifiedPack(value: unknown): value is VerifiedPack {
  if (!value || typeof value !== "object") {
    return false;
  }

  const record = value as Record<string, unknown>;
  return (
    typeof record.name === "string" &&
    typeof record.version === "string" &&
    typeof record.hash === "string" &&
    typeof record.verifiedAt === "string" &&
    Array.isArray(record.modules) &&
    record.verificationScope === REQUIRED_VERIFICATION_SCOPE &&
    Array.isArray(record.verifiedDependencies)
  );
}

function parseVerifiedPack(raw: string, key?: string): VerifiedPack | null {
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (!isVerifiedPack(parsed)) {
      if (key && hasWindow()) {
        window.localStorage.removeItem(key);
      }
      return null;
    }

    return parsed;
  } catch {
    if (key && hasWindow()) {
      window.localStorage.removeItem(key);
    }
    return null;
  }
}

export function saveVerifiedPack(pack: VerifiedPack): void {
  if (!hasWindow()) {
    return;
  }
  try {
    const key = buildKey(pack.name);
    window.localStorage.setItem(key, JSON.stringify(pack));
  } catch {
    return;
  }
}

export function loadVerifiedPack(name: string): VerifiedPack | null {
  if (!hasWindow()) {
    return null;
  }
  try {
    const key = buildKey(name);
    const raw = window.localStorage.getItem(key);
    if (!raw) {
      return null;
    }
    return parseVerifiedPack(raw, key);
  } catch {
    return null;
  }
}

export function clearVerifiedPack(name: string): void {
  if (!hasWindow()) {
    return;
  }
  try {
    window.localStorage.removeItem(buildKey(name));
  } catch {
    return;
  }
}

export function clearAllVerifiedPacks(): void {
  if (!hasWindow()) {
    return;
  }
  try {
    const keys: string[] = [];
    for (let i = 0; i < window.localStorage.length; i += 1) {
      const key = window.localStorage.key(i);
      if (key?.startsWith(PREFIX)) {
        keys.push(key);
      }
    }

    keys.forEach((key) => window.localStorage.removeItem(key));
  } catch {
    return;
  }
}

export function loadVerifiedPacks(): VerifiedPack[] {
  if (!hasWindow()) {
    return [];
  }
  try {
    const packs: VerifiedPack[] = [];
    for (let i = 0; i < window.localStorage.length; i += 1) {
      const key = window.localStorage.key(i);
      if (!key || !key.startsWith(PREFIX)) {
        continue;
      }
      const raw = window.localStorage.getItem(key);
      if (!raw) {
        continue;
      }
      const parsed = parseVerifiedPack(raw, key);
      if (parsed) {
        packs.push(parsed);
      }
    }
    return packs;
  } catch {
    return [];
  }
}
