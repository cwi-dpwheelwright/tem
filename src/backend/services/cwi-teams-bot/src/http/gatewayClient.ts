import { config } from "../config.js";
import { getGatewayToken } from "../auth/tokenProvider.js";
export type ApprovalCompleteRequest = {
  ticket_id: string;
  decision: "approve" | "deny";
  approver_id: string;
  reason?: string | null;
};

export type ApprovalTicketStatus = {
  ticket_id: string;
  status: string;
  risk: string;
  reason: string;
  updated_utc: string;
  ticket_type?: string;
  trace_id?: string;
  tenant_id?: string;
};

export type BreakGlassAuditPage = {
  page: number;
  pageSize: number;
  total: number;
  items: Array<{
    tokenId: string;
    tenantId: string;
    userId: string;
    role: string;
    scope: string;
    reason: string;
    createdUtc: string;
    expiresUtc: string;
    revokedUtc?: string | null;
  }>;
};

export async function completeApproval(
  request: ApprovalCompleteRequest,
  options?: { idempotencyKey?: string; routeOverride?: string }
): Promise<unknown> {
  const token = await getGatewayToken();
  const route =
    options?.routeOverride?.trim() || "/api/approval/complete";
  const response = await fetch(`${config.gatewayBaseUrl}${route}`, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      ...(options?.idempotencyKey ? { "x-idempotency-key": options.idempotencyKey } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`Approval completion failed: ${response.status} ${body}`);
  }

  return response.json();
}

export async function listApprovals(
  tenantId: string,
  limit = 50
): Promise<ApprovalTicketStatus[]> {
  const token = await getGatewayToken();
  const url = new URL(`${config.gatewayBaseUrl}/api/approvals`);
  url.searchParams.set("tenant_id", tenantId);
  url.searchParams.set("limit", String(limit));

  const response = await fetch(url.toString(), {
    method: "GET",
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`Approval list failed: ${response.status} ${body}`);
  }

  return response.json() as Promise<ApprovalTicketStatus[]>;
}

export async function queryBreakGlassAudit(
  tenantId: string,
  page = 1,
  pageSize = 20
): Promise<BreakGlassAuditPage> {
  const token = await getGatewayToken();
  const url = new URL(`${config.gatewayBaseUrl}/api/break-glass/audit`);
  url.searchParams.set("tenant_id", tenantId);
  url.searchParams.set("page", String(page));
  url.searchParams.set("page_size", String(pageSize));

  const response = await fetch(url.toString(), {
    method: "GET",
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`Break-glass audit failed: ${response.status} ${body}`);
  }

  return response.json() as Promise<BreakGlassAuditPage>;
}

export function resolveApprovalEndpoint(ticketType?: string | null): string | undefined {
  if (!ticketType || !config.approvalRouteOverrides) {
    return undefined;
  }
  try {
    const overrides = JSON.parse(config.approvalRouteOverrides) as Record<string, string>;
    return overrides[ticketType] || undefined;
  } catch {
    return undefined;
  }
}
