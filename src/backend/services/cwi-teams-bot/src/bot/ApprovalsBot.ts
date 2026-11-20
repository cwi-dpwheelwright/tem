import { ActivityHandler, TurnContext } from "botbuilder";
import {
  extractApprovalAction,
  handleApprovalAction,
} from "../services/approvalService.js";
import { handleBreakGlassAuditCommand } from "../services/breakGlassAudit.js";

export class ApprovalsBot extends ActivityHandler {
  constructor() {
    super();

    this.onMessage(async (context, next) => {
      const text = context.activity.text ?? "";
      if (text.trim().toLowerCase().startsWith("/breakglass-audit")) {
        const response = await handleBreakGlassAuditCommand(text);
        await context.sendActivity(response);
        await next();
        return;
      }

      const action = extractApprovalAction(context.activity);
      if (action) {
        const result = await handleApprovalAction(action, context.activity);
        await context.sendActivity(result.message);
      } else {
        await context.sendActivity(
          "CWI approvals bot online. No approval action detected."
        );
      }
      await next();
    });

    this.onMembersAdded(async (context, next) => {
      const membersAdded = context.activity.membersAdded ?? [];
      for (const member of membersAdded) {
        if (member.id !== context.activity.recipient?.id) {
          await context.sendActivity(
            "Welcome. This bot will surface approval tickets once configured."
          );
        }
      }
      await next();
    });

    this.onInvokeActivity = async (context) => {
      const text = context.activity.text ?? "";
      if (text.trim().toLowerCase().startsWith("/breakglass-audit")) {
        const response = await handleBreakGlassAuditCommand(text);
        await context.sendActivity(response);
        return { status: 200 };
      }

      const action = extractApprovalAction(context.activity);
      if (action) {
        const result = await handleApprovalAction(action, context.activity);
        await context.sendActivity(result.message);
        return { status: 200 };
      }

      await context.sendActivity("No approval action detected.");
      return { status: 200 };
    };
  }

  async handleError(context: TurnContext, error: Error) {
    // Keep a terse response and log full details server-side.
    // eslint-disable-next-line no-console
    console.error("Bot error:", error);
    await context.sendActivity("Sorry, something went wrong.");
  }
}
