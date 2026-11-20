"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ApiError, createApiClient, formatApiError, getApiErrorPayloadString } from "@/lib/api/client";
import { formatNumber } from "@/lib/format";
import { logApiError } from "@/lib/logger";
import { reportError } from "@/lib/error-reporting";
import { buildQuestionWithConfidence, parseAnswerWithConfidence } from "@/lib/llm-confidence";
import type { AskResponse } from "@/lib/api/types";
import { AUTH_NOTICE_MESSAGE, handleAuthRedirect, resolveAuthToken } from "@/lib/auth";
import { resolveRuntimeConfig } from "@/lib/config";
import { clearAskDraft, expireAuthSession, loadAskDraft, loadOverrides, loadModelTier, loadUseWeb, saveModelTier, saveUseWeb } from "@/lib/storage";
import { loadSelectedPackName, saveSelectedPackName } from "@/lib/packs/selection";
import { usePackCatalogAvailability } from "@/lib/packs/use-pack-catalog-availability";
import { findMatchingVerifiedPack, resolveCanonicalVerifiedPackSelection } from "@/lib/packs/reconcile";
import { useLiveVerifiedPacks } from "@/lib/packs/use-live-verified-packs";
import { loadAskHistorySettings, recordLocalAskHistory, recordRemoteAskHistory } from "@/lib/history";
import { useOfflineStatus } from "@/lib/offline";
import { GlassboxPanel } from "@/components/glassbox-panel";
import { TracePanel } from "@/components/trace-panel";
import { JourneyPanel } from "@/components/journey-panel";
import { CitedAnswer } from "@/components/cited-answer";
import { NoValidPacksModal } from "@/components/no-valid-packs-modal";
import { Modal } from "@/components/modal";
import type { JourneyEvent } from "@/lib/api/types";
import { buildLocalJourneyEvent } from "@/lib/journey";
import { resolveCurrentSessionIdentity, resolveSessionIdentity } from "@/lib/session-identity";
import {
  FEEDBACK_SEVERITIES,
  FEEDBACK_TYPES,
  buildFeedbackReportRequest,
  captureElementScreenshotDataUrl,
} from "@/lib/feedback";
import { useGlobalInputFocus } from "@/hooks/useGlobalInputFocus";

type FeedbackToast = {
  message: string;
  kind: "success" | "warning" | "error";
};

type AskFeedbackContext = {
  question: string;
  requestId: string;
  packName: string;
  packHash: string;
};

type TrustedWebStatus = {
  enabled: boolean;
  default_profile?: string;
  max_results?: number;
};

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

export default function AskPage() {
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const initialIdentity = useMemo(() => resolveCurrentSessionIdentity(loadOverrides()), []);
  const router = useRouter();
  const handleMissingPackCatalogAuth = useCallback(() => {
    expireAuthSession(AUTH_NOTICE_MESSAGE);
    router.replace("/login");
  }, [router]);
  const [selectedPackName, setSelectedPackName] = useState(() => loadSelectedPackName());
  const [question, setQuestion] = useState("");
  const [modelTier, setModelTier] = useState(() => loadModelTier());
  const [useWeb, setUseWeb] = useState(() => loadUseWeb());
  const [trustedWebStatus, setTrustedWebStatus] = useState<TrustedWebStatus | null>(null);
  const [response, setResponse] = useState<AskResponse | null>(null);
  const [feedbackContext, setFeedbackContext] = useState<AskFeedbackContext | null>(null);
  const [isFeedbackModalOpen, setIsFeedbackModalOpen] = useState(false);
  const [feedbackType, setFeedbackType] = useState<string>(FEEDBACK_TYPES[0]);
  const [feedbackSeverity, setFeedbackSeverity] = useState<string>("Medium");
  const [feedbackComment, setFeedbackComment] = useState("");
  const [feedbackIncludeScreenshot, setFeedbackIncludeScreenshot] = useState(true);
  const [feedbackSubmitting, setFeedbackSubmitting] = useState(false);
  const [feedbackToast, setFeedbackToast] = useState<FeedbackToast | null>(null);
  const questionInputRef = useRef<HTMLTextAreaElement>(null);
  const [journeyRequestId, setJourneyRequestId] = useState<string | null>(null);
  const [journeySeed, setJourneySeed] = useState<JourneyEvent[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { isOffline } = useOfflineStatus();
  const { isEmpty: isPackCatalogEmpty, emptyStateSummary } = usePackCatalogAvailability({
    authMode: runtime.config.authMode,
    authIdleTimeoutMs: runtime.config.authIdleTimeoutMs,
    requestTimeoutMs: runtime.config.requestTimeoutMs,
    onMissingAuth: handleMissingPackCatalogAuth,
  });
  const {
    packs: liveVerifiedPacks,
    status: livePackStatus,
    error: livePackError,
  } = useLiveVerifiedPacks({
    authMode: runtime.config.authMode,
    authIdleTimeoutMs: runtime.config.authIdleTimeoutMs,
    requestTimeoutMs: runtime.config.requestTimeoutMs,
    onMissingAuth: handleMissingPackCatalogAuth,
  });
  const isTrustedWebAvailable = trustedWebStatus?.enabled === true;

  useEffect(() => {
    let active = true;
    void fetch("/api/trusted-web/status", { cache: "no-store" })
      .then(async (statusResponse) => {
        if (!statusResponse.ok) {
          throw new Error("trusted_web_status_unavailable");
        }
        return (await statusResponse.json()) as TrustedWebStatus;
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

  const pack = useMemo(
    () => findMatchingVerifiedPack(selectedPackName, liveVerifiedPacks),
    [liveVerifiedPacks, selectedPackName],
  );

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

    if (!pack) {
      const removedPack = selectedPackName;
      queueMicrotask(() => {
        setSelectedPackName(null);
        saveSelectedPackName(null);
        setError(`Selected pack "${removedPack}" is no longer in the live verified catalog. Choose another pack in Trust.`);
      });
    }
  }, [livePackStatus, liveVerifiedPacks, pack, selectedPackName]);

  useEffect(() => {
    const draft = loadAskDraft();
    if (draft) {
      setQuestion(draft);
      clearAskDraft();
    }
  }, []);

  useEffect(() => {
    if (isPackCatalogEmpty) {
      document.body.classList.add("modal-open");
      return () => document.body.classList.remove("modal-open");
    }

    document.body.classList.remove("modal-open");
  }, [isPackCatalogEmpty]);

  useEffect(() => {
    if (isFeedbackModalOpen) {
      document.body.classList.add("modal-open");
      return () => document.body.classList.remove("modal-open");
    }
    document.body.classList.remove("modal-open");
  }, [isFeedbackModalOpen]);

  const showFeedbackToast = useCallback((message: string, kind: FeedbackToast["kind"]) => {
    setFeedbackToast({ message, kind });
    window.setTimeout(() => {
      setFeedbackToast((current) => (current?.message === message ? null : current));
    }, 5000);
  }, []);

  const resetFeedbackModal = useCallback(() => {
    if (feedbackSubmitting) {
      return;
    }
    setIsFeedbackModalOpen(false);
    setFeedbackType(FEEDBACK_TYPES[0]);
    setFeedbackSeverity("Medium");
    setFeedbackComment("");
    setFeedbackIncludeScreenshot(true);
  }, [feedbackSubmitting]);

  const resolveFeedbackSessionContext = useCallback(async () => {
    let token: string | null = null;
    if (runtime.config.authMode !== "none") {
      token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        const notice =
          runtime.config.authMode === "oidc"
            ? "Sign in with Identity to continue."
            : AUTH_NOTICE_MESSAGE;
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
    if (!identity.tenantId || !identity.userId) {
      setError("Tenant or user identity is missing from the authenticated session.");
      return null;
    }

    return { token, tenantId: identity.tenantId, userId: identity.userId };
  }, [
    router,
    runtime.config.authIdleTimeoutMs,
    runtime.config.authMode,
    runtime.config.tenantId,
    runtime.config.userId,
  ]);

  const submitAnswerFeedback = useCallback(async (
    type: string,
    severity: string,
    comment: string,
    includeScreenshot: boolean,
  ) => {
    if (!response || !feedbackContext) {
      return;
    }

    const sessionContext = await resolveFeedbackSessionContext();
    if (!sessionContext) {
      return;
    }

    setFeedbackSubmitting(true);
    let screenshotDataUrl: string | null = null;
    let screenshotCaptureFailed = false;
    if (includeScreenshot) {
      try {
        const element = document.getElementById("ask-feedback-answer");
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
        requestTimeoutMs: runtime.config.requestTimeoutMs,
        modelTier,
      });
      const payload = buildFeedbackReportRequest(
        {
          tenantId: sessionContext.tenantId,
          userId: sessionContext.userId,
          question: feedbackContext.question,
          answer: response.answer,
          requestId: feedbackContext.requestId,
          traceId: response.trace?.trace_id,
          selectedPackId: feedbackContext.packName,
          selectedPackVersion: response.trace?.pack_version,
          selectedPackDomain: feedbackContext.packName,
          trace: response.trace,
          uiPage: "ask",
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

      const report = await client.feedbackReport(payload, { requestId: crypto.randomUUID() });
      setIsFeedbackModalOpen(false);
      if (screenshotCaptureFailed || (includeScreenshot && !report.screenshot_stored)) {
        showFeedbackToast("Feedback submitted, but screenshot could not be attached.", "warning");
      } else {
        showFeedbackToast("Thanks - feedback submitted.", "success");
      }
    } catch (err) {
      if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
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
    feedbackContext,
    modelTier,
    resolveFeedbackSessionContext,
    response,
    router,
    runtime.config.authMode,
    runtime.config.buildVersion,
    runtime.config.requestTimeoutMs,
    showFeedbackToast,
  ]);

  const submit = async () => {
    setError(null);
    setResponse(null);

    if (isOffline) {
      setError("Offline mode: reconnect to submit questions.");
      return;
    }
    if (!selectedPackName) {
      setError("Select a pack in Trust before asking a question.");
      return;
    }
    if (!pack) {
      setError(
        livePackStatus === "loading"
          ? "Loading the selected pack from the live verified catalog. Please try again in a moment."
          : "Selected pack is not available in the live verified catalog. Open Trust to refresh.",
      );
      return;
    }
    if (!question.trim()) {
      setError("Question is required.");
      return;
    }

    const trimmedQuestion = question.trim();
    const questionWithConfidence = buildQuestionWithConfidence(trimmedQuestion);

    let token: string | null = null;
    if (runtime.config.authMode !== "none") {
      token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        const notice =
          runtime.config.authMode === "oidc"
            ? "Sign in with Identity to continue."
            : AUTH_NOTICE_MESSAGE;
        expireAuthSession(notice);
        router.replace("/login");
        return;
      }
    }

    const identity = resolveSessionIdentity({
      authMode: runtime.config.authMode,
      tenantId: runtime.config.tenantId,
      runtimeUserId: runtime.config.userId,
      token,
    });
    const resolvedTenantId = identity.tenantId;
    if (!resolvedTenantId) {
      setError("Tenant configuration is missing. Contact your administrator.");
      return;
    }
    const resolvedUserId = identity.userId;
    if (!resolvedUserId) {
      setError("User identity is missing from the authenticated session.");
      return;
    }

    const client = createApiClient({
      baseUrl: "",
      token,
      requestTimeoutMs: runtime.config.requestTimeoutMs,
      modelTier,
    });

    const requestId = crypto.randomUUID();
    setJourneyRequestId(requestId);
    setJourneySeed([
      buildLocalJourneyEvent(requestId, "ui", "request_submitted", "Question submitted from ask page."),
      buildLocalJourneyEvent(requestId, "gateway", "request_sent", "Request forwarded to the gateway."),
    ]);

    setLoading(true);
    try {
      const data = await client.ask({
        tenant_id: resolvedTenantId,
        user_id: resolvedUserId,
        question: questionWithConfidence,
        pack: { name: pack.name, anchor: pack.hash },
        context: {
          mode: runtime.config.policyMode,
          top_k: runtime.config.topK,
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
        confidence: parsed.confidence ?? data.trace?.confidence ?? 0,
        sources,
      };
      setResponse({ ...data, answer: parsed.answer, trace });
      setFeedbackContext({
        question: trimmedQuestion,
        requestId,
        packName: pack.name,
        packHash: pack.hash,
      });
      setQuestion("");
      setJourneySeed((current) => [
        ...current,
        buildLocalJourneyEvent(requestId, "ui", "response_received", "Answer returned to the ask page."),
      ]);
      const historySettings = loadAskHistorySettings();
      const historyPayload = {
        requestId,
        question: trimmedQuestion,
        answer: parsed.answer,
        responseStatus: "completed",
        traceId: trace?.trace_id,
        packName: pack.name,
        packHash: pack.hash,
        trace,
        citations: data.citations ?? [],
      };

      if (historySettings.enabled) {
        if (runtime.config.authMode === "none") {
          recordLocalAskHistory(historyPayload);
        } else {
          try {
            await recordRemoteAskHistory(client, historyPayload, historySettings);
          } catch (historyError) {
            if (handleAuthRedirect(historyError, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
              return;
            }
            if (historyError instanceof ApiError) {
              logApiError(historyError);
            }
          }
        }
      }
    } catch (err) {
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
        setJourneySeed((current) => [
          ...current,
          buildLocalJourneyEvent(requestId, "ui", "response_failed", formatAskRequestError(err, selectedPackName)),
        ]);
        setError(formatAskRequestError(err, selectedPackName));
      } else if (err instanceof Error) {
        setJourneySeed((current) => [
          ...current,
          buildLocalJourneyEvent(requestId, "ui", "response_failed", err.message),
        ]);
        setError(err.message);
      } else {
        setJourneySeed((current) => [
          ...current,
          buildLocalJourneyEvent(requestId, "ui", "response_failed", "Ask request failed."),
        ]);
        setError("Ask request failed.");
      }
    } finally {
      setLoading(false);
    }
  };

  const globalInputFocus = useGlobalInputFocus({
    inputRef: questionInputRef,
    value: question,
    onValueChange: setQuestion,
    onSubmit: () => {
      void submit();
    },
    enabled: runtime.config.globalInputFocusEnabled && !loading,
    isModalOpen: isFeedbackModalOpen || isPackCatalogEmpty,
    clearOnEscape: true,
  });

  return (
    <>
      <div className="ask-layout">
        <div className="ask-main">
        <div className="card-light">
          <h2 className="card-title">Ask</h2>
          <p className="card-subtitle">
            Questions are sent to the live gateway with live pack anchor validation.
          </p>
          <div className="meta-grid">
            <span className="meta-label">Tenant</span>
            <span className="meta-value">{runtime.config.tenantId || "(not set)"}</span>
            <span className="meta-label">User</span>
            <span className="meta-value">{initialIdentity.userId || "(not set)"}</span>
            <span className="meta-label">Pack</span>
            <span className="meta-value">{selectedPackName || "Not selected"}</span>
          </div>

          <label className="label-stack" htmlFor="cwi-main-input">
            <span className="label-title">Question</span>
            <textarea
              id="cwi-main-input"
              ref={questionInputRef}
              className="textarea-input"
              placeholder="Ask a question"
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              onKeyDown={globalInputFocus.onKeyDown}
            />
          </label>
          <div className="web-toggle-row web-toggle-row--ask">
            <label
              className={`web-toggle ${!isTrustedWebAvailable ? "web-toggle--disabled" : ""}`}
              title={isTrustedWebAvailable ? "Use configured trusted sites" : "Trusted web context is not enabled"}
            >
              <input
                className="web-toggle-input"
                type="checkbox"
                checked={useWeb && isTrustedWebAvailable}
                disabled={!isTrustedWebAvailable || loading}
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
          <div className="ask-model-tier">
            <label className="ask-model-tier-label" htmlFor="ask-model-tier">
              Model tier
            </label>
            <select
              id="ask-model-tier"
              className="ask-model-tier-select"
              value={modelTier}
              onChange={(event) => {
                const value = event.target.value as "free" | "pro" | "edge";
                setModelTier(value);
                saveModelTier(value);
              }}
              disabled={loading}
            >
              <option value="free">Free</option>
              <option value="pro">Pro</option>
            </select>
          </div>
          {isOffline ? (
            <div
              className="alert alert-warning"
              role="status"
            >
              Offline mode: connect to the network to submit questions.
            </div>
          ) : null}
          {livePackError ? (
            <div
              className="alert alert-error"
              role="alert"
            >
              {livePackError}
            </div>
          ) : null}
          {error ? (
            <div
              className="alert alert-error"
              role="alert"
            >
              {error}
            </div>
          ) : null}
          {!selectedPackName ? (
            <div
              className="alert alert-warning"
              role="status"
            >
              No pack selected. <Link className="link-underline" href="/?modal=trust">Select a pack</Link> to continue.
            </div>
          ) : livePackStatus === "loading" ? (
            <div
              className="alert alert-warning"
              role="status"
            >
              Loading the selected pack from the live verified catalog...
            </div>
          ) : !pack ? (
            <div
              className="alert alert-warning"
              role="status"
            >
              Selected pack is not live verified right now. <Link className="link-underline" href="/?modal=trust">Refresh Trust</Link> to continue.
            </div>
          ) : null}
          <div className="panel-actions">
            <button
              className="btn-primary"
              onClick={submit}
              disabled={loading || isOffline || livePackStatus === "loading"}
            >
              {loading ? "Submitting..." : "Submit question"}
            </button>

          </div>
        </div>

        {response ? (
          <div id="ask-feedback-answer" className="card-light">
            <h3 className="card-title-sm">Answer</h3>
            <CitedAnswer
              answer={response.answer}
              citations={response.citations}
              trace={response.trace}
              className="answer-text"
            />
            {typeof response.trace?.confidence === "number" ? (
              <p className="answer-confidence">
                Confidence: {formatNumber(response.trace.confidence, { maximumFractionDigits: 3 })}
              </p>
            ) : null}
            <div className="answer-feedback-actions">
              <button
                className="answer-feedback-button"
                type="button"
                onClick={() => void submitAnswerFeedback("Helpful", "Low", "", false)}
                disabled={feedbackSubmitting}
              >
                Helpful
              </button>
              <button
                className="answer-feedback-button answer-feedback-button--issue"
                type="button"
                onClick={() => {
                  setFeedbackType(FEEDBACK_TYPES[0]);
                  setFeedbackSeverity("Medium");
                  setFeedbackComment("");
                  setFeedbackIncludeScreenshot(true);
                  setIsFeedbackModalOpen(true);
                }}
                disabled={feedbackSubmitting}
            >
                Report Issue
              </button>
            </div>
          </div>
        ) : null}
      </div>

        <div className="ask-side">
          <GlassboxPanel trace={response?.trace} citationsCount={response?.citations?.length} loading={loading} />
          <TracePanel trace={response?.trace} />
          <JourneyPanel
            requestId={journeyRequestId ?? response?.trace?.request_id}
            seedEvents={journeySeed}
            live={loading}
            defaultExpanded={loading}
            authMode={runtime.config.authMode}
            authIdleTimeoutMs={runtime.config.authIdleTimeoutMs}
          />
        </div>
      </div>
      {isFeedbackModalOpen && feedbackContext ? (
        <Modal ariaLabel="Report an issue with this answer" onClose={resetFeedbackModal} dismissible={!feedbackSubmitting}>
          <div className="feedback-modal">
            <div>
              <h2 className="card-title">Report an issue with this answer</h2>
              <p className="card-subtitle">Question: {feedbackContext.question}</p>
            </div>
            <label className="label-stack" htmlFor="ask-feedback-type">
              <span className="label-title">Feedback type</span>
              <select
                id="ask-feedback-type"
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
            <label className="label-stack" htmlFor="ask-feedback-severity">
              <span className="label-title">Severity</span>
              <select
                id="ask-feedback-severity"
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
            <label className="label-stack" htmlFor="ask-feedback-comment">
              <span className="label-title">Comment</span>
              <textarea
                id="ask-feedback-comment"
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
                  feedbackType,
                  feedbackSeverity,
                  feedbackComment,
                  feedbackIncludeScreenshot,
                )}
                disabled={feedbackSubmitting}
              >
                {feedbackSubmitting ? "Submitting..." : "Submit feedback"}
              </button>
              <button className="btn-secondary" type="button" onClick={resetFeedbackModal} disabled={feedbackSubmitting}>
                Cancel
              </button>
            </div>
          </div>
        </Modal>
      ) : null}
      {feedbackToast ? (
        <div className={`feedback-toast feedback-toast--${feedbackToast.kind}`} role="status" aria-live="polite">
          {feedbackToast.message}
        </div>
      ) : null}
      {isPackCatalogEmpty ? <NoValidPacksModal availabilitySummary={emptyStateSummary} /> : null}
    </>
  );
}
