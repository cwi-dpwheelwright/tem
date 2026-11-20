import type { RuntimeOverrides } from "./config";
import type { ModelTier } from "./api/types";
import { getScopedStorageKey } from "./session-identity";

const KEY_PREFIX = "cwi.user.web.";
const TOKEN_KEY = `${KEY_PREFIX}token.access`;
const TOKEN_LAST_USED_KEY = `${KEY_PREFIX}token.lastUsed`;
const AUTH_NOTICE_KEY = `${KEY_PREFIX}auth.notice`;
const OVERRIDES_KEY = `${KEY_PREFIX}overrides`;
const ASK_DRAFT_KEY = `${KEY_PREFIX}ask.draft`;
const ERROR_REPORTING_KEY = `${KEY_PREFIX}error.reporting.enabled`;
const MODEL_TIER_KEY = `${KEY_PREFIX}model.tier`;
const WEB_RETRIEVAL_KEY = `${KEY_PREFIX}web.retrieval.enabled`;
const USER_LABEL_KEY = `${KEY_PREFIX}user.label`;

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

function readLocal(key: string): string | null {
  if (!hasWindow()) {
    return null;
  }
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeLocal(key: string, value: string): void {
  if (!hasWindow()) {
    return;
  }
  try {
    window.localStorage.setItem(key, value);
  } catch {
    return;
  }
}

function removeLocal(key: string): void {
  if (!hasWindow()) {
    return;
  }
  try {
    window.localStorage.removeItem(key);
  } catch {
    return;
  }
}

function readSession(key: string): string | null {
  if (!hasWindow()) {
    return null;
  }
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeSession(key: string, value: string): void {
  if (!hasWindow()) {
    return;
  }
  try {
    window.sessionStorage.setItem(key, value);
  } catch {
    return;
  }
}

function removeSession(key: string): void {
  if (!hasWindow()) {
    return;
  }
  try {
    window.sessionStorage.removeItem(key);
  } catch {
    return;
  }
}

export function saveAuthToken(token: string): void {
  if (!token) {
    return;
  }
  writeSession(TOKEN_KEY, token);
  writeSession(TOKEN_LAST_USED_KEY, Date.now().toString());
}

export function loadAuthToken(): string | null {
  return readSession(TOKEN_KEY);
}

export function touchAuthToken(): void {
  if (!loadAuthToken()) {
    return;
  }
  writeSession(TOKEN_LAST_USED_KEY, Date.now().toString());
}

export function loadAuthTokenLastUsed(): number | null {
  const raw = readSession(TOKEN_LAST_USED_KEY);
  if (!raw) {
    return null;
  }
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : null;
}

export function clearAuthSession(): void {
  removeSession(TOKEN_KEY);
  removeSession(TOKEN_LAST_USED_KEY);
  removeSession(USER_LABEL_KEY);

  const overrides = loadOverrides();
  if (overrides.userId) {
    const rest = { ...overrides };
    delete rest.userId;
    saveOverrides(rest);
  }
}

export function saveAuthNotice(message: string): void {
  if (!message) {
    return;
  }
  writeLocal(AUTH_NOTICE_KEY, message);
}

export function consumeAuthNotice(): string | null {
  const notice = readLocal(AUTH_NOTICE_KEY);
  if (notice) {
    removeLocal(AUTH_NOTICE_KEY);
  }
  return notice;
}

export function loadOverrides(): RuntimeOverrides {
  const raw = readLocal(OVERRIDES_KEY);
  if (!raw) {
    return {};
  }
  try {
    return JSON.parse(raw) as RuntimeOverrides;
  } catch {
    return {};
  }
}

export function saveOverrides(overrides: RuntimeOverrides): void {
  writeLocal(OVERRIDES_KEY, JSON.stringify(overrides ?? {}));
}

export function saveUserLabel(label: string): void {
  const trimmed = label.trim();
  if (!trimmed) {
    return;
  }
  writeSession(USER_LABEL_KEY, trimmed);
}

export function loadUserLabel(): string | null {
  return readSession(USER_LABEL_KEY);
}

export function loadAskDraft(): string | null {
  return readSession(getScopedStorageKey(ASK_DRAFT_KEY));
}

export function saveAskDraft(value: string): void {
  writeSession(getScopedStorageKey(ASK_DRAFT_KEY), value);
}

export function clearAskDraft(): void {
  removeSession(getScopedStorageKey(ASK_DRAFT_KEY));
}

export function expireAuthSession(notice: string): void {
  if (notice) {
    saveAuthNotice(notice);
  }
  clearAuthSession();
  if (typeof window !== "undefined") {
    try {
      window.dispatchEvent(new Event("cwi-auth-expired"));
    } catch {
      return;
    }
  }
}

export function isErrorReportingEnabled(): boolean {
  return readLocal(ERROR_REPORTING_KEY) === "true";
}

export function setErrorReportingEnabled(enabled: boolean): void {
  writeLocal(ERROR_REPORTING_KEY, enabled ? "true" : "false");
}

export function loadModelTier(): ModelTier {
  const value = readLocal(getScopedStorageKey(MODEL_TIER_KEY));
  if (value === "pro" || value === "free" || value === "edge") {
    return value;
  }
  return "free";
}

export function saveModelTier(tier: ModelTier): void {
  if (!tier) {
    return;
  }
  writeLocal(getScopedStorageKey(MODEL_TIER_KEY), tier);
}

export function loadUseWeb(): boolean {
  return readLocal(getScopedStorageKey(WEB_RETRIEVAL_KEY)) === "true";
}

export function saveUseWeb(enabled: boolean): void {
  writeLocal(getScopedStorageKey(WEB_RETRIEVAL_KEY), enabled ? "true" : "false");
}
