"use client";

import { useCallback, useEffect, useState } from "react";
import { apiGet, apiPost, apiPut } from "@/lib/identity/client";
import { useAuth } from "@/lib/auth/context";
import { getRuntimeConfigStatus } from "@/lib/config";

type UserSummary = {
  user_id: string;
  email: string;
  username?: string | null;
  display_name: string;
  status: string;
  token_version: number;
  roles: string[];
};

type Role = {
  role_id: string;
  name: string;
};

type TenantSummary = {
  tenant_id: string;
  name: string;
  status: string;
};

type HomeNodeUserLink = {
  tenant_id: string;
  user_id: string;
  linked: boolean;
  node_id?: string | null;
  node_status?: string | null;
  tailscale_ip?: string | null;
};

function maskToken(token: string): string {
  if (token.length <= 8) {
    return "••••";
  }
  return `${token.slice(0, 4)}••••${token.slice(-4)}`;
}

function confirmAction(message: string): boolean {
  if (typeof window === "undefined") {
    return true;
  }
  return window.confirm(message);
}

async function apiGetOrchestrator<T>(path: string, accessToken?: string | null) {
  const baseUrl = getRuntimeConfigStatus().config?.apiBaseUrl;
  if (!baseUrl) {
    return { ok: false, status: 0, error: "API base URL is not configured." };
  }

  const headers = new Headers();
  headers.set("Content-Type", "application/json");
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }

  try {
    const response = await fetch(`${baseUrl}${path}`, { method: "GET", headers });
    const text = await response.text();
    if (!response.ok) {
      return { ok: false, status: response.status, error: text || response.statusText };
    }
    if (!text) {
      return { ok: true, status: response.status };
    }
    try {
      return { ok: true, status: response.status, data: JSON.parse(text) as T };
    } catch {
      return { ok: true, status: response.status };
    }
  } catch (error) {
    return {
      ok: false,
      status: 0,
      error: error instanceof Error ? error.message : "Request failed.",
    };
  }
}

export default function UsersPanel({ tenantId }: { tenantId: string }) {
  const { accessToken } = useAuth();
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [tenants, setTenants] = useState<TenantSummary[]>([]);
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteUsername, setInviteUsername] = useState("");
  const [inviteName, setInviteName] = useState("");
  const [passwordUserId, setPasswordUserId] = useState("");
  const [passwordValue, setPasswordValue] = useState("");
  const [switchTenantByUser, setSwitchTenantByUser] = useState<Record<string, string>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [inviteToken, setInviteToken] = useState("");
  const [showInviteToken, setShowInviteToken] = useState(false);
  const [homeNodeLinksByUser, setHomeNodeLinksByUser] = useState<Record<string, HomeNodeUserLink>>({});
  const [homeNodeMessage, setHomeNodeMessage] = useState<string | null>(null);

  const loadUsers = useCallback(
    async (targetTenant: string) => {
      if (!targetTenant || !accessToken) {
        return;
      }
      const response = await apiGet<UserSummary[]>(
        `/admin/users?tenant_id=${encodeURIComponent(targetTenant)}`,
        accessToken,
      );
      if (response.ok && response.data) {
        setUsers(response.data);
        setSwitchTenantByUser({});
        setMessage(null);
      } else {
        setMessage(response.error || "Failed to load users.");
      }
    },
    [accessToken],
  );

  const loadRoles = useCallback(
    async (targetTenant: string) => {
      if (!targetTenant || !accessToken) {
        return;
      }
      const response = await apiGet<Role[]>(
        `/admin/roles?tenant_id=${encodeURIComponent(targetTenant)}`,
        accessToken,
      );
      if (response.ok && response.data) {
        setRoles(response.data);
      }
    },
    [accessToken],
  );

  const loadHomeNodeLinks = useCallback(
    async (targetTenant: string) => {
      if (!targetTenant || !accessToken) {
        return;
      }

      const response = await apiGetOrchestrator<HomeNodeUserLink[]>(
        `/api/admin/home-node-user-links?tenant_id=${encodeURIComponent(targetTenant)}`,
        accessToken,
      );
      if (response.ok && response.data) {
        const links = response.data.reduce<Record<string, HomeNodeUserLink>>((current, link) => {
          if (link.linked) {
            current[link.user_id] = link;
          }
          return current;
        }, {});
        setHomeNodeLinksByUser(links);
        setHomeNodeMessage(null);
      } else {
        setHomeNodeLinksByUser({});
        setHomeNodeMessage(response.error || "Failed to load home-node link status.");
      }
    },
    [accessToken],
  );

  const loadTenants = useCallback(async () => {
    if (!accessToken) {
      return;
    }
    const response = await apiGet<TenantSummary[]>("/admin/tenants", accessToken);
    if (response.ok && response.data) {
      setTenants(response.data);
    }
  }, [accessToken]);

  useEffect(() => {
    if (tenantId) {
      void loadUsers(tenantId);
      void loadRoles(tenantId);
      void loadHomeNodeLinks(tenantId);
    }
  }, [tenantId, loadUsers, loadRoles, loadHomeNodeLinks]);

  useEffect(() => {
    void loadTenants();
  }, [loadTenants]);

  const invite = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!accessToken) {
      setMessage("Sign in with an admin account to invite users.");
      return;
    }
    if (!tenantId) {
      setMessage("Select a tenant before inviting users.");
      return;
    }
    const response = await apiPost<{ invite_token: string }>(
      "/admin/users/invite",
      {
        tenant_id: tenantId,
        email: inviteEmail,
        username: inviteUsername || undefined,
        display_name: inviteName,
      },
      accessToken,
    );
    if (response.ok && response.data) {
      setInviteToken(response.data.invite_token);
      setShowInviteToken(false);
      setInviteEmail("");
      setInviteUsername("");
      setInviteName("");
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
    } else {
      setMessage(response.error || "Failed to invite user.");
    }
  };

  const revoke = async (user: UserSummary) => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to revoke users.");
      return;
    }
    if (
      !confirmAction(
        `Revoke access for ${user.email}?\n\nTenant: ${tenantId}\nUser ID: ${user.user_id}`,
      )
    ) {
      setMessage("Revoke action cancelled.");
      return;
    }
    const response = await apiPost(
      `/admin/users/${user.user_id}/revoke`,
      { tenant_id: tenantId, reason: "admin revoke" },
      accessToken,
    );
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage(`Revoked ${user.email}.`);
    } else {
      setMessage(response.error || "Failed to revoke user.");
    }
  };

  const removeFromTenant = async (user: UserSummary) => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to remove users.");
      return;
    }
    if (
      !confirmAction(
        `Remove ${user.email} from tenant ${tenantId}?\n\nThis revokes sessions, clears roles, and removes the user from this tenant roster.`,
      )
    ) {
      setMessage("Remove action cancelled.");
      return;
    }
    const response = await apiPost(
      `/admin/users/${user.user_id}/remove`,
      { tenant_id: tenantId, reason: "admin remove from tenant" },
      accessToken,
    );
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage(`Removed ${user.email} from ${tenantId}.`);
    } else {
      setMessage(response.error || "Failed to remove user from tenant.");
    }
  };

  const switchTenant = async (user: UserSummary) => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to switch users.");
      return;
    }
    const targetTenantId = switchTenantByUser[user.user_id];
    if (!targetTenantId) {
      setMessage("Choose a target tenant before switching the user.");
      return;
    }
    if (
      !confirmAction(
        `Switch ${user.email} from ${tenantId} to ${targetTenantId}?\n\nThis revokes old sessions and clears source-tenant roles.`,
      )
    ) {
      setMessage("Tenant switch cancelled.");
      return;
    }
    const response = await apiPost(
      `/admin/users/${user.user_id}/tenant`,
      { tenant_id: tenantId, target_tenant_id: targetTenantId },
      accessToken,
    );
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage(`Moved ${user.email} to ${targetTenantId}.`);
    } else {
      setMessage(response.error || "Failed to switch user tenant.");
    }
  };

  const assignRoles = async (user: UserSummary, nextRoles: string[]) => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to assign roles.");
      return;
    }
    const response = await apiPut(
      `/admin/users/${user.user_id}/roles`,
      { tenant_id: tenantId, roles: nextRoles },
      accessToken,
    );
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
    } else {
      setMessage(response.error || "Failed to assign roles.");
    }
  };

  const approve = async (user: UserSummary) => {
    if (!accessToken) {
      setMessage("Sign in with an admin account to approve users.");
      return;
    }
    if (
      !confirmAction(
        `Approve ${user.email}?\n\nTenant: ${tenantId}\nUser ID: ${user.user_id}`,
      )
    ) {
      setMessage("Approve action cancelled.");
      return;
    }
    const response = await apiPost(
      `/admin/users/${user.user_id}/approve`,
      { tenant_id: tenantId },
      accessToken,
    );
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage(`Approved ${user.email}.`);
    } else {
      setMessage(response.error || "Failed to approve user.");
    }
  };

  const setPassword = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!accessToken) {
      setMessage("Sign in with an admin account to set passwords.");
      return;
    }
    if (!passwordUserId || !passwordValue) {
      setMessage("Select a user and enter a password.");
      return;
    }
    const targetUser = users.find((user) => user.user_id === passwordUserId);
    const targetLabel = targetUser
      ? `${targetUser.email} (${targetUser.username || "no-username"})`
      : passwordUserId;
    if (
      !confirmAction(
        `Set a new password for ${targetLabel}?\n\nTenant: ${tenantId}\nUser ID: ${passwordUserId}`,
      )
    ) {
      setMessage("Password reset cancelled.");
      return;
    }
    const response = await apiPost(
      `/admin/users/${passwordUserId}/password`,
      { tenant_id: tenantId, password: passwordValue },
      accessToken,
    );
    if (response.ok) {
      setPasswordUserId("");
      setPasswordValue("");
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage("Password set and user activated.");
    } else {
      setMessage(response.error || "Failed to set password.");
    }
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Users</h2>
          <p className="text-sm text-slate-300">Invite, activate, move, remove, and manage user roles.</p>
        </div>
        <button
          className="rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.2em] text-slate-200"
          type="button"
          onClick={() => {
            void loadUsers(tenantId);
            void loadHomeNodeLinks(tenantId);
          }}
        >
          Refresh
        </button>
      </header>

      {!tenantId && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          Enter a tenant ID above to manage users.
        </div>
      )}

      <form className="mt-6 grid gap-4 md:grid-cols-[1fr_1fr_1fr_auto]" onSubmit={invite}>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Invite email
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={inviteEmail}
            onChange={(e) => setInviteEmail(e.target.value)}
            required
            disabled={!tenantId}
          />
        </label>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Username (optional)
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={inviteUsername}
            onChange={(e) => setInviteUsername(e.target.value)}
            disabled={!tenantId}
          />
        </label>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Display name
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={inviteName}
            onChange={(e) => setInviteName(e.target.value)}
            disabled={!tenantId}
          />
        </label>
        <button
          className="h-fit rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
          type="submit"
          disabled={!tenantId}
        >
          Send invite
        </button>
      </form>

      <form className="mt-4 grid gap-4 md:grid-cols-[1fr_1fr_auto]" onSubmit={setPassword}>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Set password for user
          <select
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={passwordUserId}
            onChange={(event) => setPasswordUserId(event.target.value)}
            required
            disabled={!tenantId}
          >
            <option value="">Select user</option>
            {users.map((user) => (
              <option key={user.user_id} value={user.user_id}>
                {user.email} ({user.username || "no-username"})
              </option>
            ))}
          </select>
        </label>
        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          New password
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={passwordValue}
            onChange={(event) => setPasswordValue(event.target.value)}
            type="password"
            required
            disabled={!tenantId}
          />
        </label>
        <button
          className="h-fit rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
          type="submit"
          disabled={!tenantId}
        >
          Set password
        </button>
      </form>

      {inviteToken && (
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-emerald-500/40 bg-emerald-500/10 p-4 text-sm text-emerald-100">
          <div>
            <p className="text-xs uppercase tracking-[0.2em] text-emerald-200">Invite token</p>
            <p className="mt-2 font-mono text-sm">
              {showInviteToken ? inviteToken : maskToken(inviteToken)}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              className="rounded-full border border-emerald-200/40 px-3 py-1 text-xs uppercase tracking-[0.2em]"
              type="button"
              onClick={() => setShowInviteToken((prev) => !prev)}
            >
              {showInviteToken ? "Hide" : "Reveal"}
            </button>
            <button
              className="rounded-full border border-emerald-200/40 px-3 py-1 text-xs uppercase tracking-[0.2em]"
              type="button"
              onClick={() => void navigator.clipboard.writeText(inviteToken)}
            >
              Copy
            </button>
          </div>
        </div>
      )}

      {message && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          {message}
        </div>
      )}
      {homeNodeMessage && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          {homeNodeMessage}
        </div>
      )}

      <div className="mt-6 space-y-3">
        <div className="grid grid-cols-[1.15fr_0.75fr_0.65fr_0.9fr_0.55fr_1.1fr_1fr] gap-3 text-xs uppercase tracking-[0.2em] text-slate-400">
          <div>User</div>
          <div>Username</div>
          <div>Status</div>
          <div>Home node</div>
          <div>Token</div>
          <div>Roles</div>
          <div>Actions</div>
        </div>
        {users.length === 0 ? (
          <div className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
            No users loaded.
          </div>
        ) : (
          users.map((user) => (
            <div
              key={user.user_id}
              className="grid grid-cols-[1.15fr_0.75fr_0.65fr_0.9fr_0.55fr_1.1fr_1fr] gap-3 rounded-2xl border border-white/10 bg-white/5 p-3 text-sm text-slate-200"
            >
              <div className="space-y-1">
                <div className="font-semibold text-white">{user.email}</div>
                <div className="text-xs text-slate-400">{user.display_name}</div>
              </div>
              <div>{user.username || "-"}</div>
              <div>{user.status}</div>
              <div className="space-y-1">
                {homeNodeLinksByUser[user.user_id]?.linked ? (
                  <>
                    <span className="inline-flex rounded-full border border-emerald-300/40 bg-emerald-500/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-emerald-100">
                      Linked
                    </span>
                    {homeNodeLinksByUser[user.user_id]?.node_id && (
                      <div className="break-all font-mono text-xs text-slate-300">
                        {homeNodeLinksByUser[user.user_id]?.node_id}
                      </div>
                    )}
                    {homeNodeLinksByUser[user.user_id]?.tailscale_ip && (
                      <div className="text-xs text-slate-400">{homeNodeLinksByUser[user.user_id]?.tailscale_ip}</div>
                    )}
                  </>
                ) : (
                  <>
                    <span className="inline-flex rounded-full border border-slate-500/40 bg-slate-500/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-slate-200">
                      Unlinked
                    </span>
                    <div className="text-xs text-slate-500">No active node</div>
                  </>
                )}
              </div>
              <div>{user.token_version}</div>
              <select
                className="h-24 w-full rounded-xl border border-white/10 bg-slate-950/40 px-2 py-1 text-sm text-white"
                multiple
                value={user.roles}
                onChange={(event) => {
                  const selected = Array.from(event.target.selectedOptions).map((option) => option.value);
                  void assignRoles(user, selected);
                }}
              >
                {roles.map((role) => (
                  <option key={role.role_id} value={role.name}>
                    {role.name}
                  </option>
                ))}
              </select>
              <div className="flex flex-wrap gap-2">
                <button
                  className="rounded-full border border-emerald-500/40 bg-emerald-500/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-emerald-100"
                  type="button"
                  onClick={() => void approve(user)}
                >
                  Approve
                </button>
                <button
                  className="rounded-full border border-red-500/40 bg-red-500/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-red-100"
                  type="button"
                  onClick={() => void revoke(user)}
                >
                  Revoke
                </button>
                <button
                  className="rounded-full border border-orange-500/40 bg-orange-500/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-orange-100"
                  type="button"
                  onClick={() => void removeFromTenant(user)}
                >
                  Remove from tenant
                </button>
                <select
                  className="w-full rounded-xl border border-white/10 bg-slate-950/40 px-2 py-1 text-xs text-white"
                  value={switchTenantByUser[user.user_id] ?? ""}
                  onChange={(event) =>
                    setSwitchTenantByUser((current) => ({
                      ...current,
                      [user.user_id]: event.target.value,
                    }))
                  }
                >
                  <option value="">Target tenant</option>
                  {tenants
                    .filter((tenant) => tenant.tenant_id !== tenantId && tenant.status === "active")
                    .map((tenant) => (
                      <option key={tenant.tenant_id} value={tenant.tenant_id}>
                        {tenant.tenant_id}
                      </option>
                    ))}
                </select>
                <button
                  className="rounded-full border border-sky-500/40 bg-sky-500/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-sky-100"
                  type="button"
                  onClick={() => void switchTenant(user)}
                  disabled={!switchTenantByUser[user.user_id]}
                >
                  Switch tenant
                </button>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}
