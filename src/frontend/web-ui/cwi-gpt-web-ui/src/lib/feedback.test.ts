import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { buildFeedbackReportRequest, loadFeedbackSessionId } from "./feedback";

describe("feedback", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  afterEach(() => {
    window.sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it("keeps a stable session id for feedback reports", () => {
    const first = loadFeedbackSessionId();
    const second = loadFeedbackSessionId();

    expect(first).toBeTruthy();
    expect(second).toBe(first);
  });

  it("builds report payload from answer trace context", () => {
    const payload = buildFeedbackReportRequest(
      {
        tenantId: "tenant-1",
        userId: "user-1",
        question: "what changed",
        answer: "One thing changed.",
        requestId: "request-1",
        traceId: "trace-1",
        selectedPackId: "enterprise",
        selectedPackVersion: "1.0.0",
        selectedPackDomain: "enterprise",
        uiPage: "ask",
        frontendAppVersion: "test-build",
        trace: {
          trace_id: "trace-1",
          request_id: "request-1",
          confidence: 0.82,
          route: "proactive_intelligence",
          intent_type: "daily_briefing",
          model: "model-1",
          memory_used: true,
          graph_used: false,
          pack_used: true,
          graph_hits: 0,
          document_hits: 1,
        },
      },
      { type: "Routing issue", severity: "High", comment: "Wrong mode." },
      "data:image/png;base64,abc",
      true,
    );

    expect(payload.request_id).toBe("request-1");
    expect(payload.trace_id).toBe("trace-1");
    expect(payload.selected_pack?.pack_id).toBe("enterprise");
    expect(payload.confidence).toBe(0.82);
    expect(payload.runtime_trace?.route).toBe("proactive_intelligence");
    expect(payload.runtime_trace?.retrieval_used).toBe(true);
    expect(payload.screenshot?.included).toBe(true);
    expect(payload.client?.frontend_app_version).toBe("test-build");
  });
});
