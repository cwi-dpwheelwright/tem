"use client";

import Image from "next/image";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type ChangeEvent } from "react";
import { flushSync } from "react-dom";
import { ApiError, createApiClient, formatApiError, getApiErrorPayloadString } from "@/lib/api/client";
import type { AskHistoryAttachment, IngestDocumentRequest } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { reportError } from "@/lib/error-reporting";
import { buildQuestionWithConfidence, parseAnswerWithConfidence } from "@/lib/llm-confidence";
import { logApiError } from "@/lib/logger";
import { AUTH_NOTICE_MESSAGE, handleAuthRedirect, resolveAuthToken } from "@/lib/auth";
import { resolveRuntimeConfig } from "@/lib/config";
import { expireAuthSession, loadOverrides, loadModelTier, loadUseWeb, saveModelTier, saveUseWeb } from "@/lib/storage";
import {
  clearLocalAskHistory,
  loadAskHistorySettings,
  purgeLocalAskHistory,
  recordLocalAskHistory,
  fetchRemoteAskHistory,
  recordRemoteAskHistory,
  clearRemoteAskHistory,
  type AskHistoryItem,
} from "@/lib/history";
import { useRemoteHistoryRefresh } from "@/lib/history-sync";
import { loadSelectedPackName, saveSelectedPackName } from "@/lib/packs/selection";
import {
  findMatchingVerifiedPack,
  resolveCanonicalVerifiedPackSelection,
} from "@/lib/packs/reconcile";
import { usePackCatalogAvailability } from "@/lib/packs/use-pack-catalog-availability";
import { useLiveVerifiedPacks } from "@/lib/packs/use-live-verified-packs";
import { loadSelectedPillar, saveSelectedPillar } from "@/lib/pillars";
import { useOfflineStatus } from "@/lib/offline";
import { getScopedStorageKey } from "@/lib/session-identity";
import { CitedAnswer } from "@/components/cited-answer";
import { GlassboxInline, GlassboxPanel } from "@/components/glassbox-panel";
import { JourneyPanel } from "@/components/journey-panel";
import { Modal } from "@/components/modal";
import { NoValidPacksModal } from "@/components/no-valid-packs-modal";
import { useSessionIdentity } from "@/components/session-badge";
import ActionPage from "@/app/action/page";
import TrustPage from "@/app/trust/page";
import { TraceLookupPanel } from "@/components/trace-lookup-panel";
import { TraceDetailPanel } from "@/components/trace-detail-panel";
import { buildLocalJourneyEvent } from "@/lib/journey";
import { takeSelectedFiles } from "@/lib/file-input";
import { resolveSessionIdentity } from "@/lib/session-identity";
import {
  FEEDBACK_SEVERITIES,
  FEEDBACK_TYPES,
  buildFeedbackReportRequest,
  captureElementScreenshotDataUrl,
} from "@/lib/feedback";
import { useChatAutoScroll } from "@/hooks/useChatAutoScroll";
import { useGlobalInputFocus } from "@/hooks/useGlobalInputFocus";
import {
  MAX_ATTACHMENTS,
  buildAskQuestion,
  buildAttachmentExcerpt,
  buildChatHistoryContext,
  buildSourceContext,
  formatFileSize,
  isImageFileType,
  isTextLikeFile,
  type ComposerAttachment,
} from "@/lib/prompt-composer";

type GatewayState = "checking" | "up" | "down";
type HomeWorkspaceTab = "chats" | "sources";
type HomeModalId = "action" | "trust" | "trace" | "pack" | "core";
type ExperienceModeId = "daily_briefing" | "what_changed" | "whats_at_risk" | "make_a_plan";
type DisplayHistoryItem = AskHistoryItem & { isPending?: boolean };
type ComposerAttachmentItem = ComposerAttachment & { previewUrl?: string; previewDataUrl?: string; signature: string };
type FeedbackModalTarget = {
  item: DisplayHistoryItem;
  elementId: string;
};
type FeedbackToast = {
  message: string;
  kind: "success" | "warning" | "error";
};
type TrustedWebStatus = {
  enabled: boolean;
  default_profile?: string;
  max_results?: number;
};
type AttachmentUploadFeedback =
  | { status: "idle" }
  | { status: "loading"; message: string; fileNames: string[] }
  | { status: "notice" | "error"; title: string; message: string; detail?: string; fileNames: string[] };

const MAX_ATTACHMENT_READ_BYTES = 32 * 1024;
const MIN_ATTACHMENT_UPLOAD_MODAL_MS = 650;
const HISTORY_IMAGE_PREVIEW_MAX_EDGE = 240;
const HISTORY_IMAGE_PREVIEW_QUALITY = 0.82;
const SOURCES_ATTACHMENTS_KEY = "cwi.user.web.sources.attachments";
const HOME_NODE_DEFERRED_POLL_INTERVAL_MS = 2000;
const HOME_NODE_DEFERRED_MAX_POLLS = 60;

const EXPERIENCE_MODES: Array<{
  id: ExperienceModeId;
  label: string;
  title: string;
  prompt: string;
}> = [
  {
    id: "daily_briefing",
    label: "Daily Briefing",
    title: "Today's focus",
    prompt:
      "Create my Daily Briefing. Use what you know from governed memory and current context. Return top 3-5 priorities, max 1-2 risks, and max 3 suggestions. Keep it concise and actionable.",
  },
  {
    id: "what_changed",
    label: "What Changed",
    title: "Recent changes",
    prompt:
      "Tell me what changed since my last relevant update. Return max 3-5 changes, include why each matters, and keep it concise and actionable.",
  },
  {
    id: "whats_at_risk",
    label: "What's at Risk",
    title: "Risk check",
    prompt:
      "Tell me what's at risk right now. Return max 1-2 risks, the likely impact, and the next action. Keep it concise and actionable.",
  },
  {
    id: "make_a_plan",
    label: "Make a Plan",
    title: "Action plan",
    prompt:
      "Make a plan for what I should do next. Return top 3-5 priorities, max 1-2 risks, and max 3 suggestions. Keep it concise and actionable.",
  },
];

async function waitForBrowserPaint() {
  if (typeof window === "undefined") {
    return;
  }

  await new Promise<void>((resolve) => {
    window.requestAnimationFrame(() => {
      window.requestAnimationFrame(() => resolve());
    });
  });
}

function resolveBrowserTimezone(): string | undefined {
  if (typeof Intl === "undefined") {
    return undefined;
  }

  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || undefined;
  } catch {
    return undefined;
  }
}

function isMobileChatViewport(): boolean {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return false;
  }

  return window.matchMedia("(max-width: 1023px)").matches;
}

function buildHistoryAttachments(attachments: ComposerAttachmentItem[]): AskHistoryAttachment[] {
  return attachments.map((attachment) => ({
    name: attachment.name,
    mime_type: attachment.mimeType,
    size: attachment.size,
    kind: attachment.kind,
    excerpt: attachment.excerpt,
    truncated: attachment.truncated || undefined,
    preview_data_url: attachment.previewDataUrl,
  }));
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    window.setTimeout(resolve, ms);
  });
}

function formatAskRequestError(error: ApiError, packName?: string | null): string {
  const code = getApiErrorPayloadString(error, "code");
  const message = formatApiError(error);
  if (code === "dependency_unavailable" && /adopted manifest unavailable/i.test(message)) {
    return packName
      ? `The selected pack "${packName}" is not available through the governed resolver right now. Open Trust to refresh or choose another live verified pack.`
      : "The selected pack is not available through the governed resolver right now. Open Trust to refresh or choose another live verified pack.";
  }
  return message;
}

function loadSourceAttachments(): ComposerAttachmentItem[] {
  if (typeof window === "undefined") {
    return [];
  }

  try {
    const raw = window.localStorage.getItem(getScopedStorageKey(SOURCES_ATTACHMENTS_KEY));
    if (!raw) {
      return [];
    }

    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return [];
    }

    return parsed.flatMap((item) => {
      if (!item || typeof item !== "object") {
        return [];
      }

      const candidate = item as Partial<ComposerAttachmentItem>;
      if (!candidate.id || !candidate.name || !candidate.mimeType || !candidate.kind) {
        return [];
      }

      return [{
        id: candidate.id,
        signature: candidate.signature ?? `${candidate.name}:${candidate.size ?? 0}:${candidate.id}`,
        name: candidate.name,
        mimeType: candidate.mimeType,
        size: candidate.size ?? 0,
        kind: candidate.kind,
        excerpt: candidate.excerpt,
        truncated: candidate.truncated,
        previewDataUrl: candidate.previewDataUrl,
      }];
    });
  } catch {
    return [];
  }
}

function saveSourceAttachments(attachments: ComposerAttachmentItem[]): void {
  if (typeof window === "undefined") {
    return;
  }

  const key = getScopedStorageKey(SOURCES_ATTACHMENTS_KEY);
  if (attachments.length === 0) {
    window.localStorage.removeItem(key);
    return;
  }

  window.localStorage.setItem(key, JSON.stringify(attachments));
}

function buildStableSourceDocumentId(attachment: ComposerAttachmentItem): string {
  const slug = attachment.name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 60) || "source";
  const signatureKey = btoa(unescape(encodeURIComponent(attachment.signature)))
    .replace(/[^a-zA-Z0-9]/g, "")
    .slice(0, 24) || attachment.id.replace(/[^a-zA-Z0-9]/g, "").slice(0, 24);

  return `gpt-source-${slug}-${signatureKey}`;
}

function buildSourceIngestRequest(
  attachment: ComposerAttachmentItem,
  tenantId: string,
  userId: string,
): IngestDocumentRequest {
  const fallbackContent =
    attachment.kind === "image"
      ? `Image source uploaded to CWI GPT Sources: ${attachment.name}.`
      : `File source uploaded to CWI GPT Sources: ${attachment.name}.`;

  return {
    version: "v1",
    tenant_id: tenantId,
    user_id: userId,
    idempotency_key: `gpt-source:${attachment.signature}`,
    tags: ["gpt-source", "workspace-source"],
    document: {
      doc_id: buildStableSourceDocumentId(attachment),
      title: attachment.name,
      mime_type: attachment.mimeType,
      content: attachment.excerpt?.trim() || fallbackContent,
      content_encoding: "plain",
      source: "cwi-gpt-web-ui:sources",
      created_utc: new Date().toISOString(),
      metadata: {
        kind: attachment.kind,
        size: attachment.size,
        signature: attachment.signature,
        truncated: attachment.truncated ?? false,
        type: "gpt-source",
      },
    },
  };
}

async function buildImageHistoryPreview(file: File): Promise<string | undefined> {
  if (typeof window === "undefined") {
    return undefined;
  }

  const objectUrl = URL.createObjectURL(file);
  try {
    const image = await new Promise<HTMLImageElement>((resolve, reject) => {
      const img = new window.Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error(`Unable to preview ${file.name}.`));
      img.src = objectUrl;
    });

    const scale = Math.min(1, HISTORY_IMAGE_PREVIEW_MAX_EDGE / Math.max(image.width, image.height, 1));
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(image.width * scale));
    canvas.height = Math.max(1, Math.round(image.height * scale));
    const context = canvas.getContext("2d");
    if (!context) {
      return undefined;
    }

    context.drawImage(image, 0, 0, canvas.width, canvas.height);
    return canvas.toDataURL("image/webp", HISTORY_IMAGE_PREVIEW_QUALITY);
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

function HomeContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const modalParam = searchParams.get("modal");
  const activeModal =
    modalParam === "action"
      || modalParam === "trust"
      || modalParam === "trace"
      || modalParam === "pack"
      || modalParam === "core"
      ? modalParam
      : null;
  const activeTraceId = activeModal === "trace" ? searchParams.get("traceId") ?? "" : "";
  const runtime = resolveRuntimeConfig(loadOverrides());
  const authMode = runtime.config.authMode;
  const authIdleTimeoutMs = runtime.config.authIdleTimeoutMs;
  const requestTimeoutMs = runtime.config.requestTimeoutMs;
  const stickyChatLayoutEnabled = runtime.config.stickyChatLayoutEnabled;
  const isRemoteHistory = authMode !== "none";
  const [selectedPackName, setSelectedPackName] = useState(() => loadSelectedPackName());
  const [gatewayState, setGatewayState] = useState<GatewayState>("checking");
  const [gatewayDetail, setGatewayDetail] = useState("Checking gateway...");
  const [prompt, setPrompt] = useState("");
  const [modelTier, setModelTier] = useState(() => loadModelTier());
  const [useWeb, setUseWeb] = useState(() => loadUseWeb());
  const [trustedWebStatus, setTrustedWebStatus] = useState<TrustedWebStatus | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [historySettings, setHistorySettings] = useState(() => loadAskHistorySettings());
  const [historyItems, setHistoryItems] = useState<AskHistoryItem[]>([]);
  const [historySyncError, setHistorySyncError] = useState<string | null>(null);
  const [pendingHistoryItem, setPendingHistoryItem] = useState<AskHistoryItem | null>(null);
  const [selectedPillar, setSelectedPillar] = useState<string | null>(() => loadSelectedPillar());
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [isUserMenuOpen, setIsUserMenuOpen] = useState(false);
  const [isComposerMenuOpen, setIsComposerMenuOpen] = useState(false);
  const [promptAttachments, setPromptAttachments] = useState<ComposerAttachmentItem[]>([]);
  const [sourceAttachments, setSourceAttachments] = useState<ComposerAttachmentItem[]>(() => loadSourceAttachments());
  const [isReadingUpload, setIsReadingUpload] = useState(false);
  const [attachmentUploadFeedback, setAttachmentUploadFeedback] = useState<AttachmentUploadFeedback>({ status: "idle" });
  const [activeWorkspaceTab, setActiveWorkspaceTab] = useState<HomeWorkspaceTab>("chats");
  const [selectedExperienceMode, setSelectedExperienceMode] = useState<ExperienceModeId | null>(null);
  const [feedbackTarget, setFeedbackTarget] = useState<FeedbackModalTarget | null>(null);
  const [feedbackType, setFeedbackType] = useState<string>(FEEDBACK_TYPES[0]);
  const [feedbackSeverity, setFeedbackSeverity] = useState<string>("Medium");
  const [feedbackComment, setFeedbackComment] = useState("");
  const [feedbackIncludeScreenshot, setFeedbackIncludeScreenshot] = useState(true);
  const [feedbackSubmitting, setFeedbackSubmitting] = useState(false);
  const [feedbackToast, setFeedbackToast] = useState<FeedbackToast | null>(null);
  const [composerHeight, setComposerHeight] = useState(156);
  const { isOffline } = useOfflineStatus();
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const sourceAttachmentInputRef = useRef<HTMLInputElement>(null);
  const promptAttachmentInputRef = useRef<HTMLInputElement>(null);
  const historyBodyRef = useRef<HTMLDivElement>(null);
  const historyBottomRef = useRef<HTMLDivElement>(null);
  const composerShellRef = useRef<HTMLDivElement>(null);
  const composerMenuRef = useRef<HTMLDivElement>(null);
  const promptAttachmentsRef = useRef<ComposerAttachmentItem[]>([]);
  const sourceAttachmentsRef = useRef<ComposerAttachmentItem[]>([]);
  const requestAttachmentsRef = useRef<ComposerAttachmentItem[]>([]);
  const pendingHistoryScrollRef = useRef(false);
  const latestTrace = pendingHistoryItem?.trace ?? historyItems[0]?.trace;
  const { displayName: sessionDisplayName, handleLogout, isVisible: isSessionVisible } = useSessionIdentity();
  const handleMissingPackCatalogAuth = useCallback(() => {
    expireAuthSession(AUTH_NOTICE_MESSAGE);
    router.replace("/login");
  }, [router]);
  const { isEmpty: isPackCatalogEmpty, emptyStateSummary } = usePackCatalogAvailability({
    authMode,
    authIdleTimeoutMs,
    requestTimeoutMs,
    onMissingAuth: handleMissingPackCatalogAuth,
  });
  const {
    packs: liveVerifiedPacks,
    status: livePackStatus,
    error: livePackError,
    filteredOutPackCount: liveFilteredOutPackCount,
    refresh: refreshLiveVerifiedPacks,
  } = useLiveVerifiedPacks({
    authMode,
    authIdleTimeoutMs,
    requestTimeoutMs,
    onMissingAuth: handleMissingPackCatalogAuth,
  });
  const isTrustedWebAvailable = trustedWebStatus?.enabled === true;

  useEffect(() => {
    let active = true;
    void fetch("/api/trusted-web/status", { cache: "no-store" })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error("trusted_web_status_unavailable");
        }
        return (await response.json()) as TrustedWebStatus;
      })
      .then((status) => {
        if (active) {
          setTrustedWebStatus(status);
        }
      })
      .catch(() => {
        if (active) {
          setTrustedWebStatus({ enabled: false });
        }
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (trustedWebStatus && !trustedWebStatus.enabled && useWeb) {
      setUseWeb(false);
      saveUseWeb(false);
    }
  }, [trustedWebStatus, useWeb]);

  useEffect(() => {
    if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
      return;
    }

    const query = window.matchMedia("(max-width: 1023px)");
    const keepChatVisibleOnMobile = () => {
      if (query.matches) {
        setActiveWorkspaceTab("chats");
      }
    };

    keepChatVisibleOnMobile();
    query.addEventListener("change", keepChatVisibleOnMobile);
    return () => query.removeEventListener("change", keepChatVisibleOnMobile);
  }, []);

  const selectedVerifiedPack = useMemo(
    () => findMatchingVerifiedPack(selectedPackName, liveVerifiedPacks),
    [liveVerifiedPacks, selectedPackName],
  );
  const historyItemsForDisplay = useMemo<DisplayHistoryItem[]>(() => {
    const items = [...historyItems]
      .reverse()
      .map((item) => ({ ...item, isPending: item.responseStatus === "processing" }));
    if (!pendingHistoryItem) {
      return items;
    }

    const pendingAlreadySynced = items.some((item) => item.requestId === pendingHistoryItem.requestId);
    return pendingAlreadySynced ? items : [...items, { ...pendingHistoryItem, isPending: true }];
  }, [historyItems, pendingHistoryItem]);
  const sessionInitial = useMemo(() => {
    const trimmed = sessionDisplayName.trim();
    return trimmed ? trimmed[0].toUpperCase() : "U";
  }, [sessionDisplayName]);
  const recentChatHistoryForRequest = useMemo(() => {
    if (!historySettings.enabled) {
      return null;
    }

    const completedTurns = [...historyItems]
      .filter((item) => (item.responseStatus ?? "completed") === "completed" && item.answer.trim().length > 0)
      .sort((left, right) => new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime())
      .map((item) => ({
        question: item.question,
        answer: item.answer,
        createdAt: item.createdAt,
      }));

    return buildChatHistoryContext(prompt, completedTurns);
  }, [historyItems, historySettings.enabled, prompt]);
  const sourceContextForRequest = useMemo(
    () => buildSourceContext(`${prompt}\n${recentChatHistoryForRequest?.content ?? ""}`, sourceAttachments),
    [prompt, recentChatHistoryForRequest, sourceAttachments],
  );
  const requestAttachments = useMemo(() => [...promptAttachments], [promptAttachments]);
  const promptForRequest = useMemo(
    () => buildAskQuestion(prompt, requestAttachments, null, recentChatHistoryForRequest, sourceContextForRequest),
    [prompt, recentChatHistoryForRequest, requestAttachments, sourceContextForRequest],
  );
  const trimmedPrompt = prompt.trim();
  const hasPromptText = trimmedPrompt.length > 0;
  const hasComposerContext = promptAttachments.length > 0;
  const hasImageAttachment = promptAttachments.some((attachment) => attachment.kind === "image");
  const imageAttachments = useMemo(
    () => promptAttachments.filter((attachment) => attachment.kind === "image"),
    [promptAttachments],
  );
  const fileAttachments = useMemo(
    () => promptAttachments.filter((attachment) => attachment.kind !== "image"),
    [promptAttachments],
  );
  const isComposerBusy = loading || isReadingUpload;
  const composerPackMessage = useMemo(() => {
    if (!hasPromptText || isComposerBusy || isOffline) {
      return null;
    }
    if (!selectedPackName) {
      return "Select a live verified pack before sending.";
    }
    if (!selectedVerifiedPack) {
      return livePackStatus === "loading"
        ? "Refreshing the selected pack from the live verified catalog..."
        : "The selected pack is no longer live verified. Refresh Trust before sending.";
    }
    return null;
  }, [
    hasPromptText,
    isComposerBusy,
    isOffline,
    livePackStatus,
    selectedPackName,
    selectedVerifiedPack,
  ]);
  const sendButtonLabel = loading
    ? "Sending..."
    : isReadingUpload
      ? "Preparing..."
      : !hasPromptText
          ? "Send"
          : !selectedPackName
            ? "Select pack"
            : !selectedVerifiedPack
              ? livePackStatus === "loading"
                ? "Refreshing..."
                : "Fix trust"
              : "Send";

  const revokeAttachmentPreview = useCallback((attachment: ComposerAttachmentItem) => {
    if (attachment.previewUrl) {
      URL.revokeObjectURL(attachment.previewUrl);
    }
  }, []);

  useEffect(() => {
    promptAttachmentsRef.current = promptAttachments;
  }, [promptAttachments]);

  useEffect(() => {
    sourceAttachmentsRef.current = sourceAttachments;
    saveSourceAttachments(sourceAttachments);
  }, [sourceAttachments]);

  useEffect(() => {
    requestAttachmentsRef.current = requestAttachments;
  }, [requestAttachments]);

  useEffect(() => {
    return () => {
      [...promptAttachmentsRef.current, ...sourceAttachmentsRef.current].forEach(revokeAttachmentPreview);
    };
  }, [revokeAttachmentPreview]);

  useEffect(() => {
    if (!selectedPackName || livePackStatus !== "ready") {
      return;
    }

    const canonical = resolveCanonicalVerifiedPackSelection(selectedPackName, liveVerifiedPacks);
    if (canonical) {
      setSelectedPackName(canonical);
      saveSelectedPackName(canonical);
      return;
    }

    if (!selectedVerifiedPack) {
      const removedPack = selectedPackName;
      queueMicrotask(() => {
        setSelectedPackName(null);
        saveSelectedPackName(null);
        setError(`Selected pack "${removedPack}" is not in the live verified catalog. Choose a pack in Trust.`);
      });
    }
  }, [livePackStatus, liveVerifiedPacks, selectedPackName, selectedVerifiedPack]);

  useEffect(() => {
    if (!isComposerMenuOpen) {
      return;
    }

    const onPointerDown = (event: MouseEvent) => {
      if (!composerMenuRef.current?.contains(event.target as Node)) {
        setIsComposerMenuOpen(false);
      }
    };

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsComposerMenuOpen(false);
      }
    };

    window.addEventListener("mousedown", onPointerDown);
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("mousedown", onPointerDown);
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [isComposerMenuOpen]);

  const clearComposerContext = useCallback(() => {
    setPromptAttachments((current) => {
      current.forEach(revokeAttachmentPreview);
      return [];
    });
    if (promptAttachmentInputRef.current) {
      promptAttachmentInputRef.current.value = "";
    }
  }, [revokeAttachmentPreview]);

  const removePromptAttachment = useCallback((attachmentId: string) => {
    setPromptAttachments((current) => {
      const next = current.filter((attachment) => attachment.id !== attachmentId);
      const removed = current.find((attachment) => attachment.id === attachmentId);
      if (removed) {
        revokeAttachmentPreview(removed);
      }
      return next;
    });
  }, [revokeAttachmentPreview]);

  const removeSourceAttachment = useCallback((attachmentId: string) => {
    setSourceAttachments((current) => {
      const next = current.filter((attachment) => attachment.id !== attachmentId);
      const removed = current.find((attachment) => attachment.id === attachmentId);
      if (removed) {
        revokeAttachmentPreview(removed);
      }
      return next;
    });
  }, [revokeAttachmentPreview]);

  const closeAttachmentUploadFeedback = useCallback(() => {
    setAttachmentUploadFeedback({ status: "idle" });
  }, []);

  const toggleComposerMenu = useCallback(() => {
    setIsComposerMenuOpen((prev) => !prev);
  }, []);

  const openAttachmentPicker = useCallback(() => {
    setIsComposerMenuOpen(false);
    if (!isMobileChatViewport()) {
      setActiveWorkspaceTab("sources");
    }
    sourceAttachmentInputRef.current?.click();
  }, []);

  const openComposerAttachmentPicker = useCallback(() => {
    setIsComposerMenuOpen(false);
    promptAttachmentInputRef.current?.click();
  }, []);

  const resolveSessionRequestContext = useCallback(async () => {
    let token: string | null = null;
    if (runtime.config.authMode !== "none") {
      token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        const notice =
          runtime.config.authMode === "oidc"
            ? "Sign in with Identity to continue."
            : "Authentication token required. Contact your administrator.";
        expireAuthSession(notice);
        router.replace("/login");
        return null;
      }
    }

    const identity = resolveSessionIdentity({
      authMode: runtime.config.authMode,
      tenantId: runtime.config.tenantId,
      runtimeUserId: runtime.config.userId,
      token,
    });
    const tenantId = identity.tenantId;
    if (!tenantId) {
      setError("Tenant configuration is missing. Contact your administrator.");
      return null;
    }

    const userId = identity.userId;
    if (!userId) {
      setError("User identity is missing from the authenticated session.");
      return null;
    }

    return { token, tenantId, userId };
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.tenantId,
    runtime.config.userId,
  ]);

  const readAttachment = useCallback(async (file: File): Promise<ComposerAttachmentItem> => {
    const mimeType = file.type || "application/octet-stream";
    const isImage = isImageFileType(mimeType);
    let excerpt: string | undefined;
    let truncated = false;

    if (isTextLikeFile(file.name, mimeType)) {
      const rawText = await file.slice(0, MAX_ATTACHMENT_READ_BYTES).text();
      if (rawText.trim()) {
        const result = buildAttachmentExcerpt(rawText);
        excerpt = result.excerpt;
        truncated = result.truncated || file.size > MAX_ATTACHMENT_READ_BYTES;
      }
    }

    return {
      id: crypto.randomUUID(),
      signature: `${file.name}:${file.size}:${file.lastModified}`,
      name: file.name,
      mimeType,
      size: file.size,
      kind: isImage ? "image" : "file",
      excerpt,
      truncated,
      previewUrl: isImage ? URL.createObjectURL(file) : undefined,
      previewDataUrl: isImage ? await buildImageHistoryPreview(file) : undefined,
    };
  }, []);

  const addFilesToBucket = useCallback(async (
    event: ChangeEvent<HTMLInputElement>,
    target: "prompt" | "sources",
  ) => {
      const selectedFiles = takeSelectedFiles(event.currentTarget);
      if (selectedFiles.length === 0) {
        return;
      }
      if (target === "sources" && !isMobileChatViewport()) {
        setActiveWorkspaceTab("sources");
      }
      flushSync(() => {
        setIsReadingUpload(true);
        setError(null);
        setAttachmentUploadFeedback({
          status: "loading",
          message:
            target === "prompt"
              ? selectedFiles.length === 1
                ? `Preparing ${selectedFiles[0].name} for this prompt.`
                : `Preparing ${selectedFiles.length} files/images for this prompt.`
              : selectedFiles.length === 1
                ? `Preparing ${selectedFiles[0].name} for Sources.`
                : `Preparing ${selectedFiles.length} files/images for Sources.`,
          fileNames: selectedFiles.map((file) => file.name),
        });
      });
      await waitForBrowserPaint();
      const uploadStartedAt = Date.now();
  
      try {
        const settledAttachments = await Promise.allSettled(selectedFiles.map((file) => readAttachment(file)));
      const nextAttachments = settledAttachments.flatMap((result) =>
        result.status === "fulfilled" ? [result.value] : [],
      );
      const failedUploads = settledAttachments.flatMap((result, index) =>
        result.status === "rejected"
          ? [
              {
                name: selectedFiles[index]?.name ?? `file-${index + 1}`,
                reason: result.reason instanceof Error ? result.reason.message : "Unable to process this upload.",
              },
            ]
          : [],
      );
      let addedCount = 0;
      let replacedCount = 0;
      let skippedCount = 0;
      const acceptedSourceAttachments: ComposerAttachmentItem[] = [];

      const merged = [...(target === "sources" ? sourceAttachmentsRef.current : promptAttachmentsRef.current)];
      for (const attachment of nextAttachments) {
        const existingIndex = merged.findIndex((item) => item.signature === attachment.signature);
        if (existingIndex >= 0) {
          revokeAttachmentPreview(merged[existingIndex]);
          merged[existingIndex] = attachment;
          replacedCount += 1;
          if (target === "sources") {
            acceptedSourceAttachments.push(attachment);
          }
          continue;
        }
        if (merged.length >= MAX_ATTACHMENTS) {
          revokeAttachmentPreview(attachment);
          skippedCount += 1;
          continue;
        }
        merged.push(attachment);
        addedCount += 1;
        if (target === "sources") {
          acceptedSourceAttachments.push(attachment);
        }
      }

      if (target === "sources") {
        setSourceAttachments(merged);
      } else {
        setPromptAttachments(merged);
      }

      const sourceIngestFailures: string[] = [];
      if (target === "sources" && acceptedSourceAttachments.length > 0) {
        if (isOffline) {
          sourceIngestFailures.push("Offline mode: Sources were saved locally but were not indexed for retrieval.");
        } else {
          const sessionContext = await resolveSessionRequestContext();
          if (!sessionContext) {
            return;
          }

          const client = createApiClient({
            baseUrl: "",
            token: sessionContext.token,
            requestTimeoutMs,
            modelTier,
          });
          const ingestResults = await Promise.allSettled(
            acceptedSourceAttachments.map((attachment) =>
              client.ingestDocument(
                buildSourceIngestRequest(attachment, sessionContext.tenantId, sessionContext.userId),
                { requestId: `gpt-source-${attachment.id}` },
              ),
            ),
          );

          for (const [index, result] of ingestResults.entries()) {
            if (result.status === "fulfilled") {
              continue;
            }

            const attachment = acceptedSourceAttachments[index];
            const detail = result.reason instanceof ApiError ? formatApiError(result.reason) : "Indexing failed.";
            sourceIngestFailures.push(`${attachment?.name ?? "Source"}: ${detail}`);
            if (result.reason instanceof ApiError) {
              logApiError(result.reason);
            }
          }
        }
      }

      const elapsedMs = Date.now() - uploadStartedAt;
      if (elapsedMs < MIN_ATTACHMENT_UPLOAD_MODAL_MS) {
        await new Promise((resolve) => window.setTimeout(resolve, MIN_ATTACHMENT_UPLOAD_MODAL_MS - elapsedMs));
      }

      if (failedUploads.length > 0) {
        const firstFailure = failedUploads[0];
        const failureSummary =
          failedUploads.length === 1
            ? `We couldn't prepare ${firstFailure.name}.`
            : `We couldn't prepare ${failedUploads.length} files/images.`;
        const partialSummary =
          addedCount + replacedCount > 0
            ? `Ready: ${addedCount + replacedCount} file/image${addedCount + replacedCount === 1 ? "" : "s"} were added to the composer.`
            : undefined;

        void reportError({
          category: "ui",
          summary: "attachment_upload.failed",
          detail: `${failureSummary} ${firstFailure.reason}`,
        });
        setError(failureSummary);
        setAttachmentUploadFeedback({
          status: "error",
          title: "Upload failed",
          message: failureSummary,
          detail: [firstFailure.reason, partialSummary].filter(Boolean).join(" "),
          fileNames: failedUploads.map((upload) => upload.name),
        });
        return;
      }

      if (skippedCount > 0) {
        setError(`You can attach up to ${MAX_ATTACHMENTS} files or images per prompt.`);
        setAttachmentUploadFeedback({
          status: "notice",
          title: "Some uploads were skipped",
          message: `Added ${addedCount + replacedCount} file/image${addedCount + replacedCount === 1 ? "" : "s"} to the composer.`,
        detail: `${skippedCount} upload${skippedCount === 1 ? "" : "s"} were skipped because the composer limit is ${MAX_ATTACHMENTS}.`,
          fileNames: selectedFiles.map((file) => file.name),
        });
        return;
      }

      if (sourceIngestFailures.length > 0) {
        setError("Sources were saved locally, but some could not be indexed for backend retrieval.");
        setAttachmentUploadFeedback({
          status: "notice",
          title: "Sources saved with indexing warnings",
          message: `Saved ${addedCount + replacedCount} source${addedCount + replacedCount === 1 ? "" : "s"} locally.`,
          detail: sourceIngestFailures[0],
          fileNames: acceptedSourceAttachments.map((attachment) => attachment.name),
        });
        return;
      }

      setAttachmentUploadFeedback({ status: "idle" });
      inputRef.current?.focus();
    } catch (uploadError) {
      const message = uploadError instanceof Error ? uploadError.message : "Unable to add the selected files.";
      void reportError({
        category: "ui",
        summary: "attachment_upload.unexpected_error",
        detail: message,
      });
      setError(message);
      setAttachmentUploadFeedback({
        status: "error",
        title: "Upload failed",
        message: "We couldn't prepare your selected files/images.",
        detail: message,
        fileNames: selectedFiles.map((file) => file.name),
      });
    } finally {
      setIsReadingUpload(false);
    }
  }, [
    isOffline,
    modelTier,
    readAttachment,
    requestTimeoutMs,
    resolveSessionRequestContext,
    revokeAttachmentPreview,
  ]);

  const onAddPromptFiles = useCallback((event: ChangeEvent<HTMLInputElement>) => {
    void addFilesToBucket(event, "prompt");
  }, [addFilesToBucket]);

  const onAddSourceFiles = useCallback((event: ChangeEvent<HTMLInputElement>) => {
    void addFilesToBucket(event, "sources");
  }, [addFilesToBucket]);

  const openModal = useCallback(
    (modal: HomeModalId, traceId?: string) => {
      const params = new URLSearchParams(searchParams.toString());
      params.set("modal", modal);
      if (modal === "trace" && traceId) {
        params.set("traceId", traceId);
      } else {
        params.delete("traceId");
      }
      const query = params.toString();
      setIsMobileMenuOpen(false);
      router.push(query ? `/?${query}` : "/");
    },
    [router, searchParams, setIsMobileMenuOpen],
  );

  const closeModal = useCallback(() => {
    const params = new URLSearchParams(searchParams.toString());
    params.delete("modal");
    params.delete("traceId");
    const query = params.toString();
    router.push(query ? `/?${query}` : "/");
  }, [router, searchParams]);

  const showFeedbackToast = useCallback((message: string, kind: FeedbackToast["kind"]) => {
    setFeedbackToast({ message, kind });
    window.setTimeout(() => {
      setFeedbackToast((current) => (current?.message === message ? null : current));
    }, 5000);
  }, []);

  const closeFeedbackModal = useCallback(() => {
    if (feedbackSubmitting) {
      return;
    }

    setFeedbackTarget(null);
    setFeedbackType(FEEDBACK_TYPES[0]);
    setFeedbackSeverity("Medium");
    setFeedbackComment("");
    setFeedbackIncludeScreenshot(true);
  }, [feedbackSubmitting]);

  const openFeedbackModal = useCallback((item: DisplayHistoryItem, elementId: string) => {
    setFeedbackTarget({ item, elementId });
    setFeedbackType(FEEDBACK_TYPES[0]);
    setFeedbackSeverity("Medium");
    setFeedbackComment("");
    setFeedbackIncludeScreenshot(true);
  }, []);

  const submitAnswerFeedback = useCallback(async (
    item: DisplayHistoryItem,
    elementId: string,
    type: string,
    severity: string,
    comment: string,
    includeScreenshot: boolean,
  ) => {
    if (item.isPending) {
      return;
    }

    const sessionContext = await resolveSessionRequestContext();
    if (!sessionContext) {
      return;
    }

    setFeedbackSubmitting(true);
    let screenshotDataUrl: string | null = null;
    let screenshotCaptureFailed = false;
    if (includeScreenshot) {
      try {
        const element = document.getElementById(elementId);
        screenshotDataUrl = element ? await captureElementScreenshotDataUrl(element) : null;
        screenshotCaptureFailed = !screenshotDataUrl;
      } catch {
        screenshotCaptureFailed = true;
      }
    }

    try {
      const client = createApiClient({
        baseUrl: "",
        token: sessionContext.token,
        requestTimeoutMs,
        modelTier,
      });
      const requestId = crypto.randomUUID();
      const payload = buildFeedbackReportRequest(
        {
          tenantId: sessionContext.tenantId,
          userId: sessionContext.userId,
          question: item.question,
          answer: item.answer,
          requestId: item.requestId ?? item.trace?.request_id,
          traceId: item.traceId ?? item.trace?.trace_id,
          selectedPackId: item.packName ?? selectedPackName,
          selectedPackVersion: item.trace?.pack_version,
          selectedPackDomain: item.packName ?? selectedPackName,
          trace: item.trace,
          uiPage: "gpt-home",
          frontendAppVersion: runtime.config.buildVersion,
        },
        {
          type,
          severity,
          comment: comment.trim() || null,
        },
        screenshotDataUrl,
        includeScreenshot,
      );

      const response = await client.feedbackReport(payload, { requestId });
      setFeedbackTarget(null);
      if (screenshotCaptureFailed || (includeScreenshot && !response.screenshot_stored)) {
        showFeedbackToast("Feedback submitted, but screenshot could not be attached.", "warning");
      } else {
        showFeedbackToast("Thanks - feedback submitted.", "success");
      }
    } catch (err) {
      if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, authMode)) {
        return;
      }
      if (err instanceof ApiError) {
        logApiError(err);
      }
      showFeedbackToast("Feedback could not be submitted. Please try again.", "error");
    } finally {
      setFeedbackSubmitting(false);
    }
  }, [
    authMode,
    modelTier,
    requestTimeoutMs,
    resolveSessionRequestContext,
    router,
    runtime.config.buildVersion,
    selectedPackName,
    showFeedbackToast,
  ]);

  useEffect(() => {
    if (activeModal || isPackCatalogEmpty || feedbackTarget) {
      document.body.classList.add("modal-open");
      return () => document.body.classList.remove("modal-open");
    }
    document.body.classList.remove("modal-open");
  }, [activeModal, feedbackTarget, isPackCatalogEmpty]);

  useEffect(() => {
    if (activeModal || isPackCatalogEmpty) {
      setIsMobileMenuOpen(false);
    }
  }, [activeModal, isPackCatalogEmpty]);

  useEffect(() => {
    if (isMobileMenuOpen) {
      document.body.classList.add("home-menu-open");
      return () => {
        document.body.classList.remove("home-menu-open");
      };
    }
    document.body.classList.remove("home-menu-open");
  }, [isMobileMenuOpen]);

  useEffect(() => {
    if (!isMobileMenuOpen) {
      setIsUserMenuOpen(false);
    }
  }, [isMobileMenuOpen]);

  const toggleUserMenu = useCallback(() => {
    setIsUserMenuOpen((prev) => !prev);
  }, []);

  const openPreferences = useCallback(() => {
    setIsUserMenuOpen(false);
    setIsMobileMenuOpen(false);
    router.push("/settings");
  }, [router]);

  const handleMobileLogout = useCallback(async () => {
    setIsUserMenuOpen(false);
    setIsMobileMenuOpen(false);
    await handleLogout();
  }, [handleLogout]);

  useEffect(() => {
    if (!isMobileMenuOpen) {
      return;
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsMobileMenuOpen(false);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [isMobileMenuOpen]);

  useEffect(() => {
    setSelectedPackName(loadSelectedPackName());
  }, [activeModal]);

  useEffect(() => {
    if (activeModal === "trust" || activeModal === "pack") {
      void refreshLiveVerifiedPacks();
    }
  }, [activeModal, refreshLiveVerifiedPacks]);

  const pillarGroups = useMemo(() => {
    if (liveVerifiedPacks.length === 0) {
      return [] as { packName: string; packLabel: string; packVersion: string; pillars: string[] }[];
    }
    const fallbackPillar = "General (default)";
    return liveVerifiedPacks
      .map((pack) => {
        const seen = new Set<string>();
        const pillars: string[] = [];
        for (const packModule of pack.modules ?? []) {
          const label = (packModule.title ?? packModule.surface ?? packModule.id ?? "").trim();
          if (!label || seen.has(label)) {
            continue;
          }
          seen.add(label);
          pillars.push(label);
        }
        if (pillars.length === 0) {
          pillars.push(fallbackPillar);
        }
        return {
          packName: pack.name,
          packLabel: pack.displayName ?? pack.name,
          packVersion: pack.version,
          pillars,
        };
      })
      .filter((group) => group.pillars.length > 0);
  }, [liveVerifiedPacks]);

  const packOptions = useMemo(() => {
    if (liveVerifiedPacks.length === 0) {
      return [];
    }
    return [...liveVerifiedPacks].sort((a, b) => {
      const left = (a.displayName ?? a.name).toLowerCase();
      const right = (b.displayName ?? b.name).toLowerCase();
      return left.localeCompare(right);
    });
  }, [liveVerifiedPacks]);

  const pillarKeys = useMemo(() => {
    const keys: string[] = [];
    for (const group of pillarGroups) {
      for (const pillar of group.pillars) {
        keys.push(`${group.packName}::${pillar}`);
      }
    }
    return keys;
  }, [pillarGroups]);

  useEffect(() => {
    if (pillarKeys.length === 0) {
      if (selectedPillar) {
        setSelectedPillar(null);
        saveSelectedPillar(null);
      }
      return;
    }
    if (selectedPillar && pillarKeys.includes(selectedPillar)) {
      return;
    }
    const next = pillarKeys[0];
    setSelectedPillar(next);
    saveSelectedPillar(next);
  }, [pillarKeys, selectedPillar]);

  const onSelectPillar = (packName: string, pillar: string) => {
    const key = `${packName}::${pillar}`;
    setSelectedPillar(key);
    saveSelectedPillar(key);
    if (packName !== selectedPackName) {
      setSelectedPackName(packName);
      saveSelectedPackName(packName);
    }
    setIsMobileMenuOpen(false);
  };

  const onSelectPack = useCallback(
    (packName: string) => {
      setSelectedPackName(packName);
      saveSelectedPackName(packName);
      const group = pillarGroups.find((entry) => entry.packName === packName);
      if (group && group.pillars.length > 0) {
        const nextKey = `${packName}::${group.pillars[0]}`;
        setSelectedPillar(nextKey);
        saveSelectedPillar(nextKey);
      }
      setIsMobileMenuOpen(false);
      closeModal();
    },
    [pillarGroups, closeModal],
  );

  const selectedPillarLabel = useMemo(() => {
    if (!selectedPillar) {
      return "None";
    }
    const parts = selectedPillar.split("::");
    if (parts.length >= 2) {
      return `${parts[1]} (${parts[0]})`;
    }
    return selectedPillar;
  }, [selectedPillar]);

  const pingGateway = useCallback(async () => {
    setGatewayState("checking");
    setGatewayDetail("Checking gateway...");
    try {
      const response = await fetch("/api/health", { cache: "no-store" });
      if (!response.ok) {
        setGatewayState("down");
        setGatewayDetail("Gateway probe failed.");
        return;
      }
      const payload = (await response.json()) as {
        ok?: boolean;
        services?: Record<string, string>;
        checks?: { api?: { status?: string; detail?: string } };
      };
      if (payload.ok === true && payload.services) {
        setGatewayState("up");
        setGatewayDetail("Live gateway reachable.");
        return;
      }
      const apiStatus = payload.checks?.api?.status ?? "down";
      if (apiStatus === "ok") {
        setGatewayState("up");
        setGatewayDetail("Live gateway reachable.");
      } else {
        setGatewayState("down");
        setGatewayDetail(payload.checks?.api?.detail ?? "Gateway unavailable.");
      }
    } catch (err) {
      setGatewayState("down");
      setGatewayDetail(err instanceof Error ? err.message : "Gateway unavailable.");
    }
  }, []);

  useEffect(() => {
    void pingGateway();
    const intervalId = window.setInterval(() => {
      void pingGateway();
    }, 15000);

    const reping = () => {
      void pingGateway();
    };

    window.addEventListener("focus", reping);
    window.addEventListener("online", reping);

    return () => {
      window.clearInterval(intervalId);
      window.removeEventListener("focus", reping);
      window.removeEventListener("online", reping);
    };
  }, [pingGateway]);

  const refreshHistory = useCallback(async () => {
    const settings = loadAskHistorySettings();
    setHistorySettings(settings);

    if (!settings.enabled) {
      setHistoryItems([]);
      setHistorySyncError(null);
      return;
    }

    if (!isRemoteHistory) {
      setHistoryItems(purgeLocalAskHistory(settings));
      setHistorySyncError(null);
      return;
    }

    try {
      const token = await resolveAuthToken(authMode, authIdleTimeoutMs);
      if (!token) {
        expireAuthSession(AUTH_NOTICE_MESSAGE);
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs,
      });

      const items = await fetchRemoteAskHistory(client, settings);
      setHistoryItems(items);
      setHistorySyncError(null);
    } catch (err) {
      if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, authMode)) {
        return;
      }
      if (err instanceof ApiError) {
        logApiError(err);
      }
      setHistorySyncError("Shared ask history is temporarily unavailable. Reconnect or refresh to sync across sessions.");
    }
  }, [authIdleTimeoutMs, authMode, isRemoteHistory, requestTimeoutMs, router]);

  useRemoteHistoryRefresh({
    enabled: historySettings.enabled,
    isRemoteHistory,
    refresh: refreshHistory,
  });

  useEffect(() => {
    void refreshHistory();
    const onStorage = (event: StorageEvent) => {
      if (!event.key || event.key.startsWith("cwi.user.web.ask.history")) {
        void refreshHistory();
      }
      if (!event.key || event.key.includes("cwi.user.web.pack.selected")) {
        setSelectedPackName(loadSelectedPackName());
        void refreshLiveVerifiedPacks();
      }
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, [refreshHistory, refreshLiveVerifiedPacks]);

  useEffect(() => {
    if (stickyChatLayoutEnabled) {
      return;
    }
    if (!pendingHistoryScrollRef.current) {
      return;
    }
    const container = historyBodyRef.current;
    if (!container) {
      pendingHistoryScrollRef.current = false;
      return;
    }
    pendingHistoryScrollRef.current = false;
    requestAnimationFrame(() => {
      container.scrollTo({ top: container.scrollHeight, behavior: "smooth" });
    });
  }, [historyItemsForDisplay.length, stickyChatLayoutEnabled]);

  useEffect(() => {
    if (!stickyChatLayoutEnabled || activeWorkspaceTab !== "chats") {
      return;
    }

    const element = composerShellRef.current;
    if (!element) {
      return;
    }

    const updateComposerHeight = () => {
      setComposerHeight(Math.ceil(element.getBoundingClientRect().height || 156));
    };

    updateComposerHeight();
    if (typeof ResizeObserver === "undefined") {
      window.addEventListener("resize", updateComposerHeight);
      return () => window.removeEventListener("resize", updateComposerHeight);
    }

    const observer = new ResizeObserver(updateComposerHeight);
    observer.observe(element);
    return () => observer.disconnect();
  }, [activeWorkspaceTab, stickyChatLayoutEnabled]);

  const onClearHistory = async () => {
    if (!isRemoteHistory) {
      clearLocalAskHistory();
      void refreshHistory();
      return;
    }

    try {
      const token = await resolveAuthToken(authMode, authIdleTimeoutMs);
      if (!token) {
        expireAuthSession(AUTH_NOTICE_MESSAGE);
        router.replace("/login");
        return;
      }

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs,
      });

      await clearRemoteAskHistory(client);
      setHistorySyncError(null);
      void refreshHistory();
    } catch (err) {
      if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, authMode)) {
        return;
      }
      if (err instanceof ApiError) {
        logApiError(err);
      }
      setHistorySyncError("Unable to clear shared ask history right now.");
    }
  };

  const toggleMobileMenu = () => {
    setIsMobileMenuOpen((prev) => !prev);
  };

  const closeMobileMenu = () => {
    setIsMobileMenuOpen(false);
  };

  const focusAskInput = () => {
    setIsMobileMenuOpen(false);
    setIsComposerMenuOpen(false);
    try {
      inputRef.current?.focus({ preventScroll: stickyChatLayoutEnabled });
    } catch {
      inputRef.current?.focus();
    }
    if (!stickyChatLayoutEnabled) {
      inputRef.current?.scrollIntoView({ behavior: "smooth", block: "center" });
    }
  };

  const applyExperienceMode = useCallback((modeId: ExperienceModeId) => {
    const mode = EXPERIENCE_MODES.find((item) => item.id === modeId);
    if (!mode) {
      return;
    }

    setSelectedExperienceMode(modeId);
    setActiveWorkspaceTab("chats");
    setPrompt(mode.prompt);
    setError(null);
    setIsMobileMenuOpen(false);
    setIsComposerMenuOpen(false);
    requestAnimationFrame(() => {
      try {
        inputRef.current?.focus({ preventScroll: stickyChatLayoutEnabled });
      } catch {
        inputRef.current?.focus();
      }
      if (!stickyChatLayoutEnabled) {
        inputRef.current?.scrollIntoView({ behavior: "smooth", block: "center" });
      }
    });
  }, [stickyChatLayoutEnabled]);

  const setPromptValue = useCallback((value: string) => {
    setPrompt(value);
    setSelectedExperienceMode(null);
  }, []);

  const onPromptChange = useCallback((event: ChangeEvent<HTMLTextAreaElement>) => {
    setPromptValue(event.target.value);
  }, [setPromptValue]);

  const onSend = async () => {
    const trimmed = trimmedPrompt;
    if (!trimmed) {
      return;
    }
    const questionWithConfidence = buildQuestionWithConfidence(promptForRequest);
    setError(null);
    setIsComposerMenuOpen(false);

    if (isOffline) {
      setError("Offline mode: reconnect to submit questions.");
      return;
    }
    if (!selectedPackName) {
      setError("Select a pack in Trust before asking a question.");
      return;
    }
    if (!selectedVerifiedPack) {
      setError(
        livePackStatus === "loading"
          ? "Loading the selected pack from the live verified catalog. Please try again in a moment."
          : "Selected pack is not available in the live verified catalog. Open Trust to refresh.",
      );
      return;
    }
    const sessionContext = await resolveSessionRequestContext();
    if (!sessionContext) {
      return;
    }

    const client = createApiClient({
      baseUrl: "",
      token: sessionContext.token,
      requestTimeoutMs: runtime.config.requestTimeoutMs,
      modelTier,
    });
    const requestId = crypto.randomUUID();

    const historyAttachments = buildHistoryAttachments(requestAttachmentsRef.current);
    const pendingItem: AskHistoryItem = {
      id: `pending-${Date.now()}`,
      requestId,
      question: trimmed,
      answer: "",
      responseStatus: "processing",
      createdAt: new Date().toISOString(),
      packName: selectedPackName,
      packHash: selectedVerifiedPack.hash,
      trace: null,
      attachments: historyAttachments,
      journeySeed: [
        buildLocalJourneyEvent(requestId, "ui", "request_submitted", "Question submitted from GPT UI."),
        buildLocalJourneyEvent(requestId, "gateway", "request_sent", "Forwarding request to gateway."),
      ],
    };

    if (historySettings.enabled) {
      setPendingHistoryItem(pendingItem);
      pendingHistoryScrollRef.current = true;
    }

    setLoading(true);
    try {
      const data = await client.ask({
        tenant_id: sessionContext.tenantId,
        user_id: sessionContext.userId,
        question: questionWithConfidence,
        pack: { name: selectedPackName, anchor: selectedVerifiedPack.hash },
        context: {
          mode: runtime.config.policyMode,
          top_k: runtime.config.topK,
          timezone: resolveBrowserTimezone(),
          ...(useWeb && isTrustedWebAvailable
            ? {
                web_retrieval: {
                  enabled: true,
                  mode: "include" as const,
                  profile: trustedWebStatus?.default_profile,
                  max_results: trustedWebStatus?.max_results,
                },
              }
            : {}),
        },
      }, { requestId });
      const answerMarkdown = data.answer_markdown ?? data.answer;
      const parsed = parseAnswerWithConfidence(answerMarkdown);
      const sources = data.trace?.sources
        ?? (parsed.sourcesCount !== null ? Array.from({ length: parsed.sourcesCount }, () => "") : []);
      const trace = {
        ...data.trace,
        execution_state: data.execution_state ?? data.trace?.execution_state,
        execution_target: data.execution_target ?? data.trace?.execution_target,
        deferred_work_id: data.deferred_work_id ?? data.trace?.deferred_work_id,
        home_node_status: data.home_node_status ?? data.trace?.home_node_status,
        execution_message: data.execution_message ?? data.trace?.execution_message,
        confidence: parsed.confidence ?? data.trace?.confidence ?? 0,
        sources,
      };
      const deferredHomeNodeWork =
        data.execution_state === "pending" &&
        data.execution_target === "home_node" &&
        Boolean(data.deferred_work_id);

      if (deferredHomeNodeWork) {
        const deferredWorkId = data.deferred_work_id!;
        const deferredItem: AskHistoryItem = {
          ...pendingItem,
          responseStatus: "processing",
          trace,
          traceId: trace?.trace_id,
          journeySeed: [
            ...(pendingItem.journeySeed ?? []),
            buildLocalJourneyEvent(
              requestId,
              "home-node",
              "local_execution_pending",
              data.execution_message ?? "Home node accepted the request and is still processing.",
            ),
          ],
        };

        if (historySettings.enabled) {
          setPendingHistoryItem(deferredItem);
          pendingHistoryScrollRef.current = true;
        }
        setPrompt("");
        setSelectedExperienceMode(null);
        clearComposerContext();
        void (async () => {
          for (let attempt = 0; attempt < HOME_NODE_DEFERRED_MAX_POLLS; attempt += 1) {
            await delay(HOME_NODE_DEFERRED_POLL_INTERVAL_MS);

            const deferred = await client.askDeferred(deferredWorkId, { requestId });
            const deferredState = deferred.execution_state ?? deferred.trace?.execution_state;
            if (deferredState !== "completed") {
              if (deferredState === "failed" || deferredState === "denied") {
                setPendingHistoryItem((current) =>
                  current?.requestId === requestId
                    ? {
                        ...current,
                        responseStatus: "failed",
                        answer: deferred.execution_message ?? "Home-node execution did not complete.",
                        trace: {
                          ...trace,
                          ...(deferred.trace ?? {}),
                          execution_state: deferredState,
                          execution_target: deferred.execution_target ?? deferred.trace?.execution_target ?? "home_node",
                          deferred_work_id: deferred.deferred_work_id ?? deferredWorkId,
                          home_node_status: deferred.home_node_status ?? deferred.trace?.home_node_status,
                        },
                      }
                    : current,
                );
                return;
              }
              continue;
            }

            const deferredAnswerMarkdown = deferred.answer_markdown ?? deferred.answer;
            const parsedDeferred = parseAnswerWithConfidence(deferredAnswerMarkdown);
            const deferredSources = deferred.trace?.sources ?? trace?.sources ?? [];
            const resolvedTrace = {
              ...trace,
              ...(deferred.trace ?? {}),
              execution_state: deferred.execution_state ?? deferred.trace?.execution_state ?? "completed",
              execution_target: deferred.execution_target ?? deferred.trace?.execution_target ?? "home_node",
              deferred_work_id: deferred.deferred_work_id ?? deferredWorkId,
              home_node_status: deferred.home_node_status ?? deferred.trace?.home_node_status,
              execution_message: deferred.execution_message ?? deferred.trace?.execution_message,
              confidence: parsedDeferred.confidence ?? deferred.trace?.confidence ?? trace?.confidence ?? 0,
              sources: deferredSources,
            };
            const historyPayload = {
              requestId,
              question: trimmed,
              answer: parsedDeferred.answer,
              responseStatus: "completed",
              traceId: resolvedTrace?.trace_id,
              packName: selectedPackName,
              packHash: selectedVerifiedPack.hash,
              trace: resolvedTrace,
              citations: deferred.citations ?? [],
              attachments: historyAttachments,
            };
            const resolvedHistoryItem: AskHistoryItem = {
              id: pendingItem.id,
              createdAt: pendingItem.createdAt,
              ...historyPayload,
              journeySeed: [
                ...(deferredItem.journeySeed ?? []),
                buildLocalJourneyEvent(requestId, "ui", "response_received", "Home-node response received in GPT UI."),
              ],
            };

            if (historySettings.enabled) {
              if (!isRemoteHistory) {
                recordLocalAskHistory(historyPayload);
                setHistoryItems((prev) => [resolvedHistoryItem, ...prev].slice(0, Math.max(0, historySettings.maxEntries)));
              } else {
                const recordedItem = await recordRemoteAskHistory(client, historyPayload, historySettings);
                const nextHistoryItem = recordedItem
                  ? {
                      ...recordedItem,
                      citations: resolvedHistoryItem.citations,
                      attachments: recordedItem.attachments?.length ? recordedItem.attachments : resolvedHistoryItem.attachments,
                      journeySeed: resolvedHistoryItem.journeySeed,
                    }
                  : resolvedHistoryItem;
                setHistoryItems((prev) => [
                  nextHistoryItem,
                  ...prev.filter((item) => item.requestId !== nextHistoryItem.requestId),
                ].slice(0, Math.max(0, historySettings.maxEntries)));
                setHistorySyncError(null);
              }
            }
            setPendingHistoryItem((current) => current?.requestId === requestId ? null : current);
            return;
          }
          setPendingHistoryItem((current) =>
            current?.requestId === requestId
              ? {
                  ...current,
                  responseStatus: "failed",
                  answer: "Home-node execution did not produce a final answer before the polling window ended.",
                  trace: {
                    ...trace,
                    ...(current.trace ?? {}),
                    execution_state: "pending",
                    execution_target: "home_node",
                    deferred_work_id: deferredWorkId,
                    home_node_status: "poll_timeout",
                  },
                }
              : current,
          );
        })().catch((pollError) => {
          const message = pollError instanceof ApiError
            ? formatApiError(pollError)
            : "Deferred home-node response could not be retrieved.";
          if (pollError instanceof ApiError) {
            logApiError(pollError);
          }
          setPendingHistoryItem((current) =>
            current?.requestId === requestId
              ? {
                  ...current,
                  responseStatus: "failed",
                  answer: message,
                  trace: {
                    ...trace,
                    ...(current.trace ?? {}),
                    execution_state: "pending",
                    execution_target: "home_node",
                    deferred_work_id: deferredWorkId,
                    home_node_status: "poll_failed",
                  },
                }
              : current,
          );
        });
        return;
      }

      const historyPayload = {
        requestId,
        question: trimmed,
        answer: parsed.answer,
        responseStatus: "completed",
        traceId: trace?.trace_id,
        packName: selectedPackName,
        packHash: selectedVerifiedPack.hash,
        trace,
        citations: data.citations ?? [],
        attachments: historyAttachments,
      };
      const resolvedHistoryItem: AskHistoryItem = {
        id: pendingItem.id,
        createdAt: pendingItem.createdAt,
        ...historyPayload,
        journeySeed: [
          ...(pendingItem.journeySeed ?? []),
          buildLocalJourneyEvent(requestId, "ui", "response_received", "Response received in GPT UI."),
        ],
      };

      if (historySettings.enabled) {
        if (!isRemoteHistory) {
          recordLocalAskHistory(historyPayload);
          setHistoryItems((prev) => [resolvedHistoryItem, ...prev].slice(0, Math.max(0, historySettings.maxEntries)));
          setPendingHistoryItem(null);
          setPrompt("");
          setSelectedExperienceMode(null);
          clearComposerContext();
        } else {
          try {
            const recordedItem = await recordRemoteAskHistory(client, historyPayload, historySettings);
            const nextHistoryItem = recordedItem
                ? {
                    ...recordedItem,
                    citations: resolvedHistoryItem.citations,
                    attachments: recordedItem.attachments?.length ? recordedItem.attachments : resolvedHistoryItem.attachments,
                    journeySeed: resolvedHistoryItem.journeySeed,
                  }
                : resolvedHistoryItem;
            setHistoryItems((prev) => [
              nextHistoryItem,
              ...prev.filter((item) => item.requestId !== nextHistoryItem.requestId),
            ].slice(0, Math.max(0, historySettings.maxEntries)));
            setHistorySyncError(null);
            setPendingHistoryItem(null);
            setPrompt("");
            setSelectedExperienceMode(null);
            clearComposerContext();
          } catch (historyError) {
            if (handleAuthRedirect(historyError, (href) => router.push(href), AUTH_NOTICE_MESSAGE, authMode)) {
              setPendingHistoryItem(null);
              return;
            }
            if (historyError instanceof ApiError) {
              logApiError(historyError);
            }
            setHistorySyncError(
              "Answer received, but shared ask history could not be synced. This response is only visible on this device until sync recovers.",
            );
            setHistoryItems((prev) => [resolvedHistoryItem, ...prev].slice(0, Math.max(0, historySettings.maxEntries)));
            setPendingHistoryItem(null);
            setPrompt("");
            setSelectedExperienceMode(null);
            clearComposerContext();
          }
        }
      } else {
        setPendingHistoryItem(null);
        setPrompt("");
        setSelectedExperienceMode(null);
        clearComposerContext();
      }
    } catch (err) {
      setPendingHistoryItem(null);
      if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (err instanceof ApiError) {
        logApiError(err);
        void reportError({
          category: "api",
          summary: "ask.request_failed",
          status: err.status,
          requestId: err.requestId,
          traceId: getApiErrorPayloadString(err, "trace_id"),
        });
        setError(formatAskRequestError(err, selectedPackName));
      } else if (err instanceof Error) {
        setError(err.message);
      } else {
        setError("Ask request failed.");
      }
    } finally {
      setLoading(false);
    }
  };

  const handleSendAttempt = useCallback(() => {
    if (isComposerBusy) {
      return;
    }
    if (!hasPromptText) {
      return;
    }
    if (isOffline) {
      setError("Offline mode: reconnect to submit questions.");
      return;
    }
    if (!selectedPackName) {
      setError("Select a pack in Trust before asking a question.");
      openModal("pack");
      return;
    }
    if (!selectedVerifiedPack) {
      setError(
        livePackStatus === "loading"
          ? "Loading the selected pack from the live verified catalog. Please try again in a moment."
          : "Selected pack is not available in the live verified catalog. Open Trust to refresh.",
      );
      openModal("trust");
      return;
    }
    void onSend();
  }, [
    hasPromptText,
    isComposerBusy,
    isOffline,
    livePackStatus,
    onSend,
    openModal,
    selectedPackName,
    selectedVerifiedPack,
  ]);

  const isGlobalInputFocusBlocked = Boolean(
    activeModal
    || isPackCatalogEmpty
    || feedbackTarget
    || isMobileMenuOpen
    || isUserMenuOpen
    || isComposerMenuOpen
    || attachmentUploadFeedback.status !== "idle",
  );

  const globalInputFocus = useGlobalInputFocus({
    inputRef,
    value: prompt,
    onValueChange: setPromptValue,
    onSubmit: handleSendAttempt,
    enabled: runtime.config.globalInputFocusEnabled && !isComposerBusy,
    isModalOpen: isGlobalInputFocusBlocked,
    clearOnEscape: true,
  });

  const latestDisplayedItem = historyItemsForDisplay[historyItemsForDisplay.length - 1] ?? null;
  const chatScrollKey = latestDisplayedItem
    ? [
        activeWorkspaceTab,
        latestDisplayedItem.id,
        latestDisplayedItem.isPending ? "pending" : "ready",
        latestDisplayedItem.answer.length,
        latestDisplayedItem.traceId ?? latestDisplayedItem.trace?.trace_id ?? "",
      ].join(":")
    : `${activeWorkspaceTab}:empty:${historySettings.enabled}`;
  const chatAutoScroll = useChatAutoScroll(
    historyBodyRef,
    historyBottomRef,
    chatScrollKey,
    {
      enabled: stickyChatLayoutEnabled && activeWorkspaceTab === "chats",
      bottomThresholdPx: 140,
      defaultBehavior: loading ? "auto" : "smooth",
    },
  );

  const gatewayLabel = gatewayState === "up" ? "UP" : gatewayState === "down" ? "DOWN" : "CHECK";
  const pillarEmptyMessage = livePackStatus === "loading"
    ? "Loading live verified packs..."
    : livePackError
      ? "Live verified packs could not be loaded. Open Trust to retry."
      : liveVerifiedPacks.length === 0
        ? "No live verified packs available. Open Trust to refresh the catalog."
        : "No pillars available in the live verified packs.";

  const quickActionsCard = (
    <div className="home-card-gradient">
      <div className="home-label">Quick actions</div>
      <div className="home-quick-links">
        <button className="home-quick-link" type="button" onClick={focusAskInput}>
          Ask
        </button>
        <button className="home-quick-link" type="button" onClick={() => openModal("core")}>
          Core Nodes
        </button>
        <button className="home-quick-link" type="button" onClick={() => openModal("action")}>
          Action
        </button>
        <button className="home-quick-link" type="button" onClick={() => openModal("trace")}>
          Trace
        </button>
        <button className="home-quick-link" type="button" onClick={() => openModal("trust")}>
          Trust
        </button>
      </div>
      <p className="home-quick-note">
        Use Trust to refresh and review live verified packs before asking or running actions.
      </p>
    </div>
  );

  const selectedCoreModeLabel =
    EXPERIENCE_MODES.find((mode) => mode.id === selectedExperienceMode)?.label ?? "Core Nodes";

  const coreNodesSelector = (
    <div className="home-core-selector" aria-label="Core CWI nodes">
      <div className="home-core-selector-header">
        <div>
          <div className="home-label">Core Nodes</div>
          <div className="home-core-selector-subtitle">Choose a structured CWI command for the next ask.</div>
        </div>
      </div>
      <div className="home-core-selector-grid">
        {EXPERIENCE_MODES.map((mode) => (
          <button
            key={mode.id}
            className={
              selectedExperienceMode === mode.id
                ? "home-core-node home-core-node--active"
                : "home-core-node"
            }
            type="button"
            onClick={() => {
              applyExperienceMode(mode.id);
              if (activeModal === "core") {
                closeModal();
              }
            }}
          >
            <span className="home-core-node-title">{mode.label}</span>
            <span className="home-core-node-copy">{mode.title}</span>
          </button>
        ))}
      </div>
    </div>
  );

  const sidebarContent = (
    <>
      <div>
        <div className="home-brand">
          <Image className="home-brand-logo" src="/cwi-logo.png" alt="CWI" width={263} height={84} priority />
          <div>
            <div className="home-brand-tagline">Where Integrity and Truth Meet Resiliency</div>
            <div className="home-pack-indicator">
              Pack: <span className="home-pack-indicator-value">{selectedPackName || "Not selected"}</span>
            </div>
          </div>
        </div>
      </div>

      <div>
        <div>
          <div className="home-label">Gateway</div>
          <div className="home-gateway-status">{gatewayLabel}</div>
        </div>
        <p className="home-gateway-detail">{gatewayDetail}</p>
      </div>

      <div>
        <div className="home-label">Domain / Pillar</div>
        {pillarGroups.length > 0 ? (
          <div className="home-pillars-groups">
            {pillarGroups.map((group) => (
              <div
                key={group.packName}
                className={
                  group.packName === selectedPackName
                    ? "home-pillars-group home-pillars-group--active"
                    : "home-pillars-group"
                }
              >
                <div className="home-pillars-group-header">
                  <span className="home-pillars-group-name">{group.packLabel}</span>
                  <span className="home-pillars-group-version">v{group.packVersion}</span>
                </div>
                <div className="home-pillars-list">
                  {group.pillars.map((pillar) => {
                    const key = `${group.packName}::${pillar}`;
                    const isSelected = key === selectedPillar;
                    return (
                      <button
                        key={key}
                        className={
                          isSelected
                            ? "home-pillars-button home-pillars-button--active"
                            : "home-pillars-button"
                        }
                        type="button"
                        onClick={() => onSelectPillar(group.packName, pillar)}
                      >
                        {pillar}
                      </button>
                    );
                  })}
                </div>
              </div>
            ))}
            <div className="home-pillars-selected">
              Selected: {selectedPillarLabel}
            </div>
          </div>
        ) : (
          <div className="home-pillars-empty">
            {pillarEmptyMessage}
          </div>
        )}
      </div>

      <div>
        <div className="home-label">Pack selection</div>
        {livePackStatus === "ready" ? (
          <div className="home-pack-availability">
            {packOptions.length === 0
              ? "No live verified packs available."
              : packOptions.length === 1
                ? "1 live verified pack available."
                : `${packOptions.length} live verified packs available.`}
          </div>
        ) : null}
        {selectedPackName ? (
          <div className="home-pack-status">Selected: {selectedPackName}</div>
        ) : (
          <div className="home-pack-empty">
            No pack selected.
            <button className="home-pack-link" type="button" onClick={() => openModal("trust")}>
              Select pack
            </button>
          </div>
        )}
        <div className="home-label home-label-spaced">Verification</div>
        {selectedVerifiedPack ? (
          <div className="home-pack-status">Selected pack is live verified.</div>
        ) : selectedPackName ? (
          livePackStatus === "loading" ? (
            <div className="home-pack-empty">Refreshing live pack catalog...</div>
          ) : (
            <div className="home-pack-empty">
              Selected pack is not available in the live verified catalog.
              <button className="home-pack-link" type="button" onClick={() => openModal("trust")}>
                Refresh Trust
              </button>
            </div>
          )
        ) : (
          <div className="home-pack-empty">
            Select a pack from the live verified catalog.
          </div>
        )}
        {packOptions.length > 1 ? (
          <div className="home-pack-switcher" role="list" aria-label="Available packs">
            {packOptions.slice(0, 5).map((pack) => {
              const isActive = pack.name === selectedPackName;
              return (
                <button
                  key={pack.name}
                  className={isActive ? "home-pack-chip home-pack-chip--active" : "home-pack-chip"}
                  type="button"
                  onClick={() => onSelectPack(pack.name)}
                >
                  {pack.displayName ?? pack.name}
                </button>
              );
            })}
            <button className="home-pack-link home-pack-link--inline" type="button" onClick={() => openModal("trust")}>
              View all
            </button>
          </div>
        ) : null}
      </div>

      {quickActionsCard}

      <div className="home-footer">
        <div className="home-footer-links">
          <button className="home-footer-link" type="button" onClick={() => openModal("trust")}>
            Trust
          </button>
          <button className="home-footer-link" type="button" onClick={focusAskInput}>
            Ask
          </button>
          <button className="home-footer-link" type="button" onClick={() => openModal("action")}>
            Action
          </button>
          <button className="home-footer-link" type="button" onClick={() => openModal("trace")}>
            Trace
          </button>
        </div>
      </div>
    </>
  );

  const drawerContent = (
    <>
      <div className="home-mobile-control-card">
        <div className="home-label">Chat controls</div>
        <div className="home-mobile-control-list">
          <button
            className="home-mobile-control-button"
            type="button"
            onClick={() => {
              closeMobileMenu();
              openModal("pack");
            }}
          >
            <span>Pack</span>
            <strong>{selectedPackName || "Select"}</strong>
          </button>
          <button
            className="home-mobile-control-button"
            type="button"
            onClick={() => {
              closeMobileMenu();
              openModal("core");
            }}
          >
            <span>Core Node</span>
            <strong>{selectedCoreModeLabel}</strong>
          </button>
          <button
            className="home-mobile-control-button"
            type="button"
            onClick={() => {
              closeMobileMenu();
              openModal("trace");
            }}
          >
            <span>Trace</span>
            <strong>Lookup</strong>
          </button>
          <button
            className="home-mobile-control-button"
            type="button"
            onClick={() => {
              closeMobileMenu();
              openModal("trust");
            }}
          >
            <span>Trust</span>
            <strong>Review</strong>
          </button>
          <button
            className="home-mobile-control-button"
            type="button"
            onClick={() => {
              closeMobileMenu();
              openModal("action");
            }}
          >
            <span>Action</span>
            <strong>Open</strong>
          </button>
        </div>
      </div>

      <div className="home-mobile-control-card">
        <div className="home-label">Request options</div>
        <div className="home-mobile-option-row">
          <label
            className={`web-toggle ${!isTrustedWebAvailable ? "web-toggle--disabled" : ""}`}
            title={isTrustedWebAvailable ? "Use configured trusted sites" : "Trusted web context is not enabled"}
          >
            <input
              className="web-toggle-input"
              type="checkbox"
              checked={useWeb && isTrustedWebAvailable}
              disabled={!isTrustedWebAvailable || isComposerBusy}
              onChange={(event) => {
                const enabled = event.target.checked && isTrustedWebAvailable;
                setUseWeb(enabled);
                saveUseWeb(enabled);
              }}
              aria-label="Use web"
            />
            <span className="web-toggle-track" aria-hidden="true">
              <span className="web-toggle-thumb" />
            </span>
            <span className="web-toggle-label">Use web</span>
          </label>
        </div>
        <label className="home-mobile-select-row" htmlFor="home-mobile-model-tier">
          <span>Model</span>
          <select
            id="home-mobile-model-tier"
            className="home-model-tier-select"
            value={modelTier}
            onChange={(event) => {
              const value = event.target.value as "free" | "pro" | "edge";
              setModelTier(value);
              saveModelTier(value);
            }}
            disabled={isComposerBusy}
          >
            <option value="free">Free (Standard)</option>
            <option value="pro">Pro</option>
          </select>
        </label>
      </div>

      <div className="home-mobile-control-card">
        <div className="home-label">History</div>
        <div className="home-mobile-control-list">
          <button
            className="home-mobile-control-button"
            type="button"
            onClick={() => {
              closeMobileMenu();
              onClearHistory();
            }}
            disabled={!historySettings.enabled || historyItems.length === 0}
          >
            <span>Chat history</span>
            <strong>Clear</strong>
          </button>
          <button
            className="home-mobile-control-button"
            type="button"
            onClick={pingGateway}
          >
            <span>Gateway</span>
            <strong>{gatewayLabel}</strong>
          </button>
        </div>
        <p className="home-mobile-control-note">{gatewayDetail}</p>
      </div>

      <div className="home-mobile-control-card">
        <div className="home-label">Sources</div>
        <div className="home-mobile-sources-summary">
          {sourceAttachments.length === 0
            ? "No saved sources are staged."
            : sourceAttachments.length === 1
              ? "1 saved source is staged."
              : `${sourceAttachments.length} saved sources are staged.`}
        </div>
        <button
          className="home-mobile-control-button home-mobile-control-button--single"
          type="button"
          onClick={() => {
            closeMobileMenu();
            openAttachmentPicker();
          }}
        >
          <span>Sources</span>
          <strong>Upload File/Image</strong>
        </button>
        {sourceAttachments.length > 0 ? (
          <div className="home-mobile-source-list" role="list" aria-label="Saved sources">
            {sourceAttachments.map((attachment) => (
              <div key={attachment.id} className="home-mobile-source-item" role="listitem">
                <div className="home-mobile-source-copy">
                  <span>{attachment.name}</span>
                  <small>{attachment.kind === "image" ? "Image" : "File"} | {formatFileSize(attachment.size)}</small>
                </div>
                <button
                  className="home-mobile-source-remove"
                  type="button"
                  onClick={() => removeSourceAttachment(attachment.id)}
                >
                  Remove
                </button>
              </div>
            ))}
          </div>
        ) : null}
      </div>

      {quickActionsCard}
    </>
  );
  const chatShellStyle = stickyChatLayoutEnabled
    ? ({ "--home-composer-height": `${composerHeight}px` } as CSSProperties)
    : undefined;

  return (
    <div className={stickyChatLayoutEnabled ? "home-shell home-shell--chat" : "home-shell"}>
      <div className="home-container">
        <div className="home-surface">
            <div className="home-layout">
            <div className="home-mobile-bar">
              <div className="home-mobile-actions">
                <button
                  className="home-mobile-menu-button"
                  type="button"
                  onClick={toggleMobileMenu}
                  aria-label={isMobileMenuOpen ? "Close menu" : "Open menu"}
                  aria-expanded={isMobileMenuOpen}
                  aria-controls="home-mobile-drawer"
                >
                  <span className="home-mobile-menu-icon" aria-hidden="true">
                    <span />
                    <span />
                    <span />
                  </span>
                  <span className="home-mobile-menu-label">Menu</span>
                </button>
              </div>
            </div>

            <aside className="home-sidebar">
              {sidebarContent}
            </aside>

            <section className="home-main">
              <div className="home-main-grid">
                <div className="home-history" style={chatShellStyle}>
                  <div className="home-history-header">
                    <div className="home-history-controls">
                      <div className="home-history-actions">
                        <div className="home-history-pill">
                          <span
                            className={`home-history-dot ${
                              historySettings.enabled ? "home-history-dot--on" : "home-history-dot--off"
                            }`}
                          />
                          {historySettings.enabled ? "History On" : "History Off"}
                        </div>
                        <div className="home-history-pill home-history-pill--status">
                          <span
                            className={`home-status-dot ${
                              gatewayState === "up"
                                ? "home-status-dot--up"
                                : gatewayState === "down"
                                  ? "home-status-dot--down"
                                  : "home-status-dot--check"
                            }`}
                          />
                        </div>
                        <button
                          className="home-history-button"
                          onClick={onClearHistory}
                          disabled={!historySettings.enabled || historyItems.length === 0}
                        >
                          Clear
                        </button>
                        <button
                          className="home-history-button home-history-button--accent"
                          onClick={pingGateway}
                        >
                          Ping
                        </button>
                      </div>
                    </div>
                  </div>

                  <div className="home-workspace-bar">
                    <div className="home-workspace-tabs" role="tablist" aria-label="Workspace view">
                      <button
                        className={
                          activeWorkspaceTab === "chats"
                            ? "home-workspace-tab home-workspace-tab--active"
                            : "home-workspace-tab"
                        }
                        type="button"
                        role="tab"
                        aria-selected={activeWorkspaceTab === "chats"}
                        onClick={() => setActiveWorkspaceTab("chats")}
                      >
                        Chats
                      </button>
                      <button
                        className={
                          activeWorkspaceTab === "sources"
                            ? "home-workspace-tab home-workspace-tab--active"
                            : "home-workspace-tab"
                        }
                        type="button"
                        role="tab"
                        aria-selected={activeWorkspaceTab === "sources"}
                        onClick={() => setActiveWorkspaceTab("sources")}
                      >
                        Sources
                        {sourceAttachments.length > 0 ? (
                          <span className="home-workspace-tab-count">
                            {sourceAttachments.length}
                          </span>
                        ) : null}
                      </button>
                    </div>
                  </div>

                  {error ? (
                    <div className="home-alert">
                      <div className="home-alert-title">Unable to send</div>
                      <div className="home-alert-text">{error}</div>
                    </div>
                  ) : null}
                  {historySyncError ? (
                    <div className="home-alert">
                      <div className="home-alert-title">History sync delayed</div>
                      <div className="home-alert-text">{historySyncError}</div>
                    </div>
                  ) : null}

                  <div className="home-history-body" ref={historyBodyRef} data-testid="chat-scroll">
                  {activeWorkspaceTab === "sources" ? (
                    <div className="home-sources-panel">
                      <div className="home-sources-panel-copy">
                        <h3 className="home-panel-title">Workspace Sources</h3>
                        <p className="home-panel-text">
                          Upload files or images here to keep them on hand while you work. They stay in Sources until you remove them.
                        </p>
                      </div>

                      {sourceAttachments.length === 0 ? (
                        <div className="home-sources-empty">
                          <div className="home-sources-empty-title">No sources staged yet.</div>
                          <div className="home-sources-empty-copy">
                            Add screenshots or documents to build reusable context before you send.
                          </div>
                          <div className="home-sources-empty-actions">
                            <button className="home-workspace-action" type="button" onClick={openAttachmentPicker}>
                              Upload File/Image
                            </button>
                          </div>
                        </div>
                      ) : null}

                      {sourceAttachments.length > 0 ? (
                        <div className="home-sources-list" role="list" aria-label="Staged sources">
                          {sourceAttachments.map((attachment) => (
                            <div key={attachment.id} className="home-sources-card" role="listitem">
                              <div className="home-sources-card-main">
                                {attachment.previewUrl || attachment.previewDataUrl ? (
                                  <Image
                                    className="home-sources-card-image"
                                    src={attachment.previewUrl ?? attachment.previewDataUrl ?? ""}
                                    alt={attachment.name}
                                    width={72}
                                    height={72}
                                    unoptimized
                                  />
                                ) : (
                                  <div className="home-sources-card-icon" aria-hidden="true">
                                    {attachment.kind === "image" ? "IMG" : "FILE"}
                                  </div>
                                )}
                                <div className="home-sources-card-copy">
                                  <div className="home-sources-card-title">{attachment.name}</div>
                                  <div className="home-sources-card-meta">
                                    {attachment.kind === "image" ? "Image" : "File"} | {formatFileSize(attachment.size)}
                                  </div>
                                  {attachment.excerpt ? (
                                    <div className="home-sources-card-excerpt">{attachment.excerpt}</div>
                                  ) : null}
                                </div>
                              </div>
                              <button
                                className="home-sources-card-remove"
                                type="button"
                                onClick={() => removeSourceAttachment(attachment.id)}
                              >
                                Remove
                              </button>
                            </div>
                          ))}
                        </div>
                      ) : null}

                    </div>
                  ) : (
                    <>
                      {!historySettings.enabled ? (
                        <div className="home-panel">
                          <h3 className="home-panel-title">Ask history is disabled.</h3>
                          <p className="home-panel-text">
                            Ask history is managed by your administrator.
                          </p>
                        </div>
                      ) : historyItems.length === 0 ? (
                        <div className="home-onboarding">
                          <div className="home-onboarding-kicker">Ready when you are</div>
                          <div className="home-onboarding-grid">
                            <section className="home-onboarding-section">
                              <h3>What I understand about you</h3>
                              <p>I will use governed memory, verified packs, and current context only when they match this user and tenant.</p>
                            </section>
                            <section className="home-onboarding-section">
                              <h3>What you should do today</h3>
                              <p>Start with a briefing, review changes, check risk, then turn the next step into a plan.</p>
                            </section>
                            <section className="home-onboarding-section">
                              <h3>One risk</h3>
                              <p>Imported context may be incomplete until sources, connectors, and graph memory are fully synced.</p>
                            </section>
                            <section className="home-onboarding-section home-onboarding-section--cta">
                              <h3>CTA</h3>
                              <button
                                className="home-onboarding-cta"
                                type="button"
                                onClick={() => applyExperienceMode("make_a_plan")}
                              >
                                Make a plan
                              </button>
                            </section>
                          </div>
                        </div>
                      ) : null}

                      {historySettings.enabled && historyItems.length > 0 ? (
                        <div className="home-history-list">
                          {historyItemsForDisplay.map((item) => {
                            const feedbackElementId = `feedback-answer-${item.id}`;
                            return (
                            <div key={item.id} id={feedbackElementId} className="home-panel home-history-item">
                              <div className="home-label home-history-label home-history-label--question">Question</div>
                              <div className="home-history-question-block">
                                <p className="home-history-message home-history-message--question">{item.question}</p>
                                {item.attachments && item.attachments.length > 0 ? (
                                  <div className="home-history-attachments" role="list" aria-label="Question attachments">
                                    {item.attachments.map((attachment, index) => (
                                      <div
                                        key={`${item.id}-attachment-${index}-${attachment.name}`}
                                        className="home-history-attachment"
                                        role="listitem"
                                      >
                                        {attachment.kind === "image" && attachment.preview_data_url ? (
                                          <Image
                                            className="home-history-attachment-image"
                                            src={attachment.preview_data_url}
                                            alt={attachment.name}
                                            width={160}
                                            height={160}
                                            unoptimized
                                          />
                                        ) : (
                                          <div className="home-history-attachment-icon" aria-hidden="true">
                                            {attachment.kind === "image" ? "IMG" : "FILE"}
                                          </div>
                                        )}
                                        <div className="home-history-attachment-copy">
                                          <div className="home-history-attachment-title">{attachment.name}</div>
                                          <div className="home-history-attachment-meta">
                                            {attachment.kind === "image" ? "Image" : "File"} | {formatFileSize(attachment.size)}
                                          </div>
                                          {attachment.excerpt ? (
                                            <div className="home-history-attachment-excerpt">{attachment.excerpt}</div>
                                          ) : null}
                                        </div>
                                      </div>
                                    ))}
                                  </div>
                                ) : null}
                              </div>
                              <div className="home-label home-history-label home-history-label--answer">Answer</div>
                              {item.isPending ? (
                                <div className="home-history-message home-history-message--answer home-history-message--answer-pending">
                                  <div className="home-processing-state">
                                    <GlassboxInline loading compact />
                                    <div>
                                      <div className="home-processing-title">Processing</div>
                                      <div className="home-processing-copy">
                                        Checking current intent, governed context, and output limits.
                                      </div>
                                    </div>
                                  </div>
                                </div>
                              ) : (
                                <>
                                  <CitedAnswer
                                    answer={item.answer}
                                    citations={item.citations}
                                    trace={item.trace}
                                    className="home-history-message home-history-message--answer"
                                  />
                                </>
                              )}
                              <div className="home-history-answer-footer">
                                {!item.isPending && typeof item.trace?.confidence === "number" ? (
                                  <div className="home-history-confidence">
                                    <GlassboxInline trace={item.trace} compact />
                                  </div>
                                ) : null}
                                <div className="home-history-meta">
                                  <span>{formatDateTime(item.createdAt)}</span>
                                  {!item.isPending && item.traceId ? (
                                    <button
                                      className="link-sky"
                                      type="button"
                                      onClick={() => openModal("trace", item.traceId)}
                                    >
                                      Trace {item.traceId}
                                    </button>
                                  ) : null}
                                </div>
                              </div>
                              {!item.isPending ? (
                                <div className="answer-feedback-actions">
                                  <button
                                    className="answer-feedback-button"
                                    type="button"
                                    onClick={() => void submitAnswerFeedback(item, feedbackElementId, "Helpful", "Low", "", false)}
                                    disabled={feedbackSubmitting}
                                  >
                                    Helpful
                                  </button>
                                  <button
                                    className="answer-feedback-button answer-feedback-button--issue"
                                    type="button"
                                    onClick={() => openFeedbackModal(item, feedbackElementId)}
                                    disabled={feedbackSubmitting}
                                  >
                                    Report Issue
                                  </button>
                                </div>
                              ) : null}
                              <JourneyPanel
                                requestId={item.requestId ?? item.trace?.request_id}
                                seedEvents={item.journeySeed}
                                live={Boolean(item.isPending)}
                                defaultExpanded={Boolean(item.isPending)}
                                authMode={authMode}
                                authIdleTimeoutMs={authIdleTimeoutMs}
                              />
                            </div>
                            );
                          })}
                        </div>
                      ) : null}
                      <div ref={historyBottomRef} className="home-history-bottom-anchor" aria-hidden="true" />
                    </>
                  )}

                  </div>

                  {stickyChatLayoutEnabled && activeWorkspaceTab === "chats" ? (
                    <button
                      className={
                        chatAutoScroll.showJumpToBottom
                          ? "home-jump-bottom home-jump-bottom--visible"
                          : "home-jump-bottom"
                      }
                      type="button"
                      onClick={() => chatAutoScroll.scrollToLatest("smooth")}
                      aria-label="Jump to latest message"
                      title="Jump to latest"
                      aria-hidden={!chatAutoScroll.showJumpToBottom}
                      tabIndex={chatAutoScroll.showJumpToBottom ? 0 : -1}
                    >
                      <span aria-hidden="true">↓</span>
                    </button>
                  ) : null}

                  {activeWorkspaceTab === "chats" ? (
                    <div className="home-history-footer" ref={composerShellRef}>
                      <div className="home-tip">
                        Tip: Open a trace to review sources and confidence details.
                      </div>

                      {loading && !pendingHistoryItem ? (
                        <div className="home-loading" role="status" aria-live="polite" aria-label="Sending">
                          <div className="home-loading-dots" aria-hidden="true">
                            <span className="home-loading-dot" />
                            <span className="home-loading-dot" />
                            <span className="home-loading-dot" />
                            <span className="home-loading-dot" />
                          </div>
                        </div>
                      ) : null}

                      <div className="home-input-bar">
                      <div className="home-input-top-row">
                        <div className="home-input-pack">
                          <span className="home-input-pack-label">Pack</span>
                          <button
                            className="home-input-pack-value"
                            type="button"
                            onClick={() => openModal("pack")}
                          >
                            {selectedPackName || "Not selected"}
                          </button>
                        </div>
                        <button
                          className="home-input-core-button"
                          type="button"
                          onClick={() => openModal("core")}
                          aria-label="Select Core Node"
                        >
                          <span className="home-input-pack-label">Core Node</span>
                          <span className="home-input-core-value">{selectedCoreModeLabel}</span>
                        </button>
                      </div>
                      {sourceAttachments.length > 0 ? (
                        <div className="home-compose-note" role="status" aria-live="polite">
                          {sourceAttachments.length === 1
                            ? "1 saved source is available in Sources."
                            : `${sourceAttachments.length} saved sources are available in Sources.`}
                        </div>
                      ) : null}

                      {imageAttachments.length > 0 ? (
                        <div className="home-compose-stage" aria-live="polite">
                          <div className="home-compose-stage-header">
                            <div className="home-compose-stage-title">
                              {imageAttachments.length === 1
                                ? "1 image attached to this prompt"
                                : `${imageAttachments.length} images attached to this prompt`}
                            </div>
                            <div className="home-compose-stage-hint">
                              {hasPromptText
                                ? "These images will be sent with your message."
                                : "Add text and send when you're ready."}
                            </div>
                          </div>
                          <div
                            className="home-compose-image-strip"
                            role="list"
                            aria-label="Images attached to this prompt"
                          >
                            {imageAttachments.map((attachment) => (
                              <div
                                key={attachment.id}
                                className="home-compose-image-card"
                                role="listitem"
                                title={attachment.name}
                                aria-label={`${attachment.name} attached to this prompt`}
                              >
                                {attachment.previewUrl ? (
                                  <Image
                                    className="home-compose-image-preview"
                                    src={attachment.previewUrl}
                                    alt={`Preview of ${attachment.name}`}
                                    width={64}
                                    height={64}
                                    unoptimized
                                  />
                                ) : (
                                  <div className="home-compose-image-fallback" aria-hidden="true">
                                    IMG
                                  </div>
                                )}
                                <button
                                  className="home-compose-image-remove"
                                  type="button"
                                  onClick={() => removePromptAttachment(attachment.id)}
                                  aria-label={`Remove ${attachment.name}`}
                                >
                                  Remove
                                </button>
                              </div>
                            ))}
                          </div>
                        </div>
                      ) : null}

                      {fileAttachments.length > 0 ? (
                        <div className="home-compose-context" aria-live="polite">
                          {fileAttachments.map((attachment) => (
                            <div key={attachment.id} className="home-compose-chip">
                              <div className="home-compose-chip-main">
                                {attachment.previewUrl || attachment.previewDataUrl ? (
                                  <Image
                                    className="home-compose-chip-thumb"
                                    src={attachment.previewUrl ?? attachment.previewDataUrl ?? ""}
                                    alt=""
                                    width={36}
                                    height={36}
                                    unoptimized
                                  />
                                ) : (
                                  <span className="home-compose-chip-icon" aria-hidden="true">
                                    {attachment.kind === "image" ? "IMG" : "FILE"}
                                  </span>
                                )}
                                <div className="home-compose-chip-copy">
                                  <div className="home-compose-chip-title">{attachment.name}</div>
                                  <div className="home-compose-chip-meta">
                                    {formatFileSize(attachment.size)} |{" "}
                                    {attachment.excerpt
                                      ? "text excerpt ready"
                                      : attachment.kind === "image"
                                        ? "image metadata"
                                        : "metadata only"}
                                  </div>
                                </div>
                              </div>
                              <button
                                className="home-compose-chip-remove"
                                type="button"
                                onClick={() => removePromptAttachment(attachment.id)}
                              >
                                Remove
                              </button>
                            </div>
                          ))}
                        </div>
                      ) : null}

                      {hasComposerContext || isReadingUpload ? (
                        <div className="home-compose-note" role="status" aria-live="polite">
                          {isReadingUpload
                            ? "Preparing uploaded context..."
                            : hasImageAttachment
                              ? hasPromptText
                                ? "Your images are attached below and will be included with this ask."
                                : "Your images are attached below. Add text when you're ready to send them."
                              : "Uploaded context will be included with this ask."}
                        </div>
                      ) : null}

                      {composerPackMessage ? (
                        <div className="home-compose-guard" role="status" aria-live="polite">
                          <span>{composerPackMessage}</span>
                          <button
                            className="home-compose-guard-action"
                            type="button"
                            onClick={() => openModal(selectedPackName ? "trust" : "pack")}
                          >
                            {selectedPackName ? "Open Trust" : "Select pack"}
                          </button>
                        </div>
                      ) : null}

                      <div className="web-toggle-row">
                        <label
                          className={`web-toggle ${!isTrustedWebAvailable ? "web-toggle--disabled" : ""}`}
                          title={isTrustedWebAvailable ? "Use configured trusted sites" : "Trusted web context is not enabled"}
                        >
                          <input
                            className="web-toggle-input"
                            type="checkbox"
                            checked={useWeb && isTrustedWebAvailable}
                            disabled={!isTrustedWebAvailable || isComposerBusy}
                            onChange={(event) => {
                              const enabled = event.target.checked && isTrustedWebAvailable;
                              setUseWeb(enabled);
                              saveUseWeb(enabled);
                            }}
                            aria-label="Use web"
                          />
                          <span className="web-toggle-track" aria-hidden="true">
                            <span className="web-toggle-thumb" />
                          </span>
                          <span className="web-toggle-label">Use web</span>
                        </label>
                      </div>

                      <div className="home-compose-row">
                        <div className="home-compose-menu" ref={composerMenuRef}>
                          <button
                            className="home-compose-add"
                            type="button"
                            onClick={toggleComposerMenu}
                            disabled={isComposerBusy}
                            aria-expanded={isComposerMenuOpen}
                            aria-controls="home-compose-actions"
                            aria-haspopup="menu"
                            aria-label="Add files"
                          >
                            +
                          </button>
                          {isComposerMenuOpen ? (
                            <div id="home-compose-actions" className="home-compose-popover" role="menu">
                              <button
                                className="home-compose-option"
                                type="button"
                                role="menuitem"
                                onClick={openComposerAttachmentPicker}
                              >
                                Upload File/Image
                              </button>
                            </div>
                          ) : null}
                        </div>

                        <textarea
                          id="cwi-main-input"
                          className="home-input"
                          ref={inputRef}
                          placeholder="Ask a question..."
                          aria-label="Ask CWI"
                          rows={1}
                          value={prompt}
                          disabled={isComposerBusy}
                          onChange={onPromptChange}
                          onKeyDown={globalInputFocus.onKeyDown}
                        />

                        <label className="home-model-tier" htmlFor="home-model-tier">
                          <select
                            id="home-model-tier"
                            className="home-model-tier-select"
                            value={modelTier}
                            onChange={(event) => {
                              const value = event.target.value as "free" | "pro" | "edge";
                              setModelTier(value);
                              saveModelTier(value);
                            }}
                            disabled={isComposerBusy}
                          >
                            <option value="free">Free (Standard)</option>
                            <option value="pro">Pro</option>
                          </select>
                        </label>

                        <button
                          className="home-send"
                          type="button"
                          onClick={handleSendAttempt}
                          disabled={isComposerBusy || isOffline || !hasPromptText}
                        >
                          {sendButtonLabel}
                        </button>
                      </div>

                      <input
                        ref={sourceAttachmentInputRef}
                        className="home-compose-hidden-input"
                        type="file"
                        multiple
                        accept="image/*,.txt,.md,.markdown,.json,.jsonl,.csv,.tsv,.log,.pdf,.doc,.docx,.xml,.yaml,.yml,.html,.htm"
                        onChange={onAddSourceFiles}
                      />
                      <input
                        ref={promptAttachmentInputRef}
                        className="home-compose-hidden-input"
                        type="file"
                        multiple
                        accept="image/*,.txt,.md,.markdown,.json,.jsonl,.csv,.tsv,.log,.pdf,.doc,.docx,.xml,.yaml,.yml,.html,.htm"
                        onChange={onAddPromptFiles}
                      />
                      </div>
                    </div>
                  ) : null}
                </div>
              </div>
            </section>
            <aside className="home-glassbox">
              <GlassboxPanel trace={latestTrace} loading={loading} />
            </aside>
          </div>
          <div
            className={`home-mobile-menu ${isMobileMenuOpen ? "home-mobile-menu--open" : ""}`}
            aria-hidden={!isMobileMenuOpen}
          >
            <div className="home-mobile-backdrop" onClick={closeMobileMenu} />
            <div
              id="home-mobile-drawer"
              className="home-mobile-drawer"
              role="dialog"
              aria-modal="true"
              aria-label="Site menu"
            >
              <div className="home-mobile-drawer-top">
                <div className="home-mobile-drawer-header">
                  <div className="home-mobile-drawer-title">Menu</div>
                  <button className="home-mobile-close" type="button" onClick={closeMobileMenu}>
                    Close
                  </button>
                </div>
                {isSessionVisible ? (
                  <div className="home-mobile-session">
                    <button
                      className="home-mobile-session-toggle"
                      type="button"
                      onClick={toggleUserMenu}
                      aria-expanded={isUserMenuOpen}
                      aria-controls="home-mobile-session-menu"
                      aria-haspopup="menu"
                    >
                      <span className="home-mobile-session-avatar" aria-hidden="true">
                        {sessionInitial}
                      </span>
                      <span className="home-mobile-session-name">{sessionDisplayName}</span>
                      <span
                        className={`home-mobile-session-caret ${isUserMenuOpen ? "is-open" : ""}`}
                        aria-hidden="true"
                      >
                        ▾
                      </span>
                    </button>
                    <div
                      id="home-mobile-session-menu"
                      className={`home-mobile-session-menu ${
                        isUserMenuOpen ? "home-mobile-session-menu--open" : ""
                      }`}
                      role="menu"
                    >
                      <button
                        className="home-mobile-session-item"
                        type="button"
                        role="menuitem"
                        onClick={openPreferences}
                      >
                        Account settings
                      </button>
                      <button
                        className="home-mobile-session-item home-mobile-session-item--danger"
                        type="button"
                        role="menuitem"
                        onClick={() => void handleMobileLogout()}
                      >
                        Logout
                      </button>
                    </div>
                  </div>
                ) : null}
              </div>
              <div className="home-mobile-drawer-body">
                {drawerContent}
              </div>
            </div>
          </div>
        </div>
      </div>
    {activeModal === "trust" ? (
      <Modal ariaLabel="Pack trust" onClose={closeModal}>
        <TrustPage />
      </Modal>
    ) : null}
    {activeModal === "action" ? (
      <Modal ariaLabel="Action" onClose={closeModal}>
        <ActionPage />
      </Modal>
    ) : null}
    {activeModal === "trace" ? (
      <Modal ariaLabel="Trace lookup" onClose={closeModal}>
        {activeTraceId ? (
          <TraceDetailPanel traceId={activeTraceId} onBack={() => openModal("trace")} />
        ) : (
          <TraceLookupPanel onLookup={(traceId) => openModal("trace", traceId)} />
        )}
      </Modal>
    ) : null}
    {activeModal === "pack" ? (
      <Modal ariaLabel="Pack selection" onClose={closeModal}>
        <div className="page-stack">
          <div className="card-light">
            <h2 className="card-title">Select pack</h2>
            {livePackError ? (
              <div className="alert alert-error" role="alert">
                {livePackError}
              </div>
            ) : null}
            {livePackStatus === "loading" ? (
              <p className="card-text-muted">Loading live verified packs...</p>
            ) : packOptions.length === 0 ? (
              <>
                <p className="card-text-muted">
                  No live verified packs are available right now. Open Trust to refresh the catalog.
                </p>
                <div className="panel-actions">
                  <button className="btn-primary" type="button" onClick={() => openModal("trust")}>
                    Open Trust
                  </button>
                </div>
              </>
            ) : (
              <>
                <p className="card-subtitle">
                  Choose a live verified pack to use for asks.
                </p>
                {liveFilteredOutPackCount > 0 ? (
                  <p className="card-text-muted-xs-tight">
                    Hidden packs: {liveFilteredOutPackCount} failing live dependency or metadata validation.
                  </p>
                ) : null}
                <div className="home-pack-select">
                  {packOptions.map((pack) => {
                    const label = pack.displayName ?? pack.name;
                    const isActive = pack.name === selectedPackName;
                    return (
                      <button
                        key={pack.name}
                        className={
                          isActive
                            ? "home-pack-select-button home-pack-select-button--active"
                            : "home-pack-select-button"
                        }
                        type="button"
                        onClick={() => onSelectPack(pack.name)}
                      >
                        <div className="home-pack-select-title">{label}</div>
                        <div className="home-pack-select-meta">v{pack.version}</div>
                      </button>
                    );
                  })}
                </div>
              </>
            )}
          </div>
        </div>
      </Modal>
    ) : null}
    {activeModal === "core" ? (
      <Modal ariaLabel="Core Node selection" onClose={closeModal}>
        <div className="page-stack">
          <div className="card-light home-core-modal">
            <h2 className="card-title">Select Core Node</h2>
            <p className="card-subtitle">
              Pick a structured mode to stage the right prompt in the composer.
            </p>
            {coreNodesSelector}
          </div>
        </div>
      </Modal>
    ) : null}
    {feedbackTarget ? (
      <Modal ariaLabel="Report an issue with this answer" onClose={closeFeedbackModal} dismissible={!feedbackSubmitting}>
        <div className="feedback-modal">
          <div>
            <h2 className="card-title">Report an issue with this answer</h2>
            <p className="card-subtitle">Question: {feedbackTarget.item.question}</p>
          </div>
          <label className="label-stack" htmlFor="feedback-type">
            <span className="label-title">Feedback type</span>
            <select
              id="feedback-type"
              className="select-input"
              value={feedbackType}
              onChange={(event) => setFeedbackType(event.target.value)}
              disabled={feedbackSubmitting}
              required
            >
              {FEEDBACK_TYPES.map((type) => (
                <option key={type} value={type}>{type}</option>
              ))}
            </select>
          </label>
          <label className="label-stack" htmlFor="feedback-severity">
            <span className="label-title">Severity</span>
            <select
              id="feedback-severity"
              className="select-input"
              value={feedbackSeverity}
              onChange={(event) => setFeedbackSeverity(event.target.value)}
              disabled={feedbackSubmitting}
              required
            >
              {FEEDBACK_SEVERITIES.map((severity) => (
                <option key={severity} value={severity}>{severity}</option>
              ))}
            </select>
          </label>
          <label className="label-stack" htmlFor="feedback-comment">
            <span className="label-title">Comment</span>
            <textarea
              id="feedback-comment"
              className="textarea-input feedback-comment"
              placeholder="Tell us what seemed wrong or what you expected instead."
              value={feedbackComment}
              onChange={(event) => setFeedbackComment(event.target.value)}
              disabled={feedbackSubmitting}
            />
          </label>
          <label className="feedback-checkbox">
            <input
              type="checkbox"
              checked={feedbackIncludeScreenshot}
              onChange={(event) => setFeedbackIncludeScreenshot(event.target.checked)}
              disabled={feedbackSubmitting}
            />
            <span>Include screenshot of this CWI screen to help diagnose the issue.</span>
          </label>
          <div className="panel-actions">
            <button
              className="btn-primary"
              type="button"
              onClick={() => void submitAnswerFeedback(
                feedbackTarget.item,
                feedbackTarget.elementId,
                feedbackType,
                feedbackSeverity,
                feedbackComment,
                feedbackIncludeScreenshot,
              )}
              disabled={feedbackSubmitting}
            >
              {feedbackSubmitting ? "Submitting..." : "Submit feedback"}
            </button>
            <button className="btn-secondary" type="button" onClick={closeFeedbackModal} disabled={feedbackSubmitting}>
              Cancel
            </button>
          </div>
        </div>
      </Modal>
    ) : null}
    {attachmentUploadFeedback.status !== "idle" ? (
      <Modal
        ariaLabel="File upload status"
        onClose={closeAttachmentUploadFeedback}
        dismissible={attachmentUploadFeedback.status !== "loading"}
      >
        <div className="card-light-compact home-upload-feedback">
          <div>
            <h2 className="card-title">
              {attachmentUploadFeedback.status === "loading"
                ? "Uploading file/image"
                : attachmentUploadFeedback.title}
            </h2>
            <p className="card-subtitle">{attachmentUploadFeedback.message}</p>
          </div>

          {attachmentUploadFeedback.status === "loading" ? (
            <div className="home-loading home-upload-feedback-loading" role="status" aria-live="polite" aria-label="Uploading">
              <div className="home-loading-dots" aria-hidden="true">
                <span className="home-loading-dot" />
                <span className="home-loading-dot" />
                <span className="home-loading-dot" />
                <span className="home-loading-dot" />
              </div>
            </div>
          ) : null}

          {attachmentUploadFeedback.fileNames.length > 0 ? (
            <div className="home-upload-feedback-files">
              {attachmentUploadFeedback.fileNames.slice(0, 5).map((fileName) => (
                <div key={fileName} className="home-upload-feedback-file">
                  {fileName}
                </div>
              ))}
              {attachmentUploadFeedback.fileNames.length > 5 ? (
                <div className="home-upload-feedback-file home-upload-feedback-file--muted">
                  +{attachmentUploadFeedback.fileNames.length - 5} more
                </div>
              ) : null}
            </div>
          ) : null}

          {attachmentUploadFeedback.status !== "loading" && attachmentUploadFeedback.detail ? (
            <div
              className={
                attachmentUploadFeedback.status === "error"
                  ? "alert alert-error"
                  : "alert alert-warning"
              }
              role="alert"
            >
              {attachmentUploadFeedback.detail}
            </div>
          ) : null}

          {attachmentUploadFeedback.status !== "loading" ? (
            <div className="panel-actions">
              <button className="btn-primary" type="button" onClick={closeAttachmentUploadFeedback}>
                Continue
              </button>
            </div>
          ) : null}
        </div>
      </Modal>
    ) : null}
    {feedbackToast ? (
      <div className={`feedback-toast feedback-toast--${feedbackToast.kind}`} role="status" aria-live="polite">
        {feedbackToast.message}
      </div>
    ) : null}
    {isPackCatalogEmpty ? <NoValidPacksModal availabilitySummary={emptyStateSummary} /> : null}
    </div>
  );
}

export default function Home() {
  return (
    <Suspense
      fallback={(
        <div className="home-shell">
          <div className="home-container">
            <div className="home-surface">
              <div className="home-layout">
                <div className="home-loading" role="status" aria-live="polite">
                  Loading…
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    >
      <HomeContent />
    </Suspense>
  );
}
