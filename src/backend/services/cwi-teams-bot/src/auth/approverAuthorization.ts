import { config } from "../config.js";

type RoleMap = Record<string, string[]>;

const allowedRoles = new Set(["approver", "admin"]);

function parseAllowlist(raw: string): Set<string> {
  return new Set(
    raw
      .split(",")
      .map((entry) => entry.trim())
      .filter(Boolean)
  );
}

function parseRoleMap(raw: string): RoleMap | null {
  if (!raw.trim()) {
    return null;
  }
  try {
    const parsed = JSON.parse(raw) as RoleMap;
    return parsed;
  } catch {
    return null;
  }
}

const approverAllowlist = parseAllowlist(config.approverAllowlist);
const approverRoleMap = parseRoleMap(config.approverRoleMap);

export type ApproverIdentity = {
  id: string;
  name?: string;
  aadObjectId?: string;
};

export function authorizeApprover(identity: ApproverIdentity): {
  ok: boolean;
  reason?: string;
  roles?: string[];
} {
  const identifiers = new Set(
    [identity.id, identity.aadObjectId, identity.name].filter(Boolean) as string[]
  );

  if (approverAllowlist.size > 0) {
    const isAllowed = [...identifiers].some((id) => approverAllowlist.has(id));
    if (!isAllowed) {
      return { ok: false, reason: "Approver not in allowlist." };
    }
  }

  if (approverRoleMap) {
    const roles =
      (identity.aadObjectId && approverRoleMap[identity.aadObjectId]) ||
      (identity.id && approverRoleMap[identity.id]) ||
      [];
    const hasRole = roles.some((role) => allowedRoles.has(role));
    if (!hasRole) {
      return { ok: false, reason: "Approver lacks required role.", roles };
    }
    return { ok: true, roles };
  }

  return { ok: true };
}
