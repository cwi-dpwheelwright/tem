import { describe, expect, it } from "vitest";
import { createApprovalCard } from "../bot/approvalCard.js";

describe("approval card", () => {
  it("renders key fields", () => {
    const card = createApprovalCard({
      ticketId: "ticket-1",
      risk: "high",
      reason: "policy breach",
      traceId: "trace-123",
    });

    expect(card.type).toBe("AdaptiveCard");
    const factSet = card.body.find((entry) => entry.type === "FactSet") as {
      facts: Array<{ title: string; value: string }>;
    };
    expect(factSet.facts.some((fact) => fact.value === "ticket-1")).toBe(true);
    expect(factSet.facts.some((fact) => fact.value === "high")).toBe(true);
  });

  it("renders ticket type and details", () => {
    const card = createApprovalCard({
      ticketId: "ticket-2",
      risk: "medium",
      reason: "guardrail triggered",
      ticketType: "guardrail",
      details: [{ title: "Policy", value: "P-12" }],
    });

    const factSet = card.body.find((entry) => entry.type === "FactSet") as {
      facts: Array<{ title: string; value: string }>;
    };
    expect(factSet.facts.some((fact) => fact.value === "Guardrail")).toBe(true);
    expect(factSet.facts.some((fact) => fact.value === "P-12")).toBe(true);
  });
});
