import type {
  AskRequest,
  AskResponse,
  IngestDocumentRequest,
  IngestDocumentResponse,
  AccountHomeNodeLinkRequest,
  AccountHomeNodeLinkResponse,
  AccountConnectorCreateRequest,
  AccountConnectorConnection,
  AccountConnectorListResponse,
  AccountConnectorUpdateRequest,
  ActionRequest,
  ActionResponse,
  ApprovalTicketStatus,
  TraceLookupResponse,
  AskHistoryRecordRequest,
  AskHistoryRecord,
  AskHistoryListResponse,
  AskHistoryClearResponse,
  JourneyListResponse,
  ModelTier,
  FeedbackReportRequest,
  FeedbackReportResponse,
} from "./types";
import { expireAuthSession } from "../storage";

const REQUEST_ID_HEADERS = ["x-request-id", "x-correlation-id"];
const AUTH_EXPIRED_MESSAGE = "Session expired. Please sign in again.";
const ASK_REQUEST_TIMEOUT_MS = 240000;
const REQUEST_TIMEOUT_MESSAGE = "CWI is still working, but this request took longer than the browser wait limit. Please retry in a moment.";

export class ApiError extends Error {
  status: number;
  requestId?: string | null;
  payload?: Record<string, unknown> | null;

  constructor(message: string, status: number, payload?: Record<string, unknown> | null, requestId?: string | null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.payload = payload ?? null;
    this.requestId = requestId ?? null;
  }
}

export interface ApiClientOptions {
  baseUrl: string;
  token?: string | null;
  requestTimeoutMs?: number;
  modelTier?: ModelTier | null;
}

function normalizeBaseUrl(value: string): string {
  if (!value) {
    return "";
  }
  return value.endsWith("/") ? value.slice(0, -1) : value;
}

function getRequestId(response: Response): string | null {
  for (const header of REQUEST_ID_HEADERS) {
    const value = response.headers.get(header);
    if (value) {
      return value;
    }
  }
  return null;
}

function readPayloadString(
  payload: Record<string, unknown> | null | undefined,
  key: string,
): string | undefined {
  const value = payload?.[key];
  return typeof value === "string" && value.trim() ? value : undefined;
}

function buildQuery(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null) {
      continue;
    }
    if (typeof value === "string" && !value.trim()) {
      continue;
    }
    search.set(key, String(value));
  }
  const query = search.toString();
  return query ? `?${query}` : "";
}

async function readJson(response: Response): Promise<Record<string, unknown> | null> {
  const text = await response.text();
  if (!text) {
    return null;
  }
  try {
    return JSON.parse(text) as Record<string, unknown>;
  } catch {
    return null;
  }
}

async function requestJson<T>(
  url: string,
  init: RequestInit,
  timeoutMs?: number,
): Promise<T> {
  const controller = new AbortController();
  const timeout = timeoutMs && timeoutMs > 0
    ? setTimeout(() => controller.abort(REQUEST_TIMEOUT_MESSAGE), timeoutMs)
    : null;

  try {
    const response = await fetch(url, { ...init, signal: controller.signal });
    const requestId = getRequestId(response);
    const payload = await readJson(response);

    if (!response.ok) {
      if (response.status === 401) {
        expireAuthSession(AUTH_EXPIRED_MESSAGE);
      }
      const message =
        readPayloadString(payload, "message") ||
        readPayloadString(payload, "error") ||
        `Request failed with status ${response.status}`;
      throw new ApiError(message, response.status, payload, requestId);
    }

    return (payload ?? {}) as T;
  } catch (error) {
    if (isAbortLikeError(error, controller.signal)) {
      throw new ApiError(
        REQUEST_TIMEOUT_MESSAGE,
        408,
        { code: "client_timeout", message: REQUEST_TIMEOUT_MESSAGE },
        null,
      );
    }

    throw error;
  } finally {
    if (timeout) {
      clearTimeout(timeout);
    }
  }
}

function isAbortLikeError(error: unknown, signal: AbortSignal): boolean {
  if (signal.aborted) {
    return true;
  }

  if (!(error instanceof Error)) {
    return false;
  }

  return error.name === "AbortError"
    || /aborted without reason/i.test(error.message)
    || /operation was aborted/i.test(error.message);
}

export function formatApiError(error: ApiError): string {
  if (!error) {
    return "Request failed.";
  }
  const message = readPayloadString(error.payload, "message") ?? readPayloadString(error.payload, "error");
  if (message) {
    return message;
  }
  return error.message || `HTTP ${error.status}`;
}

export function getApiErrorPayloadString(error: ApiError, key: string): string | undefined {
  return readPayloadString(error.payload, key);
}

export function createApiClient(options: ApiClientOptions) {
  const baseUrl = normalizeBaseUrl(options.baseUrl);
  const token = options.token?.trim();
  const timeoutMs = options.requestTimeoutMs;
  const modelTier = options.modelTier?.trim();

  const buildHeaders = () => {
    const headers = new Headers();
    headers.set("Accept", "application/json");
    headers.set("Content-Type", "application/json");
    if (token) {
      headers.set("Authorization", `Bearer ${token}`);
    }
    if (modelTier && modelTier !== "unknown") {
      headers.set("x-model-tier", modelTier);
    }
    return headers;
  };

  return {
    ask: (payload: AskRequest, requestOptions?: { requestId?: string }) => {
      const headers = buildHeaders();
      if (requestOptions?.requestId) {
        headers.set("x-request-id", requestOptions.requestId);
      }
      const askTimeoutMs = Math.max(timeoutMs ?? 0, ASK_REQUEST_TIMEOUT_MS);
      return requestJson<AskResponse>(`${baseUrl}/api/ask`, {
        method: "POST",
        headers,
        body: JSON.stringify(payload),
      }, askTimeoutMs);
    },

    askDeferred: (workId: string, requestOptions: { requestId: string }) =>
      requestJson<AskResponse>(
        `${baseUrl}/api/ask/deferred/${encodeURIComponent(workId)}${buildQuery({ request_id: requestOptions.requestId })}`,
        {
          method: "GET",
          headers: buildHeaders(),
        },
        timeoutMs,
      ),

    ingestDocument: (payload: IngestDocumentRequest, requestOptions?: { requestId?: string }) => {
      const headers = buildHeaders();
      if (requestOptions?.requestId) {
        headers.set("x-request-id", requestOptions.requestId);
      }
      return requestJson<IngestDocumentResponse>(`${baseUrl}/api/ingest/document`, {
        method: "POST",
        headers,
        body: JSON.stringify(payload),
      }, timeoutMs);
    },

    action: (payload: ActionRequest) =>
      requestJson<ActionResponse>(`${baseUrl}/api/action`, {
        method: "POST",
        headers: buildHeaders(),
        body: JSON.stringify(payload),
      }, timeoutMs),

    approvalStatus: (ticketId: string) =>
      requestJson<ApprovalTicketStatus>(`${baseUrl}/api/approval/${encodeURIComponent(ticketId)}`, {
        method: "GET",
        headers: buildHeaders(),
      }, timeoutMs),

    traceLookup: (traceId: string) =>
      requestJson<TraceLookupResponse>(`${baseUrl}/api/trace/${encodeURIComponent(traceId)}`, {
        method: "GET",
        headers: buildHeaders(),
      }, timeoutMs),

    historyList: (options?: { limit?: number; retentionDays?: number }) => {
      const query = buildQuery({
        limit: options?.limit,
        retention_days: options?.retentionDays,
      });
      return requestJson<AskHistoryListResponse>(`${baseUrl}/api/history${query}`, {
        method: "GET",
        headers: buildHeaders(),
      }, timeoutMs);
    },

    historyRecord: (payload: AskHistoryRecordRequest) =>
      requestJson<AskHistoryRecord>(`${baseUrl}/api/history`, {
        method: "POST",
        headers: buildHeaders(),
        body: JSON.stringify(payload),
      }, timeoutMs),

    historyClear: () =>
      requestJson<AskHistoryClearResponse>(`${baseUrl}/api/history`, {
        method: "DELETE",
        headers: buildHeaders(),
      }, timeoutMs),

    accountHomeNode: () =>
      requestJson<AccountHomeNodeLinkResponse>(`${baseUrl}/api/account/home-node`, {
        method: "GET",
        headers: buildHeaders(),
      }, timeoutMs),

    linkAccountHomeNode: (payload: AccountHomeNodeLinkRequest) =>
      requestJson<AccountHomeNodeLinkResponse>(`${baseUrl}/api/account/home-node`, {
        method: "POST",
        headers: buildHeaders(),
        body: JSON.stringify(payload),
      }, timeoutMs),

    unlinkAccountHomeNode: () =>
      requestJson<AccountHomeNodeLinkResponse>(`${baseUrl}/api/account/home-node`, {
        method: "DELETE",
        headers: buildHeaders(),
      }, timeoutMs),

    accountConnectors: () =>
      requestJson<AccountConnectorListResponse>(`${baseUrl}/api/account/connectors`, {
        method: "GET",
        headers: buildHeaders(),
      }, timeoutMs),

    createAccountConnector: (payload: AccountConnectorCreateRequest) =>
      requestJson<AccountConnectorConnection>(`${baseUrl}/api/account/connectors`, {
        method: "POST",
        headers: buildHeaders(),
        body: JSON.stringify(payload),
      }, timeoutMs),

    updateAccountConnector: (connectionId: string, payload: AccountConnectorUpdateRequest) =>
      requestJson<AccountConnectorConnection>(`${baseUrl}/api/account/connectors/${encodeURIComponent(connectionId)}`, {
        method: "PATCH",
        headers: buildHeaders(),
        body: JSON.stringify(payload),
      }, timeoutMs),

    disconnectAccountConnector: (connectionId: string) =>
      requestJson<AccountConnectorConnection>(`${baseUrl}/api/account/connectors/${encodeURIComponent(connectionId)}`, {
        method: "DELETE",
        headers: buildHeaders(),
      }, timeoutMs),

    syncAccountConnector: (connectionId: string, force = true) =>
      requestJson<AccountConnectorConnection>(`${baseUrl}/api/account/connectors/${encodeURIComponent(connectionId)}/sync`, {
        method: "POST",
        headers: buildHeaders(),
        body: JSON.stringify({ force }),
      }, timeoutMs),

    journeyList: (requestId: string) =>
      requestJson<JourneyListResponse>(`${baseUrl}/api/journey/requests/${encodeURIComponent(requestId)}`, {
        method: "GET",
        headers: buildHeaders(),
      }, timeoutMs),

    feedbackReport: (payload: FeedbackReportRequest, requestOptions?: { requestId?: string }) => {
      const headers = buildHeaders();
      if (requestOptions?.requestId) {
        headers.set("x-request-id", requestOptions.requestId);
      }
      return requestJson<FeedbackReportResponse>(`${baseUrl}/api/feedback/report`, {
        method: "POST",
        headers,
        body: JSON.stringify(payload),
      }, timeoutMs);
    },
  };
}
