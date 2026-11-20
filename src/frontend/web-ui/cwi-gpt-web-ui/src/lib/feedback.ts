import type { FeedbackReportRequest, Trace } from "./api/types";

const FEEDBACK_SESSION_KEY = "cwi.user.web.feedback.session";
const SCREENSHOT_MAX_WIDTH = 1280;
const SCREENSHOT_MAX_HEIGHT = 1600;

export const FEEDBACK_TYPES = [
  "Wrong answer",
  "Confusing answer",
  "Too long",
  "Too short",
  "Routing issue",
  "Memory issue",
  "Connector issue",
  "Tone issue",
  "Other",
] as const;

export const FEEDBACK_SEVERITIES = ["Low", "Medium", "High", "Critical"] as const;

export interface FeedbackAnswerContext {
  tenantId: string;
  userId: string;
  question: string;
  answer: string;
  requestId?: string | null;
  traceId?: string | null;
  selectedPackId?: string | null;
  selectedPackVersion?: string | null;
  selectedPackDomain?: string | null;
  trace?: Trace | null;
  uiPage: string;
  frontendAppVersion?: string | null;
}

export function loadFeedbackSessionId(): string {
  if (typeof window === "undefined") {
    return "server";
  }

  const existing = window.sessionStorage.getItem(FEEDBACK_SESSION_KEY);
  if (existing) {
    return existing;
  }

  const value = crypto.randomUUID();
  window.sessionStorage.setItem(FEEDBACK_SESSION_KEY, value);
  return value;
}

export function buildFeedbackReportRequest(
  context: FeedbackAnswerContext,
  feedback: { type: string; severity: string; comment?: string | null },
  screenshotDataUrl?: string | null,
  screenshotRequested = Boolean(screenshotDataUrl),
): FeedbackReportRequest {
  const trace = context.trace;
  const retrievalUsed = Boolean(
    (trace?.document_hits ?? 0) > 0 ||
    (trace?.graph_hits ?? 0) > 0 ||
    (trace?.trusted_web_hits ?? 0) > 0,
  );

  return {
    tenant_id: context.tenantId,
    user_id: context.userId,
    session_id: loadFeedbackSessionId(),
    request_id: context.requestId ?? trace?.request_id ?? null,
    trace_id: context.traceId ?? trace?.trace_id ?? null,
    selected_pack: {
      pack_id: context.selectedPackId ?? null,
      pack_version: context.selectedPackVersion ?? trace?.pack_version ?? null,
      domain: context.selectedPackDomain ?? context.selectedPackId ?? null,
    },
    question: context.question,
    answer: context.answer,
    confidence: trace?.confidence ?? null,
    runtime_trace: {
      route: trace?.route ?? null,
      intent: trace?.intent_type ?? trace?.intent_mode ?? null,
      model_used: trace?.model ?? null,
      memory_used: trace?.memory_used ?? null,
      graph_used: trace?.graph_used ?? null,
      retrieval_used: retrievalUsed,
      pack_used: trace?.pack_used ?? null,
      connector_context_used: false,
      latency_ms: null,
    },
    feedback,
    screenshot: {
      included: screenshotRequested,
      data_url: screenshotDataUrl ?? null,
    },
    client: {
      ui_page: context.uiPage,
      frontend_app_version: context.frontendAppVersion ?? null,
      browser_user_agent: typeof navigator === "undefined" ? null : navigator.userAgent,
    },
  };
}

export async function captureElementScreenshotDataUrl(element: HTMLElement): Promise<string | null> {
  const rect = element.getBoundingClientRect();
  const width = Math.max(1, Math.ceil(rect.width));
  const height = Math.max(1, Math.ceil(rect.height));
  const scale = Math.min(1, SCREENSHOT_MAX_WIDTH / width, SCREENSHOT_MAX_HEIGHT / height);
  const clone = element.cloneNode(true) as HTMLElement;
  clone.style.width = `${width}px`;
  clone.style.minHeight = `${height}px`;
  clone.style.margin = "0";
  clone.style.transform = "none";

  const styles = collectStyleText();
  const serialized = new XMLSerializer().serializeToString(clone);
  const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">
      <foreignObject width="100%" height="100%">
        <div xmlns="http://www.w3.org/1999/xhtml">
          <style>${escapeStyle(styles)}</style>
          ${serialized}
        </div>
      </foreignObject>
    </svg>`;

  const image = await loadSvgImage(svg);
  const canvas = document.createElement("canvas");
  canvas.width = Math.max(1, Math.floor(width * scale));
  canvas.height = Math.max(1, Math.floor(height * scale));
  const context = canvas.getContext("2d");
  if (!context) {
    return null;
  }

  context.drawImage(image, 0, 0, canvas.width, canvas.height);
  return canvas.toDataURL("image/png");
}

function collectStyleText(): string {
  const parts: string[] = [];
  for (const sheet of Array.from(document.styleSheets)) {
    try {
      for (const rule of Array.from(sheet.cssRules)) {
        parts.push(rule.cssText);
      }
    } catch {
      continue;
    }
  }
  return parts.join("\n");
}

function escapeStyle(value: string): string {
  return value.replace(/<\/style/gi, "<\\/style");
}

function loadSvgImage(svg: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("screenshot_capture_failed"));
    image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
  });
}
