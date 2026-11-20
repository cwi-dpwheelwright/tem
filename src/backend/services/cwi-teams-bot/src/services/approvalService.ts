import type { Activity } from "botbuilder";
import { authorizeApprover } from "../auth/approverAuthorization.js";
import { completeApproval } from "../http/gatewayClient.js";
import { createIdempotencyStore, IdempotencyRecord } from "../idempotency/store.js";
import { config } from "../config.js";
import { recordApprovalAction } from "../observability/metrics.js";
import { createApprovalCard } from "../bot/approvalCard.js";
import { resolveApprovalEndpoint } from "../http/gatewayClient.js";

type ApprovalDecision = "approve" | "deny";

export type ApprovalAction = {
  ticketId: string;
  decision: ApprovalDecision;
  ticketType?: string | null;
  reason?: string | null;
};

export type ApprovalCardRequest = {
  ticketId: string;
  risk: string;
  reason: string;
  ticketType?: string | null;
  traceId?: string | null;
  traceUrl?: string | null;
  details?: Array<{ title: string; value: string }>;
};

const store = createIdempotencyStore();

export async function handleApprovalAction(
  action: ApprovalAction,
  activity: Activity
): Promise<{ message: string; response?: unknown }> {
  const approverId = activity.from?.aadObjectId ?? activity.from?.id ?? "unknown";
  const approverName = activity.from?.name ?? "";
  const approverAadId = activity.from?.aadObjectId ?? "";

  const authorization = authorizeApprover({
    id: approverId,
    name: approverName,
    aadObjectId: approverAadId,
  });
  if (!authorization.ok) {
    return { message: authorization.reason ?? "Unauthorized." };
  }

  const idempotencyKey = [
    action.ticketId,
    action.decision,
    approverId,
    activity.id ?? "",
  ].join(":");

  const existing = await store.get(idempotencyKey);
  if (existing) {
    return {
      message: "Approval already processed.",
      response: existing.response,
    };
  }

  const request = {
    ticket_id: action.ticketId,
    decision: action.decision,
    approver_id: approverId,
    reason: action.reason ?? null,
  };

  const response = await completeApproval(request, {
    idempotencyKey,
    routeOverride: resolveApprovalEndpoint(action.ticketType),
  });

  const record: IdempotencyRecord = {
    key: idempotencyKey,
    ticketId: action.ticketId,
    decision: action.decision,
    approverId,
    approverName,
    approverAadId,
    activityId: activity.id,
    conversationId: activity.conversation?.id,
    reason: action.reason ?? null,
    timestamp: new Date().toISOString(),
    response,
  };

  await store.set(record, config.idempotencyTtlSeconds);
  recordApprovalAction(action.decision);

  return { message: `Approval ${action.decision}d.`, response };
}

export function extractApprovalAction(activity: Activity): ApprovalAction | null {
  const value = (activity.value ?? {}) as Record<string, unknown>;

  const ticketId =
    (value.ticket_id as string | undefined) ||
    (value.ticketId as string | undefined) ||
    ((value.approval_ticket as { ticket_id?: string } | undefined)?.ticket_id ??
      undefined);

  const decision =
    (value.decision as ApprovalDecision | undefined) ||
    (value.action as ApprovalDecision | undefined);

  if (!ticketId || !decision) {
    return null;
  }

  const reason =
    (value.reason as string | undefined) ||
    (value.comment as string | undefined) ||
    null;

  const ticketType =
    (value.ticket_type as string | undefined) ||
    (value.ticketType as string | undefined) ||
    null;

  return { ticketId, decision, reason, ticketType };
}

export function buildApprovalCard(request: ApprovalCardRequest) {
  return createApprovalCard({
    ticketId: request.ticketId,
    risk: request.risk,
    reason: request.reason,
    ticketType: request.ticketType ?? undefined,
    traceId: request.traceId ?? undefined,
    traceUrl: request.traceUrl ?? undefined,
    details: request.details,
  });
}
