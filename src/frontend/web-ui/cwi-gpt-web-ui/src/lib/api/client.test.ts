import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, createApiClient } from "./client";

describe("api client", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("maps browser aborts to a user-facing timeout error", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn((_url: string | URL | Request, init?: RequestInit) => {
      const signal = init?.signal as AbortSignal | undefined;
      return new Promise<Response>((_resolve, reject) => {
        signal?.addEventListener("abort", () => {
          reject(new DOMException("signal is aborted without reason", "AbortError"));
        });
      });
    }));

    const client = createApiClient({ baseUrl: "", requestTimeoutMs: 1 });
    const request = client.historyList();
    const assertion = expect(request).rejects.toMatchObject({
      status: 408,
      message: expect.stringContaining("CWI is still working"),
      payload: {
        code: "client_timeout",
      },
    } satisfies Partial<ApiError>);
    await vi.advanceTimersByTimeAsync(1);

    await assertion;
  });

  it("gives ask requests enough time for governed runtime responses", async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn((_url: string | URL | Request, init?: RequestInit) => {
      const signal = init?.signal as AbortSignal | undefined;
      return new Promise<Response>((resolve, reject) => {
        signal?.addEventListener("abort", () => {
          reject(new DOMException("signal is aborted without reason", "AbortError"));
        });
        setTimeout(() => {
          resolve(new Response(JSON.stringify({ answer: "ok", citations: [], trace: {} }), { status: 200 }));
        }, 120001);
      });
    });
    vi.stubGlobal("fetch", fetchMock);

    const client = createApiClient({ baseUrl: "", requestTimeoutMs: 120000 });
    const request = client.ask({
      tenant_id: "tenant-1",
      question: "What is 2+2?",
      pack: { name: "cwi", anchor: "hash" },
      context: { mode: "observe", top_k: 3 },
    });
    await vi.advanceTimersByTimeAsync(120001);

    await expect(request).resolves.toMatchObject({ answer: "ok" });
  });

  it("posts feedback reports to the report endpoint", async () => {
    const fetchMock = vi.fn((_url: string | URL | Request, _init?: RequestInit) =>
      Promise.resolve(new Response(JSON.stringify({
        ok: true,
        feedback_id: "fbk-1",
        screenshot_stored: false,
        message: "Feedback submitted.",
      }), { status: 200 })),
    );
    vi.stubGlobal("fetch", fetchMock);

    const client = createApiClient({ baseUrl: "" });
    await client.feedbackReport({
      tenant_id: "tenant-1",
      user_id: "user-1",
      question: "question",
      answer: "answer",
      feedback: { type: "Helpful", severity: "Low" },
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/feedback/report",
      expect.objectContaining({
        method: "POST",
        body: expect.stringContaining('"type":"Helpful"'),
      }),
    );
  });

  it("passes trusted web retrieval context through ask requests", async () => {
    const fetchMock = vi.fn((_url: string | URL | Request, _init?: RequestInit) =>
      Promise.resolve(new Response(JSON.stringify({ answer: "ok", citations: [], trace: {} }), { status: 200 })),
    );
    vi.stubGlobal("fetch", fetchMock);

    const client = createApiClient({ baseUrl: "" });
    await client.ask({
      tenant_id: "tenant-1",
      question: "What changed?",
      pack: { name: "cwi", anchor: "hash" },
      context: {
        mode: "observe",
        top_k: 3,
        web_retrieval: {
          enabled: true,
          mode: "include",
          profile: "general",
          max_results: 4,
        },
      },
    });

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit;
    const body = JSON.parse(String(init.body));
    expect(body.context.web_retrieval).toEqual({
      enabled: true,
      mode: "include",
      profile: "general",
      max_results: 4,
    });
  });
});
