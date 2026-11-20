import type { AskCitation, AskHistoryAttachment, AskHistoryRecord, AskHistoryRecordRequest, JourneyEvent, Trace } from "./api/types";
import { createApiClient } from "./api/client";
import { getScopedStorageKey } from "./session-identity";

const HISTORY_KEY = "cwi.user.web.ask.history";
const SETTINGS_KEY = "cwi.user.web.ask.history.settings";

type HistoryListApiClient = Pick<ReturnType<typeof createApiClient>, "historyList">;
type HistoryRecordApiClient = Pick<ReturnType<typeof createApiClient>, "historyRecord">;
type HistoryClearApiClient = Pick<ReturnType<typeof createApiClient>, "historyClear">;

export interface AskHistorySettings {
  enabled: boolean;
  maxEntries: number;
  retentionDays: number;
}

export interface AskHistoryItem {
  id: string;
  requestId?: string;
  conversationId?: string;
  question: string;
  answer: string;
  responseStatus?: string;
  createdAt: string;
  traceId?: string;
  packName?: string;
  packHash?: string;
  trace?: Trace | null;
  citations?: AskCitation[];
  attachments?: AskHistoryAttachment[];
  journeySeed?: JourneyEvent[];
}

const DEFAULT_SETTINGS: AskHistorySettings = {
  enabled: true,
  maxEntries: 25,
  retentionDays: 30,
};

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

function parseHistory(raw: string | null): AskHistoryItem[] {
  if (!raw) {
    return [];
  }
  try {
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return [];
    }
    return parsed as AskHistoryItem[];
  } catch {
    return [];
  }
}

function generateId(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }
  return `hist_${Date.now()}_${Math.floor(Math.random() * 10000)}`;
}

export function loadAskHistorySettings(): AskHistorySettings {
  const raw = readLocal(getScopedStorageKey(SETTINGS_KEY));
  if (!raw) {
    return { ...DEFAULT_SETTINGS };
  }
  try {
    const parsed = JSON.parse(raw) as Partial<AskHistorySettings>;
    return {
      enabled: parsed.enabled ?? DEFAULT_SETTINGS.enabled,
      maxEntries: parsed.maxEntries ?? DEFAULT_SETTINGS.maxEntries,
      retentionDays: parsed.retentionDays ?? DEFAULT_SETTINGS.retentionDays,
    };
  } catch {
    return { ...DEFAULT_SETTINGS };
  }
}

export function saveAskHistorySettings(settings: AskHistorySettings): void {
  writeLocal(getScopedStorageKey(SETTINGS_KEY), JSON.stringify(settings));
}

export function purgeLocalAskHistory(settings: AskHistorySettings): AskHistoryItem[] {
  if (!settings.enabled) {
    return [];
  }
  const historyKey = getScopedStorageKey(HISTORY_KEY);
  const raw = readLocal(historyKey);
  const items = parseHistory(raw);
  const cutoff = settings.retentionDays > 0
    ? Date.now() - settings.retentionDays * 24 * 60 * 60 * 1000
    : null;

  const filtered = items.filter((item) => {
    if (!cutoff) {
      return true;
    }
    const time = new Date(item.createdAt).getTime();
    return Number.isFinite(time) && time >= cutoff;
  });

  const trimmed = filtered.slice(0, Math.max(0, settings.maxEntries));
  writeLocal(historyKey, JSON.stringify(trimmed));
  return trimmed;
}

export function clearLocalAskHistory(): void {
  removeLocal(getScopedStorageKey(HISTORY_KEY));
}

export function recordLocalAskHistory(data: Omit<AskHistoryItem, "id" | "createdAt">): void {
  const settings = loadAskHistorySettings();
  if (!settings.enabled) {
    return;
  }
  const historyKey = getScopedStorageKey(HISTORY_KEY);
  const raw = readLocal(historyKey);
  const items = parseHistory(raw);
  const entry: AskHistoryItem = {
    id: generateId(),
    createdAt: new Date().toISOString(),
    ...data,
  };
  const updated = [entry, ...items];
  const trimmed = updated.slice(0, Math.max(0, settings.maxEntries));
  writeLocal(historyKey, JSON.stringify(trimmed));
}

function mapRemoteItem(record: AskHistoryRecord): AskHistoryItem {
  return {
    id: record.id,
    requestId: record.request_id,
    conversationId: record.conversation_id,
    question: record.question,
    answer: record.answer,
    responseStatus: record.response_status ?? resolveRemoteResponseStatus(record),
    createdAt: record.created_at,
    traceId: record.trace_id,
    packName: record.pack_name,
    packHash: record.pack_hash,
    trace: record.trace ?? null,
    citations: [],
    attachments: record.attachments ?? [],
  };
}

export async function fetchRemoteAskHistory(
  client: HistoryListApiClient,
  settings: AskHistorySettings,
): Promise<AskHistoryItem[]> {
  const response = await client.historyList({
    limit: settings.maxEntries,
    retentionDays: settings.retentionDays,
  });
  return response.items.map(mapRemoteItem);
}

export async function recordRemoteAskHistory(
  client: HistoryRecordApiClient,
  data: Omit<AskHistoryItem, "id" | "createdAt">,
  settings?: AskHistorySettings,
): Promise<AskHistoryItem | null> {
  const payload: AskHistoryRecordRequest = {
    request_id: data.requestId,
    conversation_id: data.conversationId,
    question: data.question,
    answer: data.answer,
    response_status: data.responseStatus ?? "completed",
    trace_id: data.traceId,
    pack_name: data.packName,
    pack_hash: data.packHash,
    trace: data.trace ?? null,
    attachments: data.attachments ?? [],
    max_entries: settings?.maxEntries,
    retention_days: settings?.retentionDays,
  };
  const record = await client.historyRecord(payload);
  return mapRemoteItem(record);
}

function resolveRemoteResponseStatus(record: AskHistoryRecord): string {
  const status = record.summary_status?.trim().toLowerCase();
  if (status === "failed" || status === "processing" || status === "completed") {
    return status;
  }

  return record.answer?.trim() ? "completed" : "processing";
}

export async function clearRemoteAskHistory(client: HistoryClearApiClient): Promise<number> {
  const response = await client.historyClear();
  return response.deleted ?? 0;
}
