import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  fetchRemoteAskHistory,
  loadAskHistorySettings,
  purgeLocalAskHistory,
  recordLocalAskHistory,
} from "@/lib/history";
import { saveOverrides } from "@/lib/storage";

function buildToken(payload: Record<string, unknown>): string {
  const encodedPayload = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `header.${encodedPayload}.signature`;
}

function setSessionUser(userId: string, tenantId = "tenant-1"): void {
  window.sessionStorage.setItem(
    "cwi.user.web.token.access",
    buildToken({ tenant_id: tenantId, sub: userId }),
  );
}

describe("history", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    saveOverrides({ authMode: "token", tenantId: "tenant-1", userId: "stale-device-user" });
  });

  afterEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it("keeps local ask history scoped to the authenticated user", () => {
    setSessionUser("user-a");
    recordLocalAskHistory({
      question: "What changed?",
      answer: "Auth is token-first now.",
    });

    const settings = loadAskHistorySettings();
    expect(purgeLocalAskHistory(settings)).toHaveLength(1);

    setSessionUser("user-b");
    expect(purgeLocalAskHistory(loadAskHistorySettings())).toHaveLength(0);

    setSessionUser("user-a");
    expect(purgeLocalAskHistory(loadAskHistorySettings())).toHaveLength(1);
  });

  it("preserves structured attachment metadata in local ask history", () => {
    setSessionUser("user-a");
    recordLocalAskHistory({
      question: "Describe this image.",
      answer: "It looks like a workflow diagram.",
      attachments: [
        {
          name: "workflow.png",
          mime_type: "image/png",
          size: 2048,
          kind: "image",
          preview_data_url: "data:image/webp;base64,abc123",
        },
      ],
    });

    const [item] = purgeLocalAskHistory(loadAskHistorySettings());
    expect(item?.attachments).toEqual([
      expect.objectContaining({
        name: "workflow.png",
        kind: "image",
        preview_data_url: "data:image/webp;base64,abc123",
      }),
    ]);
  });

  it("maps remote processing rows into pending account history items", async () => {
    const client = {
      historyList: async () => ({
        items: [
          {
            id: "hist-processing",
            request_id: "req-processing",
            conversation_id: "default",
            question: "Why is this taking long?",
            answer: "",
            response_status: "processing",
            created_at: "2026-05-03T12:00:00Z",
          },
        ],
      }),
    };

    const [item] = await fetchRemoteAskHistory(client, loadAskHistorySettings());

    expect(item?.requestId).toBe("req-processing");
    expect(item?.responseStatus).toBe("processing");
    expect(item?.answer).toBe("");
  });
});
