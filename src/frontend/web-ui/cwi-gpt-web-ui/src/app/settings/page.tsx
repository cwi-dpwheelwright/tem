"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent } from "react";
import { useRouter } from "next/navigation";
import { HomeNodeLinkModal } from "@/components/home-node-link-modal";
import { Modal } from "@/components/modal";
import { useSessionIdentity } from "@/components/session-badge";
import { ApiError, createApiClient, formatApiError } from "@/lib/api/client";
import type {
  AccountConnectorConnection,
  AccountConnectorListResponse,
  AccountHomeNodeLinkRequest,
  AccountHomeNodeLinkResponse,
} from "@/lib/api/types";
import { AUTH_NOTICE_MESSAGE, handleAuthRedirect, resolveAuthToken } from "@/lib/auth";
import { resolveRuntimeConfig } from "@/lib/config";
import {
  buildChatHistoryIngestRequests,
  isChatHistoryImportFile,
  MAX_CHAT_HISTORY_IMPORT_FILE_BYTES,
  type ChatHistoryIngestRequestPart,
  type ChatHistoryNormalizationMode,
} from "@/lib/dataset-ingest";
import { takeSelectedFiles } from "@/lib/file-input";
import { formatDateTime } from "@/lib/format";
import { logApiError } from "@/lib/logger";
import { useOfflineStatus } from "@/lib/offline";
import { formatFileSize } from "@/lib/prompt-composer";
import { resolveSessionIdentity } from "@/lib/session-identity";
import { consumeAuthNotice, loadOverrides } from "@/lib/storage";

type AccountSettingsState = "loading" | "ready" | "error";

type AccountDetailItem = {
  label: string;
  value: string;
  breakValue?: boolean;
};

type ImportItem = {
  id: string;
  name: string;
  status: "uploaded" | "partial" | "failed";
  detail: string;
  importedChunks: number;
  failedChunks: number;
};

type ImportBatch = {
  id: string;
  createdAt: string;
  uploadedCount: number;
  partialCount: number;
  failedCount: number;
  totalCount: number;
  importedChunks: number;
  items: ImportItem[];
};

const ACCOUNT_IMPORT_CONCURRENCY = 3;
const EMPTY_CONNECTOR_LIST: AccountConnectorListResponse = {
  available: [],
  connections: [],
};

function renderBooleanLabel(value: boolean): string {
  return value ? "Enabled" : "Disabled";
}

function formatAuthModeLabel(value: string): string {
  if (value === "oidc") {
    return "Identity session";
  }
  if (value === "token") {
    return "Token session";
  }
  return "Open access";
}

function formatConnectorStatus(value: string): string {
  if (value === "connected") {
    return "Connected";
  }
  if (value === "paused") {
    return "Paused";
  }
  if (value === "queued") {
    return "Queued";
  }
  return "Sign in required";
}

function formatConnectorSyncLabel(connection: AccountConnectorConnection): string {
  if (connection.last_sync_utc) {
    return `Last sync ${formatDateTime(connection.last_sync_utc)}`;
  }
  return connection.last_sync_status ? `Sync ${connection.last_sync_status.replace(/_/g, " ")}` : "Sync not started";
}

function connectorPillClass(value: string): string {
  if (value === "connected") {
    return "connected-services-status connected-services-status--connected";
  }
  if (value === "paused") {
    return "connected-services-status connected-services-status--paused";
  }
  return "connected-services-status";
}

function mergeConnector(
  list: AccountConnectorListResponse,
  connection: AccountConnectorConnection,
): AccountConnectorListResponse {
  const without = list.connections.filter((item) => item.connection_id !== connection.connection_id);
  return {
    ...list,
    connections: connection.status === "disconnected" ? without : [...without, connection],
  };
}

export default function SettingsPage() {
  const router = useRouter();
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const { displayName, tenantId, userId, isVisible } = useSessionIdentity();
  const { isOffline } = useOfflineStatus();
  const [notice] = useState(() => consumeAuthNotice());
  const [settingsState, setSettingsState] = useState<AccountSettingsState>("loading");
  const [linkedNode, setLinkedNode] = useState<AccountHomeNodeLinkResponse | null>(null);
  const [connectorsState, setConnectorsState] = useState<AccountSettingsState>("loading");
  const [connectors, setConnectors] = useState<AccountConnectorListResponse>(EMPTY_CONNECTOR_LIST);
  const [connectorNotice, setConnectorNotice] = useState<string | null>(null);
  const [connectorError, setConnectorError] = useState<string | null>(null);
  const [connectorBusyId, setConnectorBusyId] = useState<string | null>(null);
  const [connectorCreateBusyType, setConnectorCreateBusyType] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [linkError, setLinkError] = useState<string | null>(null);
  const [linkNotice, setLinkNotice] = useState<string | null>(null);
  const [isLinkModalOpen, setIsLinkModalOpen] = useState(false);
  const [isLinking, setIsLinking] = useState(false);
  const [isUnlinking, setIsUnlinking] = useState(false);
  const [isImportModalOpen, setIsImportModalOpen] = useState(false);
  const [isImporting, setIsImporting] = useState(false);
  const [importStatusMessage, setImportStatusMessage] = useState<string | null>(null);
  const [importBatch, setImportBatch] = useState<ImportBatch | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const accountImportInputRef = useRef<HTMLInputElement>(null);

  const loadLinkedNode = useCallback(async () => {
    if (runtime.config.authMode === "none") {
      setLinkedNode(null);
      setLoadError(null);
      setSettingsState("ready");
      return;
    }

    setSettingsState("loading");
    setLoadError(null);

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const response = await client.accountHomeNode();
      setLinkedNode(response);
      setSettingsState("ready");
    } catch (error) {
      if (handleAuthRedirect(error, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (error instanceof ApiError) {
        logApiError(error);
      }
      setLoadError(error instanceof ApiError ? formatApiError(error) : "Unable to load your linked node.");
      setSettingsState("error");
    }
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
  ]);

  const loadConnectors = useCallback(async () => {
    if (runtime.config.authMode === "none") {
      setConnectors(EMPTY_CONNECTOR_LIST);
      setConnectorError(null);
      setConnectorsState("ready");
      return;
    }

    setConnectorsState("loading");
    setConnectorError(null);

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const response = await client.accountConnectors();
      setConnectors(response);
      setConnectorsState("ready");
    } catch (error) {
      if (handleAuthRedirect(error, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (error instanceof ApiError) {
        logApiError(error);
      }
      setConnectorError(error instanceof ApiError ? formatApiError(error) : "Unable to load connected services.");
      setConnectorsState("error");
    }
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
  ]);

  useEffect(() => {
    void loadLinkedNode();
  }, [loadLinkedNode]);

  useEffect(() => {
    void loadConnectors();
  }, [loadConnectors]);

  useEffect(() => {
    document.body.classList.add("modal-open");
    return () => document.body.classList.remove("modal-open");
  }, []);

  const closeAccountSettings = useCallback(() => {
    if (typeof window !== "undefined" && window.history.length > 1) {
      router.back();
      return;
    }

    router.push("/");
  }, [router]);

  const handleLinkNode = useCallback(async (request: AccountHomeNodeLinkRequest): Promise<boolean> => {
    if (runtime.config.authMode === "none") {
      setLinkError("Node linking requires an authenticated account.");
      return false;
    }

    setIsLinking(true);
    setLinkError(null);
    setLinkNotice(null);

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return false;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const response = await client.linkAccountHomeNode(request);
      setLinkedNode(response);
      setSettingsState("ready");
      setIsLinkModalOpen(false);
      setLinkNotice(
        response.node?.node_id
          ? `Linked to node ${response.node.node_id}${response.matched_via ? ` via ${response.matched_via}.` : "."}`
          : "Your node is now linked to this account.",
      );
      return true;
    } catch (error) {
      if (handleAuthRedirect(error, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return false;
      }
      if (error instanceof ApiError) {
        logApiError(error);
      }
      setLinkError(error instanceof ApiError ? formatApiError(error) : "Unable to link this node.");
      return false;
    } finally {
      setIsLinking(false);
    }
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
  ]);

  const handleUnlinkNode = useCallback(async () => {
    if (runtime.config.authMode === "none") {
      setLinkError("Node unlinking requires an authenticated account.");
      return;
    }

    if (typeof window !== "undefined") {
      const confirmed = window.confirm("Unlink this home node from your account? You can link it again later by scanning the QR code.");
      if (!confirmed) {
        return;
      }
    }

    setIsUnlinking(true);
    setLinkError(null);
    setLinkNotice(null);

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const response = await client.unlinkAccountHomeNode();
      setLinkedNode(response);
      setSettingsState("ready");
      setLinkNotice("Your account is no longer linked to a home node.");
    } catch (error) {
      if (handleAuthRedirect(error, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (error instanceof ApiError) {
        logApiError(error);
      }
      setLinkError(error instanceof ApiError ? formatApiError(error) : "Unable to unlink this node.");
    } finally {
      setIsUnlinking(false);
    }
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
  ]);

  const handleCreateConnector = useCallback(async (connectorType: string) => {
    if (runtime.config.authMode === "none") {
      setConnectorError("Connected services require an authenticated account.");
      return;
    }

    setConnectorCreateBusyType(connectorType);
    setConnectorError(null);
    setConnectorNotice(null);

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const connection = await client.createAccountConnector({ connector_type: connectorType });
      setConnectors((current) => mergeConnector(current, connection));
      if (connection.authorization_url) {
        setConnectorNotice(`Opening ${connection.display_name} sign-in.`);
        window.location.assign(connection.authorization_url);
        return;
      }
      if (!connection.authorization_configured) {
        setConnectorError(`${connection.display_name} sign-in is not configured yet.`);
        return;
      }
      setConnectorNotice(`${connection.display_name} sign-in was started.`);
    } catch (error) {
      if (handleAuthRedirect(error, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (error instanceof ApiError) {
        logApiError(error);
      }
      setConnectorError(error instanceof ApiError ? formatApiError(error) : "Unable to add this connector.");
    } finally {
      setConnectorCreateBusyType(null);
    }
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
  ]);

  const handleSyncConnector = useCallback(async (connection: AccountConnectorConnection) => {
    setConnectorBusyId(connection.connection_id);
    setConnectorError(null);
    setConnectorNotice(null);

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const updated = await client.syncAccountConnector(connection.connection_id, true);
      setConnectors((current) => mergeConnector(current, updated));
      setConnectorNotice(`${updated.display_name} sync queued.`);
    } catch (error) {
      if (handleAuthRedirect(error, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (error instanceof ApiError) {
        logApiError(error);
      }
      setConnectorError(error instanceof ApiError ? formatApiError(error) : "Unable to queue connector sync.");
    } finally {
      setConnectorBusyId(null);
    }
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
  ]);

  const handleDisconnectConnector = useCallback(async (connection: AccountConnectorConnection) => {
    if (typeof window !== "undefined") {
      const confirmed = window.confirm(`Disconnect ${connection.display_name}?`);
      if (!confirmed) {
        return;
      }
    }

    setConnectorBusyId(connection.connection_id);
    setConnectorError(null);
    setConnectorNotice(null);

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const updated = await client.disconnectAccountConnector(connection.connection_id);
      setConnectors((current) => mergeConnector(current, updated));
      setConnectorNotice(`${connection.display_name} disconnected.`);
    } catch (error) {
      if (handleAuthRedirect(error, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (error instanceof ApiError) {
        logApiError(error);
      }
      setConnectorError(error instanceof ApiError ? formatApiError(error) : "Unable to disconnect this connector.");
    } finally {
      setConnectorBusyId(null);
    }
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
  ]);

  const openImportModal = useCallback(() => {
    setImportError(null);
    setIsImportModalOpen(true);
  }, []);

  const closeImportModal = useCallback(() => {
    if (isImporting) {
      return;
    }
    setIsImportModalOpen(false);
  }, [isImporting]);

  const clearImportBatch = useCallback(() => {
    setImportBatch(null);
    setImportStatusMessage(null);
    setImportError(null);
    if (accountImportInputRef.current) {
      accountImportInputRef.current.value = "";
    }
  }, []);

  const handleAccountImport = useCallback(async (event: ChangeEvent<HTMLInputElement>) => {
    const files = takeSelectedFiles(event.currentTarget);
    if (files.length === 0) {
      return;
    }

    if (isOffline) {
      setImportError("Offline mode: reconnect before importing account data.");
      return;
    }

    if (runtime.config.authMode === "none" || !isVisible) {
      setImportError("Account imports require a signed-in session.");
      return;
    }

    setIsImporting(true);
    setImportBatch(null);
    setImportError(null);
    setImportStatusMessage("Preparing account import...");

    try {
      const token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        router.replace("/login");
        return;
      }

      const identity = resolveSessionIdentity({
        authMode: runtime.config.authMode,
        tenantId: runtime.config.tenantId,
        runtimeUserId: runtime.config.userId,
        token,
      });
      const resolvedTenantId = identity.tenantId ?? tenantId ?? runtime.config.tenantId;
      const resolvedUserId = identity.userId ?? userId;
      if (!resolvedTenantId || !resolvedUserId) {
        setImportError("Tenant or user identity is missing from the authenticated session.");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      const uploadBatchId = crypto.randomUUID();
      const uploadedAt = new Date().toISOString();
      const items: ImportItem[] = [];
      let totalImportedChunks = 0;

      const uploadQueuedParts = async (queuedParts: ChatHistoryIngestRequestPart[]) => {
        return Promise.all(
          queuedParts.map(async (part) => {
            try {
              const response = await client.ingestDocument(part.request, {
                requestId: `${uploadBatchId}-${part.request.document.doc_id}`,
              });
              return {
                ok: true as const,
                detail: response.message?.trim() || "accepted",
              };
            } catch (uploadError) {
              if (handleAuthRedirect(uploadError, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
                return { ok: false as const, authRedirected: true as const, detail: "Authentication required." };
              }
              if (uploadError instanceof ApiError) {
                logApiError(uploadError);
              }
              return {
                ok: false as const,
                authRedirected: false as const,
                detail: uploadError instanceof ApiError ? formatApiError(uploadError) : "Upload failed.",
              };
            }
          }),
        );
      };

      for (const [index, file] of files.entries()) {
        if (!isChatHistoryImportFile(file.name, file.type)) {
          items.push({
            id: `${uploadBatchId}-invalid-${index}`,
            name: file.name,
            status: "failed",
            detail: "Only text-like files, JSON exports, images, and ZIP archives containing supported files can be imported right now.",
            importedChunks: 0,
            failedChunks: 0,
          });
          continue;
        }

        if (file.size > MAX_CHAT_HISTORY_IMPORT_FILE_BYTES) {
          items.push({
            id: `${uploadBatchId}-oversized-${index}`,
            name: file.name,
            status: "failed",
            detail: `This file is larger than the current ${formatFileSize(MAX_CHAT_HISTORY_IMPORT_FILE_BYTES)} account import limit.`,
            importedChunks: 0,
            failedChunks: 0,
          });
          continue;
        }

        try {
          setImportStatusMessage(`Importing ${file.name} (${formatFileSize(file.size)})...`);

          let importedChunks = 0;
          let failedChunks = 0;
          let importMode: ChatHistoryNormalizationMode = "raw_chunked";
          let lastFailureDetail: string | null = null;
          let stopFileImport = false;
          const queuedParts: ChatHistoryIngestRequestPart[] = [];

          for await (const part of buildChatHistoryIngestRequests(file, {
            tenantId: resolvedTenantId,
            userId: resolvedUserId,
            uploadBatchId,
            index,
          })) {
            importMode = part.normalizationMode;
            queuedParts.push(part);

            if (queuedParts.length < ACCOUNT_IMPORT_CONCURRENCY) {
              continue;
            }

            const results = await uploadQueuedParts(queuedParts.splice(0, queuedParts.length));
            for (const result of results) {
              if (!result.ok && result.authRedirected) {
                return;
              }
              if (result.ok) {
                importedChunks += 1;
                totalImportedChunks += 1;
              } else {
                failedChunks += 1;
                lastFailureDetail = result.detail;
                stopFileImport = true;
              }
            }

            setImportStatusMessage(`Importing ${file.name}... ${importedChunks} chunks ingested`);
            if (stopFileImport) {
              break;
            }
          }

          if (!stopFileImport && queuedParts.length > 0) {
            const results = await uploadQueuedParts(queuedParts.splice(0, queuedParts.length));
            for (const result of results) {
              if (!result.ok && result.authRedirected) {
                return;
              }
              if (result.ok) {
                importedChunks += 1;
                totalImportedChunks += 1;
              } else {
                failedChunks += 1;
                lastFailureDetail = result.detail;
                stopFileImport = true;
              }
            }
          }

          const modeLabel = importMode === "structured_transcript" ? "normalized transcript" : "raw chunked import";
          const status =
            failedChunks > 0
              ? importedChunks > 0
                ? "partial"
                : "failed"
              : importedChunks > 0
                ? "uploaded"
                : "failed";

          items.push({
            id: `${uploadBatchId}-import-${index}`,
            name: file.name,
            status,
            detail:
              status === "uploaded"
                ? `${importedChunks} chunks imported via ${modeLabel}.`
                : status === "partial"
                  ? `${importedChunks} chunks imported before the import stopped. ${lastFailureDetail ?? "Upload failed."}`
                  : lastFailureDetail ?? "No readable text chunks were imported.",
            importedChunks,
            failedChunks,
          });
        } catch (uploadError) {
          items.push({
            id: `${uploadBatchId}-failed-${index}`,
            name: file.name,
            status: "failed",
            detail: uploadError instanceof Error ? uploadError.message : "Unable to import this file.",
            importedChunks: 0,
            failedChunks: 0,
          });
        }
      }

      const uploadedCount = items.filter((item) => item.status === "uploaded").length;
      const partialCount = items.filter((item) => item.status === "partial").length;
      const failedCount = items.filter((item) => item.status === "failed").length;
      setImportBatch({
        id: uploadBatchId,
        createdAt: uploadedAt,
        uploadedCount,
        partialCount,
        failedCount,
        totalCount: items.length,
        importedChunks: totalImportedChunks,
        items,
      });

      if (uploadedCount === 0 && partialCount === 0) {
        setImportError("No files were imported. Review the import summary and try again.");
      } else if (failedCount > 0 || partialCount > 0) {
        setImportError("Some imports were only partially completed or failed. Review the import summary.");
      }
    } finally {
      setImportStatusMessage(null);
      setIsImporting(false);
    }
  }, [
    isOffline,
    isVisible,
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.requestTimeoutMs,
    runtime.config.tenantId,
    tenantId,
    userId,
  ]);

  const effectiveTenantId = tenantId ?? linkedNode?.tenant_id ?? runtime.config.tenantId ?? "Unavailable";
  const effectiveUserId = userId ?? linkedNode?.user_id ?? "Unavailable";
  const linkedDetails = linkedNode?.linked && linkedNode.node && linkedNode.assignment
    ? {
        assignment: linkedNode.assignment,
        matchedIdentifier: linkedNode.matched_identifier,
        matchedVia: linkedNode.matched_via,
        node: linkedNode.node,
      }
    : null;
  const isLinked = Boolean(linkedDetails);
  const nodeStatusLabel = isLinked ? "Linked" : "Not linked";
  const accountInitial = displayName.trim().charAt(0).toUpperCase() || "A";
  const authModeLabel = formatAuthModeLabel(runtime.config.authMode);
  const sessionStatusLabel = isVisible && runtime.config.authMode !== "none" ? "Signed in" : "Sign-in required";
  const linkStatusLabel = settingsState === "loading" ? "Refreshing..." : nodeStatusLabel;
  const connectedServicesCount = connectors.connections.length;

  const summaryItems: AccountDetailItem[] = [
    { label: "Tenant", value: effectiveTenantId, breakValue: effectiveTenantId !== "Unavailable" },
    { label: "User ID", value: effectiveUserId, breakValue: effectiveUserId !== "Unavailable" },
    { label: "Session", value: sessionStatusLabel },
    { label: "Home node", value: linkStatusLabel },
    { label: "Services", value: connectorsState === "loading" ? "Refreshing..." : String(connectedServicesCount) },
  ];

  const linkedNodeItems: AccountDetailItem[] = linkedDetails
    ? [
        { label: "Node ID", value: linkedDetails.node.node_id, breakValue: true },
        { label: "Node status", value: linkedDetails.node.status },
        { label: "Tailscale IP", value: linkedDetails.node.tailscale_ip || "Unavailable" },
        { label: "Owner user", value: linkedDetails.node.owner_user_id, breakValue: true },
        { label: "Assigned", value: formatDateTime(linkedDetails.assignment.assigned_utc) },
        {
          label: "Last heartbeat",
          value: linkedDetails.node.last_heartbeat_utc
            ? formatDateTime(linkedDetails.node.last_heartbeat_utc)
            : "No heartbeat reported",
        },
        {
          label: "Watcher node",
          value: linkedDetails.assignment.watcher_node_id || linkedDetails.node.watcher_node_id || "Not set",
          breakValue: true,
        },
        {
          label: "Policy scope",
          value: linkedDetails.assignment.policy_scope_id || "Default",
          breakValue: true,
        },
        { label: "Local only", value: renderBooleanLabel(linkedDetails.assignment.local_only) },
        {
          label: "Remote fallback",
          value: renderBooleanLabel(linkedDetails.assignment.remote_fallback_allowed),
        },
        ...(linkedDetails.matchedIdentifier
          ? [{
              label: "Last matched identifier",
              value: `${linkedDetails.matchedIdentifier}${linkedDetails.matchedVia ? ` (${linkedDetails.matchedVia})` : ""}`,
              breakValue: true,
            }]
          : []),
      ]
    : [];

  return (
    <Modal
      ariaLabel="Account settings"
      onClose={closeAccountSettings}
      dismissible={!isImportModalOpen && !isLinkModalOpen && !isUnlinking}
    >
    <div className="page-stack account-settings-modal">
      <div className="card-light">
        <div className="account-settings-hero">
          <div className="account-settings-profile">
            <div className="account-settings-avatar" aria-hidden="true">{accountInitial}</div>
            <div className="account-settings-copy">
              <div className="account-settings-eyebrow">Account workspace</div>
              <h2 className="account-settings-name">{displayName}</h2>
              <p className="account-settings-description">
                Manage the node linked to your GPT account. A home node can be linked by multiple users in the same tenant, and the transaction path will show when that linked node participates.
              </p>
              <div className="account-settings-chip-row">
                <span className={isLinked ? "account-settings-chip account-settings-chip--linked" : "account-settings-chip"}>
                  {settingsState === "loading" ? "Refreshing link" : isLinked ? "Home node linked" : "No home node linked"}
                </span>
                <span className="account-settings-chip">{authModeLabel}</span>
              </div>
            </div>
          </div>

          <div className="account-settings-stat-grid">
            {summaryItems.map((item) => (
              <div key={item.label} className="account-settings-stat">
                <div className="account-settings-stat-label">{item.label}</div>
                <div className={item.breakValue ? "account-settings-stat-value account-settings-stat-value--break" : "account-settings-stat-value"}>
                  {item.value}
                </div>
              </div>
            ))}
          </div>
        </div>

        {notice || linkNotice || loadError || (!isVisible || runtime.config.authMode === "none") ? (
          <div className="account-settings-feedback">
            {notice ? (
              <p className="settings-notice" role="status" aria-live="polite">
                {notice}
              </p>
            ) : null}

            {!isVisible || runtime.config.authMode === "none" ? (
              <div className="alert alert-warning" role="status">
                Account-based node linking requires a signed-in session.
              </div>
            ) : null}

            {linkNotice ? (
              <div className="alert alert-success" role="status">
                {linkNotice}
              </div>
            ) : null}

            {loadError ? (
              <div className="alert alert-error" role="alert">
                {loadError}
              </div>
            ) : null}
          </div>
        ) : null}

        <div className="account-settings-actions">
          {!isLinked ? (
            <button
              className="btn-primary"
              type="button"
              onClick={() => {
                setLinkError(null);
                setIsLinkModalOpen(true);
              }}
              disabled={runtime.config.authMode === "none" || !isVisible || settingsState === "loading" || isUnlinking}
            >
              Link My Node
            </button>
          ) : null}
          {isLinked ? (
            <button
              className="btn-secondary"
              type="button"
              onClick={() => void handleUnlinkNode()}
              disabled={runtime.config.authMode === "none" || !isVisible || settingsState === "loading" || isUnlinking || isLinking}
            >
              {isUnlinking ? "Unlinking..." : "Unlink node"}
            </button>
          ) : null}
          <button
            className="btn-secondary"
            type="button"
            onClick={openImportModal}
            disabled={runtime.config.authMode === "none" || !isVisible || isUnlinking}
          >
            Import Chat History
          </button>
          <button
            className="btn-secondary"
            type="button"
            onClick={() => void loadLinkedNode()}
            disabled={settingsState === "loading" || isUnlinking}
          >
            {settingsState === "loading" ? "Refreshing..." : "Refresh status"}
          </button>
        </div>
      </div>

      <div className="card-light">
        <div className="home-node-settings-header">
          <div>
            <h3 className="card-title-sm">Linked node</h3>
            <p className="card-text-muted-sm">
              The linked node becomes the user-scoped home-node target used for local-only or hybrid execution policies.
            </p>
          </div>
          <div className={isLinked ? "home-node-settings-pill home-node-settings-pill--linked" : "home-node-settings-pill"}>
            {settingsState === "loading" ? "Loading..." : nodeStatusLabel}
          </div>
        </div>

        {settingsState === "loading" ? (
          <p className="card-text-muted-sm">Loading account settings...</p>
        ) : null}

        {settingsState !== "loading" && !isLinked ? (
          <div className="account-settings-empty">
            <div className="account-settings-empty-title">No home node linked yet</div>
            <div className="account-settings-empty-copy">
              Open <strong>Link My Node</strong>, scan the QR code shown on your node, and confirm the link to activate user-scoped home-node routing.
            </div>
          </div>
        ) : null}

        {settingsState !== "loading" && linkedDetails ? (
          <div className="account-settings-node-grid">
            {linkedNodeItems.map((item) => (
              <div key={item.label} className="account-settings-node-card">
                <div className="account-settings-node-label">{item.label}</div>
                <div className={item.breakValue ? "account-settings-node-value account-settings-node-value--break" : "account-settings-node-value"}>
                  {item.value}
                </div>
              </div>
            ))}
          </div>
        ) : null}
      </div>

      <div className="card-light">
        <div className="home-node-settings-header">
          <div>
            <h3 className="card-title-sm">Connected services</h3>
            <p className="card-text-muted-sm">
              Service access is scoped to this account and reflected in governed Source retrieval.
            </p>
          </div>
          <div className={connectedServicesCount > 0 ? "home-node-settings-pill home-node-settings-pill--linked" : "home-node-settings-pill"}>
            {connectorsState === "loading" ? "Loading..." : `${connectedServicesCount} linked`}
          </div>
        </div>

        {connectorNotice ? (
          <div className="alert alert-success" role="status">
            {connectorNotice}
          </div>
        ) : null}

        {connectorError ? (
          <div className="alert alert-error" role="alert">
            {connectorError}
          </div>
        ) : null}

        {connectorsState === "loading" ? (
          <p className="card-text-muted-sm">Loading connected services...</p>
        ) : null}

        {connectorsState !== "loading" ? (
          <div className="connected-services-catalog" aria-label="Available services">
            {connectors.available.map((service) => (
              <div key={service.connector_type} className="connected-services-catalog-card">
                <div className="connected-services-catalog-main">
                  <div className="connected-services-name">{service.display_name}</div>
                  <div className="connected-services-meta">
                    {service.description}
                  </div>
                </div>
                <button
                  className="btn-secondary"
                  type="button"
                  onClick={() => void handleCreateConnector(service.connector_type)}
                  disabled={
                    runtime.config.authMode === "none"
                    || !isVisible
                    || connectorCreateBusyType === service.connector_type
                    || Boolean(connectorBusyId)
                  }
                >
                  {connectorCreateBusyType === service.connector_type ? "Starting..." : "Sign in"}
                </button>
              </div>
            ))}
          </div>
        ) : null}

        {connectorsState !== "loading" && connectors.connections.length === 0 ? (
          <div className="account-settings-empty">
            <div className="account-settings-empty-title">No services connected</div>
            <div className="account-settings-empty-copy">
              Sign in to a service to link it to this account.
            </div>
          </div>
        ) : null}

        {connectors.connections.length > 0 ? (
          <div className="connected-services-list" aria-label="Connected services">
            {connectors.connections.map((connection) => {
              const isBusy = connectorBusyId === connection.connection_id;
              return (
                <div key={connection.connection_id} className="connected-services-card">
                  <div className="connected-services-card-header">
                    <div>
                      <div className="connected-services-name">{connection.display_name}</div>
                      <div className="connected-services-meta">
                        {connection.connector_type.replace(/_/g, " ")}
                      </div>
                    </div>
                    <span className={connectorPillClass(connection.status)}>
                      {formatConnectorStatus(connection.status)}
                    </span>
                  </div>

                  <div className="connected-services-form-grid">
                    <div className="connected-services-field">
                      <span>Sign-in</span>
                      <div className="connected-services-readonly">
                        {connection.credential_configured ? "Configured" : "Needed"}
                      </div>
                    </div>

                    <div className="connected-services-field">
                      <span>Sync</span>
                      <div className="connected-services-readonly">
                        {renderBooleanLabel(connection.sync_enabled)}
                      </div>
                    </div>

                    <div className="connected-services-field">
                      <span>Status</span>
                      <div className="connected-services-readonly">
                        {formatConnectorSyncLabel(connection)}
                      </div>
                    </div>
                  </div>

                  {connection.last_error ? (
                    <div className="alert alert-warning" role="status">
                      {connection.last_error}
                    </div>
                  ) : null}

                  <div className="connected-services-actions">
                    {connection.status === "pending_auth" && connection.authorization_url ? (
                      <button
                        className="btn-secondary"
                        type="button"
                        onClick={() => window.location.assign(connection.authorization_url!)}
                        disabled={isBusy || connectorCreateBusyType !== null}
                      >
                        Sign in
                      </button>
                    ) : null}
                    <button
                      className="btn-secondary"
                      type="button"
                      onClick={() => void handleSyncConnector(connection)}
                      disabled={isBusy || connectorCreateBusyType !== null || connection.status !== "connected"}
                    >
                      Sync now
                    </button>
                    <button
                      className="btn-secondary"
                      type="button"
                      onClick={() => void handleDisconnectConnector(connection)}
                      disabled={isBusy || connectorCreateBusyType !== null}
                    >
                      Disconnect
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        ) : null}
      </div>

      <div className="card-light">
        <h3 className="card-title-sm">How linking works</h3>
        <ol className="account-settings-steps">
          <li className="account-settings-step">
            <div className="account-settings-step-number" aria-hidden="true">1</div>
            <div className="account-settings-step-copy">
              <div className="account-settings-step-title">Scan or paste the node QR</div>
              <div className="account-settings-step-text">
                The canonical QR payload is JSON with a node_id and identifier_version. Older labels may also include tenant_id. Raw node IDs still work, and Tailscale IP remains a legacy fallback for older labels.
              </div>
            </div>
          </li>
          <li className="account-settings-step">
            <div className="account-settings-step-number" aria-hidden="true">2</div>
            <div className="account-settings-step-copy">
              <div className="account-settings-step-title">Use the same tenant and claim if needed</div>
              <div className="account-settings-step-text">
                The first successful authenticated scan claims the node into your tenant when needed. After that, other users in the same tenant can link the same node to their own accounts.
              </div>
            </div>
          </li>
          <li className="account-settings-step">
            <div className="account-settings-step-number" aria-hidden="true">3</div>
            <div className="account-settings-step-copy">
              <div className="account-settings-step-title">Use manual entry when camera scan is unavailable</div>
              <div className="account-settings-step-text">
                If the browser cannot scan live video, paste the QR payload manually in the link dialog and confirm the same link flow.
              </div>
            </div>
          </li>
        </ol>
      </div>

      {isLinkModalOpen ? (
        <HomeNodeLinkModal
          onClose={() => {
            if (!isLinking) {
              setIsLinkModalOpen(false);
            }
          }}
          onLink={handleLinkNode}
          isLinking={isLinking}
          error={linkError}
          expectedTenantId={effectiveTenantId !== "Unavailable" ? effectiveTenantId : null}
        />
      ) : null}

      {isImportModalOpen ? (
        <Modal ariaLabel="Import chat history" onClose={closeImportModal} dismissible={!isImporting}>
          <div className="card-light-compact account-import-modal">
            <div>
              <h2 className="card-title">Import chat history</h2>
              <p className="card-subtitle">
                Upload a chat export, text-like file, image, or ZIP archive to ingest it into your account through the orchestrator pipeline.
                Files can be up to {formatFileSize(MAX_CHAT_HISTORY_IMPORT_FILE_BYTES)}.
              </p>
            </div>

            <div className="account-import-actions">
              <button
                className="btn-primary"
                type="button"
                onClick={() => accountImportInputRef.current?.click()}
                disabled={isImporting || runtime.config.authMode === "none" || !isVisible}
              >
                Choose File
              </button>
              {importBatch ? (
                <button className="btn-secondary" type="button" onClick={clearImportBatch} disabled={isImporting}>
                  Clear summary
                </button>
              ) : null}
            </div>

            <input
              ref={accountImportInputRef}
              className="home-compose-hidden-input"
              type="file"
              multiple
              accept="image/*,.json,.jsonl,.txt,.md,.markdown,.csv,.tsv,.log,.xml,.yaml,.yml,.html,.htm,.zip,application/zip,application/x-zip-compressed"
              onChange={handleAccountImport}
            />

            {isImporting ? (
              <div className="home-loading home-upload-feedback-loading" role="status" aria-live="polite" aria-label="Importing">
                <div className="home-loading-dots" aria-hidden="true">
                  <span className="home-loading-dot" />
                  <span className="home-loading-dot" />
                  <span className="home-loading-dot" />
                  <span className="home-loading-dot" />
                </div>
                <div className="card-text-muted-sm">
                  {importStatusMessage ?? "Importing through orchestrator..."}
                </div>
              </div>
            ) : null}

            {importBatch ? (
              <div className="home-upload-summary home-upload-summary--account" role="status" aria-live="polite">
                <div className="home-upload-summary-main">
                  <span className="home-upload-summary-icon" aria-hidden="true">
                    INGEST
                  </span>
                  <div className="home-upload-summary-copy">
                    <div className="home-upload-summary-title">
                      Import batch | {importBatch.uploadedCount} completed
                      {importBatch.partialCount > 0 ? ` | ${importBatch.partialCount} partial` : ""}
                      {importBatch.failedCount > 0 ? ` | ${importBatch.failedCount} failed` : ""}
                    </div>
                    <div className="home-upload-summary-meta">
                      {importBatch.importedChunks} chunks ingested | {importBatch.totalCount} files | {formatDateTime(importBatch.createdAt)}
                    </div>
                    <div className="home-upload-summary-snippet">
                      {importBatch.items.slice(0, 3).map((item) => `${item.name} (${item.importedChunks} chunks)`).join(" | ")}
                      {importBatch.items.length > 3 ? ` | +${importBatch.items.length - 3} more` : ""}
                    </div>
                  </div>
                </div>
              </div>
            ) : null}

            {importBatch?.items.length ? (
              <div className="account-import-results" role="list" aria-label="Import results">
                {importBatch.items.map((item) => (
                  <div key={item.id} className="account-import-result" role="listitem">
                    <div className="account-import-result-title">{item.name}</div>
                    <div className="account-import-result-meta">
                      {item.status} | {item.importedChunks} chunks imported
                      {item.failedChunks > 0 ? ` | ${item.failedChunks} failed` : ""}
                    </div>
                    <div className="account-import-result-detail">{item.detail}</div>
                  </div>
                ))}
              </div>
            ) : null}

            {importError ? (
              <div className="alert alert-warning" role="alert">
                {importError}
              </div>
            ) : null}

            <div className="panel-actions">
              <button className="btn-secondary" type="button" onClick={closeImportModal} disabled={isImporting}>
                Close
              </button>
            </div>
          </div>
        </Modal>
      ) : null}
    </div>
    </Modal>
  );
}
