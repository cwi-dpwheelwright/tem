export type ApprovalCardPayload = {
  ticketId: string;
  risk: string;
  reason: string;
  ticketType?: string;
  traceId?: string;
  traceUrl?: string;
  details?: Array<{ title: string; value: string }>;
};

export function createApprovalCard(payload: ApprovalCardPayload) {
  const traceLink =
    payload.traceUrl ?? (payload.traceId ? `trace:${payload.traceId}` : "");
  const typeLabel = payload.ticketType
    ? payload.ticketType.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase())
    : "General Approval";

  const facts = [
    { title: "Ticket ID", value: payload.ticketId },
    { title: "Type", value: typeLabel },
    { title: "Risk", value: payload.risk },
    { title: "Reason", value: payload.reason },
    { title: "Trace", value: traceLink || "n/a" },
  ];

  if (payload.details && payload.details.length > 0) {
    for (const detail of payload.details) {
      facts.push(detail);
    }
  }

  return {
    type: "AdaptiveCard",
    version: "1.5",
    body: [
      {
        type: "TextBlock",
        text: `CWI Approval Required: ${typeLabel}`,
        weight: "Bolder",
        size: "Medium",
      },
      {
        type: "FactSet",
        facts,
      },
    ],
    actions: [
      {
        type: "Action.Submit",
        title: "Approve",
        data: {
          ticket_id: payload.ticketId,
          ticket_type: payload.ticketType,
          decision: "approve",
        },
      },
      {
        type: "Action.Submit",
        title: "Deny",
        data: {
          ticket_id: payload.ticketId,
          ticket_type: payload.ticketType,
          decision: "deny",
        },
      },
    ],
  };
}
