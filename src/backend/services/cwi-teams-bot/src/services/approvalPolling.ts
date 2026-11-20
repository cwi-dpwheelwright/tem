import { config } from "../config.js";
import { listApprovals } from "../http/gatewayClient.js";
import { notifyApproval } from "./approvalNotifications.js";

const lastSeen = new Map<string, number>();

function parseUpdated(value: string): number {
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? Date.now() : parsed;
}

export async function pollApprovalsOnce(): Promise<void> {
  if (config.approvalTenantIds.length === 0) {
    return;
  }

  for (const tenantId of config.approvalTenantIds) {
    const tickets = await listApprovals(tenantId, config.approvalPollLimit);
    for (const ticket of tickets) {
      const updated = parseUpdated(ticket.updated_utc);
      const key = ticket.ticket_id;
      const previous = lastSeen.get(key) ?? 0;
      if (updated <= previous) {
        continue;
      }

      if (ticket.status.toLowerCase() === "pending") {
        await notifyApproval({
          ticket_id: ticket.ticket_id,
          status: ticket.status,
          risk: ticket.risk,
          reason: ticket.reason,
          tenant_id: tenantId,
          ticket_type: ticket.ticket_type,
          trace_id: ticket.trace_id,
        });
      }

      lastSeen.set(key, updated);
    }
  }
}

export function startApprovalPolling(): void {
  if (config.approvalPollIntervalSeconds <= 0) {
    return;
  }

  void pollApprovalsOnce().catch(() => {
    // swallow initial poll errors to avoid crashing the bot
  });

  setInterval(() => {
    void pollApprovalsOnce().catch(() => {
      // swallow errors; logs handled upstream
    });
  }, config.approvalPollIntervalSeconds * 1000);
}
