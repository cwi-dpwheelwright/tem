import { TestAdapter } from "botbuilder-core";
import { beforeEach, describe, expect, it, vi } from "vitest";

const completeApproval = vi.fn();
const queryBreakGlassAudit = vi.fn();
const resolveApprovalEndpoint = vi.fn();

vi.mock("../http/gatewayClient.js", () => ({
  completeApproval,
  queryBreakGlassAudit,
  resolveApprovalEndpoint,
}));

async function loadBot(allowlist = "approver-1") {
  vi.resetModules();
  process.env.GATEWAY_BASE_URL ||= "https://gateway.test";
  process.env.APPROVER_ALLOWLIST = allowlist;
  process.env.APPROVER_ROLE_MAP = "";
  const { ApprovalsBot } = await import("../bot/ApprovalsBot.js");
  return new ApprovalsBot();
}

describe("ApprovalsBot (adapter flow)", () => {
  beforeEach(() => {
    completeApproval.mockReset();
    queryBreakGlassAudit.mockReset();
    resolveApprovalEndpoint.mockReset();
  });

  it("processes approval actions with idempotency", async () => {
    completeApproval.mockResolvedValue({ ok: true });
    resolveApprovalEndpoint.mockReturnValue(undefined);

    const bot = await loadBot();
    const adapter = new TestAdapter(async (context) => bot.run(context));
    const activity = {
      type: "message",
      id: "activity-1",
      value: { ticket_id: "ticket-1", decision: "approve" },
      from: { id: "approver-1", aadObjectId: "approver-1", name: "Tester" },
      recipient: { id: "bot" },
      conversation: { id: "conv-1" },
      channelId: "msteams",
      serviceUrl: "https://example.com",
    };

    await adapter.processActivity(activity);
    const reply1 = adapter.getNextReply();
    expect(reply1?.text).toBe("Approval approved.");
    expect(completeApproval).toHaveBeenCalledTimes(1);

    await adapter.processActivity(activity);
    const reply2 = adapter.getNextReply();
    expect(reply2?.text).toBe("Approval already processed.");
    expect(completeApproval).toHaveBeenCalledTimes(1);
  });

  it("responds to breakglass audit commands", async () => {
    queryBreakGlassAudit.mockResolvedValue({
      page: 1,
      pageSize: 20,
      total: 1,
      items: [
        {
          tokenId: "token-1",
          tenantId: "tenant-1",
          userId: "user-1",
          role: "admin",
          scope: "all",
          reason: "incident",
          createdUtc: "2026-01-31T00:00:00Z",
          expiresUtc: "2026-02-01T00:00:00Z",
        },
      ],
    });

    const bot = await loadBot();
    const adapter = new TestAdapter(async (context) => bot.run(context));
    const activity = {
      type: "message",
      id: "activity-2",
      text: "/breakglass-audit tenant-1",
      from: { id: "approver-1", aadObjectId: "approver-1", name: "Tester" },
      recipient: { id: "bot" },
      conversation: { id: "conv-2" },
      channelId: "msteams",
      serviceUrl: "https://example.com",
    };

    await adapter.processActivity(activity);
    const reply = adapter.getNextReply();
    expect(reply?.text).toContain("Break-glass audit (tenant=tenant-1)");
  });

  it("rejects approvers not in the allowlist", async () => {
    completeApproval.mockResolvedValue({ ok: true });
    const bot = await loadBot("approver-allow");
    const adapter = new TestAdapter(async (context) => bot.run(context));
    const activity = {
      type: "message",
      id: "activity-3",
      value: { ticket_id: "ticket-2", decision: "deny" },
      from: { id: "approver-deny", aadObjectId: "approver-deny", name: "Tester" },
      recipient: { id: "bot" },
      conversation: { id: "conv-3" },
      channelId: "msteams",
      serviceUrl: "https://example.com",
    };

    await adapter.processActivity(activity);
    const reply = adapter.getNextReply();
    expect(reply?.text).toBe("Approver not in allowlist.");
    expect(completeApproval).toHaveBeenCalledTimes(0);
  });
});
