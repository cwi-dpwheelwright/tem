import { queryBreakGlassAudit } from "../http/gatewayClient.js";
import { recordBreakGlassAuditQuery } from "../observability/metrics.js";

type AuditCommandArgs = {
  tenantId: string;
  page: number;
  pageSize: number;
};

function parseArgs(text: string): AuditCommandArgs | null {
  const tokens = text.trim().split(/\s+/);
  if (tokens.length < 2) {
    return null;
  }
  const tenantId = tokens[1];
  const page = tokens[2] ? Number(tokens[2]) : 1;
  const pageSize = tokens[3] ? Number(tokens[3]) : 20;
  if (!tenantId) {
    return null;
  }
  return {
    tenantId,
    page: Number.isFinite(page) && page > 0 ? page : 1,
    pageSize: Number.isFinite(pageSize) && pageSize > 0 ? pageSize : 20,
  };
}

function formatItem(item: {
  tokenId: string;
  userId: string;
  role: string;
  scope: string;
  createdUtc: string;
  expiresUtc: string;
  revokedUtc?: string | null;
}) {
  const status = item.revokedUtc ? "revoked" : "active";
  return `- ${item.tokenId} user=${item.userId} role=${item.role} scope=${item.scope} status=${status} created=${item.createdUtc}`;
}

export async function handleBreakGlassAuditCommand(text: string): Promise<string> {
  const args = parseArgs(text);
  if (!args) {
    return "Usage: /breakglass-audit <tenant_id> [page] [page_size]";
  }

  const page = await queryBreakGlassAudit(args.tenantId, args.page, args.pageSize);
  recordBreakGlassAuditQuery();

  const header = `Break-glass audit (tenant=${args.tenantId}) page ${page.page}/${Math.ceil(
    page.total / page.pageSize
  )} total=${page.total}`;

  if (page.items.length === 0) {
    return `${header}\nNo results.`;
  }

  const lines = page.items.slice(0, 10).map(formatItem);
  if (page.items.length > 10) {
    lines.push(`...and ${page.items.length - 10} more`);
  }

  return `${header}\n${lines.join("\n")}`;
}

