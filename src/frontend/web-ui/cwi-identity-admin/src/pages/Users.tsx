import { useEffect, useMemo, useState } from 'react';
import { apiGet, apiPost, apiPut, orchestratorApiGet } from '../api/client';

type UserSummary = {
  user_id: string;
  email: string;
  username?: string | null;
  display_name: string;
  status: string;
  account_state: string;
  is_locked: boolean;
  lockout_until_utc?: string | null;
  failed_login_count: number;
  can_unlock: boolean;
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
  assignment_status?: string | null;
  assigned_utc?: string | null;
  last_heartbeat_utc?: string | null;
  policy_scope_id?: string | null;
  local_only: boolean;
  remote_fallback_allowed: boolean;
};

function confirmAction(message: string): boolean {
  return window.confirm(message);
}

function accountState(user: UserSummary): string {
  return user.account_state || user.status || 'unknown';
}

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString();
}

export default function Users() {
  const [tenantId, setTenantId] = useState('');
  const [tenantPickerValue, setTenantPickerValue] = useState('');
  const [manualTenantId, setManualTenantId] = useState('');
  const [tenantModalOpen, setTenantModalOpen] = useState(false);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [tenants, setTenants] = useState<TenantSummary[]>([]);
  const [inviteEmail, setInviteEmail] = useState('');
  const [inviteUsername, setInviteUsername] = useState('');
  const [inviteName, setInviteName] = useState('');
  const [roleName, setRoleName] = useState('');
  const [passwordUserId, setPasswordUserId] = useState('');
  const [passwordValue, setPasswordValue] = useState('');
  const [switchTenantByUser, setSwitchTenantByUser] = useState<Record<string, string>>({});
  const [message, setMessage] = useState('');
  const [tenantLoadMessage, setTenantLoadMessage] = useState('');
  const [inviteToken, setInviteToken] = useState('');
  const [homeNodeLinksByUser, setHomeNodeLinksByUser] = useState<Record<string, HomeNodeUserLink>>({});
  const [homeNodeStatusMessage, setHomeNodeStatusMessage] = useState('');

  const tenantOptions = useMemo(
    () => [...tenants].sort((left, right) => left.tenant_id.localeCompare(right.tenant_id)),
    [tenants],
  );

  const selectedTenant = useMemo(
    () => tenantOptions.find((tenant) => tenant.tenant_id === tenantId),
    [tenantId, tenantOptions],
  );

  const stats = useMemo(() => {
    const activeStates = ['active', 'approved', 'enabled'];
    const pendingStates = ['invited', 'pending', 'new'];
    const active = users.filter((user) => activeStates.includes(accountState(user).toLowerCase()) && !user.is_locked).length;
    const pending = users.filter((user) => pendingStates.includes(accountState(user).toLowerCase())).length;
    const locked = users.filter((user) => user.is_locked).length;
    const restricted = Math.max(users.length - active - pending - locked, 0);
    const uniqueRoles = new Set(users.flatMap((user) => user.roles)).size;
    const linkedHomeNodes = users.filter((user) => homeNodeLinksByUser[user.user_id]?.linked).length;

    return {
      total: users.length,
      active,
      pending,
      locked,
      restricted,
      uniqueRoles,
      linkedHomeNodes,
    };
  }, [homeNodeLinksByUser, users]);

  const statusClassName = (status: string) => {
    const normalized = status.toLowerCase();
    if (normalized === 'locked') {
      return 'status-badge status-badge--locked';
    }
    if (['active', 'approved', 'enabled'].includes(normalized)) {
      return 'status-badge status-badge--active';
    }
    if (['invited', 'pending', 'new'].includes(normalized)) {
      return 'status-badge status-badge--pending';
    }
    return 'status-badge status-badge--disabled';
  };

  const renderHomeNodeStatus = (user: UserSummary) => {
    const link = homeNodeLinksByUser[user.user_id];
    if (!link?.linked) {
      return (
        <div className="stack">
          <span className="status-badge status-badge--disabled">Unlinked</span>
          <small>No active home node</small>
        </div>
      );
    }

    return (
      <div className="stack">
        <span className="status-badge status-badge--active">Linked</span>
        {link.node_id && <small className="mono-value">{link.node_id}</small>}
        {link.tailscale_ip && <small>{link.tailscale_ip}</small>}
        {link.node_status && <small>Node {link.node_status}</small>}
      </div>
    );
  };

  const loadUsers = async (targetTenant: string) => {
    if (!targetTenant) {
      return;
    }
    const response = await apiGet<UserSummary[]>(`/admin/users?tenant_id=${encodeURIComponent(targetTenant)}`);
    if (response.ok && response.data) {
      setUsers(response.data);
      setSwitchTenantByUser({});
      setMessage('');
    } else {
      setMessage(response.error || 'Failed to load users.');
    }
  };

  const loadHomeNodeLinks = async (targetTenant: string) => {
    if (!targetTenant) {
      return;
    }

    const response = await orchestratorApiGet<HomeNodeUserLink[]>(
      `/api/admin/home-node-user-links?tenant_id=${encodeURIComponent(targetTenant)}`,
    );

    if (response.ok && response.data) {
      const nextLinks = response.data.reduce<Record<string, HomeNodeUserLink>>((current, link) => {
        if (link.linked) {
          current[link.user_id] = link;
        }
        return current;
      }, {});
      setHomeNodeLinksByUser(nextLinks);
      setHomeNodeStatusMessage('');
    } else {
      setHomeNodeLinksByUser({});
      setHomeNodeStatusMessage(response.error || 'Failed to load home-node link status.');
    }
  };

  const loadRoles = async (targetTenant: string) => {
    if (!targetTenant) {
      return;
    }
    const response = await apiGet<Role[]>(`/admin/roles?tenant_id=${encodeURIComponent(targetTenant)}`);
    if (response.ok && response.data) {
      setRoles(response.data);
    } else {
      setMessage(response.error || 'Failed to load roles.');
    }
  };

  const loadTenants = async () => {
    const response = await apiGet<TenantSummary[]>('/admin/tenants');
    if (response.ok && response.data) {
      setTenants(response.data);
      setTenantLoadMessage('');
    } else {
      setTenantLoadMessage(response.error || 'Failed to load tenants.');
    }
  };

  const openTenantManagement = (targetTenant: string) => {
    const normalizedTenantId = targetTenant.trim();
    if (!normalizedTenantId) {
      return;
    }

    setTenantId(normalizedTenantId);
    setTenantPickerValue(normalizedTenantId);
    setManualTenantId(normalizedTenantId);
    setPasswordUserId('');
    setPasswordValue('');
    setRoleName('');
    setInviteToken('');
    setMessage('');
    setTenantModalOpen(true);
  };

  const refreshTenantManagement = async () => {
    await Promise.all([loadUsers(tenantId), loadRoles(tenantId), loadHomeNodeLinks(tenantId), loadTenants()]);
  };

  useEffect(() => {
    if (tenantModalOpen && tenantId) {
      void loadUsers(tenantId);
      void loadRoles(tenantId);
      void loadHomeNodeLinks(tenantId);
    }
  }, [tenantId, tenantModalOpen]);

  useEffect(() => {
    void loadTenants();
  }, []);

  useEffect(() => {
    if (!tenantModalOpen) {
      return;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setTenantModalOpen(false);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [tenantModalOpen]);

  const invite = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!tenantId) {
      setMessage('Select a tenant before inviting users.');
      return;
    }
    const response = await apiPost<{ invite_token: string }>('/admin/users/invite', {
      tenant_id: tenantId,
      email: inviteEmail,
      username: inviteUsername || undefined,
      display_name: inviteName
    });
    if (response.ok && response.data) {
      setInviteToken(response.data.invite_token);
      setInviteEmail('');
      setInviteUsername('');
      setInviteName('');
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
    } else {
      setMessage(response.error || 'Failed to invite user.');
    }
  };

  const revoke = async (user: UserSummary) => {
    if (!confirmAction(`Revoke access for ${user.email}?`)) {
      setMessage('Revoke action cancelled.');
      return;
    }
    const response = await apiPost(`/admin/users/${user.user_id}/revoke`, {
      tenant_id: tenantId,
      reason: 'admin revoke'
    });
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
    } else {
      setMessage(response.error || 'Failed to revoke user.');
    }
  };

  const removeFromTenant = async (user: UserSummary) => {
    if (!confirmAction(`Remove ${user.email} from tenant ${tenantId}? This revokes sessions and clears roles.`)) {
      setMessage('Remove action cancelled.');
      return;
    }
    const response = await apiPost(`/admin/users/${user.user_id}/remove`, {
      tenant_id: tenantId,
      reason: 'admin remove from tenant'
    });
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage(`Removed ${user.email} from ${tenantId}.`);
    } else {
      setMessage(response.error || 'Failed to remove user from tenant.');
    }
  };

  const switchTenant = async (user: UserSummary) => {
    const targetTenantId = switchTenantByUser[user.user_id];
    if (!targetTenantId) {
      setMessage('Choose a target tenant before switching the user.');
      return;
    }
    if (!confirmAction(`Switch ${user.email} from ${tenantId} to ${targetTenantId}?`)) {
      setMessage('Tenant switch cancelled.');
      return;
    }
    const response = await apiPost(`/admin/users/${user.user_id}/tenant`, {
      tenant_id: tenantId,
      target_tenant_id: targetTenantId
    });
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage(`Moved ${user.email} to ${targetTenantId}.`);
    } else {
      setMessage(response.error || 'Failed to switch user tenant.');
    }
  };

  const assignRoles = async (user: UserSummary, nextRoles: string[]) => {
    const response = await apiPut(`/admin/users/${user.user_id}/roles`, {
      tenant_id: tenantId,
      roles: nextRoles
    });
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
    } else {
      setMessage(response.error || 'Failed to assign roles.');
    }
  };

  const createRole = async (event: React.FormEvent) => {
    event.preventDefault();
    const nextRoleName = roleName.trim();
    if (!tenantId) {
      setMessage('Select a tenant before creating roles.');
      return;
    }
    if (!nextRoleName) {
      setMessage('Enter a role name before creating a role.');
      return;
    }

    const response = await apiPost<Role>('/admin/roles', {
      tenant_id: tenantId,
      name: nextRoleName
    });
    if (response.ok) {
      setRoleName('');
      await loadRoles(tenantId);
      setMessage(`Created role ${nextRoleName}.`);
    } else {
      setMessage(response.error || 'Failed to create role.');
    }
  };

  const approve = async (user: UserSummary) => {
    const response = await apiPost(`/admin/users/${user.user_id}/approve`, {
      tenant_id: tenantId
    });
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
    } else {
      setMessage(response.error || 'Failed to approve user.');
    }
  };

  const unlock = async (user: UserSummary) => {
    if (!user.can_unlock && !user.is_locked) {
      setMessage(`${user.email} does not have a lockout to clear.`);
      return;
    }
    if (!confirmAction(`Unlock ${user.email} and reset failed sign-in attempts?`)) {
      setMessage('Unlock action cancelled.');
      return;
    }
    const response = await apiPost(`/admin/users/${user.user_id}/unlock`, {
      tenant_id: tenantId,
      reason: 'admin unlock'
    });
    if (response.ok) {
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage(`Unlocked ${user.email}.`);
    } else {
      setMessage(response.error || 'Failed to unlock user.');
    }
  };

  const setPassword = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!passwordUserId || !passwordValue) {
      setMessage('Select a user and enter a password.');
      return;
    }
    const response = await apiPost(`/admin/users/${passwordUserId}/password`, {
      tenant_id: tenantId,
      password: passwordValue
    });
    if (response.ok) {
      setPasswordUserId('');
      setPasswordValue('');
      await loadUsers(tenantId);
      await loadHomeNodeLinks(tenantId);
      setMessage('Password set and user activated.');
    } else {
      setMessage(response.error || 'Failed to set password.');
    }
  };

  return (
    <>
      <section className="panel">
        <header className="panel__header">
          <div>
            <span className="panel__eyebrow">Workspace</span>
            <h2>Select a tenant</h2>
            <p>Choose a tenant first. User management, role changes, invites, removals, and tenant moves open in a focused management modal.</p>
          </div>
          <div className="panel__actions">
            <button className="btn btn--ghost" type="button" onClick={() => void loadTenants()}>
              Refresh tenants
            </button>
          </div>
        </header>

        <div className="tenant-selector">
          <label className="field">
            <span>Tenant</span>
            <select
              value={tenantPickerValue}
              onChange={(event) => {
                const nextTenantId = event.target.value;
                setTenantPickerValue(nextTenantId);
                if (nextTenantId) {
                  openTenantManagement(nextTenantId);
                }
              }}
            >
              <option value="">Select a tenant</option>
              {tenantOptions.map((tenant) => (
                <option key={tenant.tenant_id} value={tenant.tenant_id}>
                  {tenant.name ? `${tenant.name} (${tenant.tenant_id})` : tenant.tenant_id}
                </option>
              ))}
            </select>
          </label>

          <form
            className="tenant-manual"
            onSubmit={(event) => {
              event.preventDefault();
              openTenantManagement(manualTenantId);
            }}
          >
            <label className="field">
              <span>Tenant ID</span>
              <input
                value={manualTenantId}
                onChange={(event) => setManualTenantId(event.target.value)}
                placeholder="tenant-1"
              />
            </label>
            <button className="btn btn--primary" type="submit" disabled={!manualTenantId.trim()}>
              Open tenant
            </button>
          </form>
        </div>

        {tenantLoadMessage && <div className="notice">{tenantLoadMessage}</div>}

        {tenantOptions.length === 0 ? (
          <div className="table__empty">No tenants loaded yet. Refresh tenants or enter a tenant ID manually.</div>
        ) : (
          <div className="tenant-grid" aria-label="Available tenants">
            {tenantOptions.map((tenant) => (
              <button
                className={`tenant-option${tenant.tenant_id === tenantId ? ' tenant-option--selected' : ''}`}
                key={tenant.tenant_id}
                type="button"
                onClick={() => openTenantManagement(tenant.tenant_id)}
              >
                <span className="tenant-option__main">
                  <strong>{tenant.name || tenant.tenant_id}</strong>
                  <span className="mono-value">{tenant.tenant_id}</span>
                </span>
                <span className={statusClassName(tenant.status)}>{tenant.status}</span>
                <span className="tenant-option__action">Manage tenant</span>
              </button>
            ))}
          </div>
        )}
      </section>

      {tenantModalOpen && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) {
              setTenantModalOpen(false);
            }
          }}
        >
          <section
            className="modal modal--wide"
            role="dialog"
            aria-modal="true"
            aria-labelledby="tenant-management-title"
          >
            <header className="modal__header">
              <div>
                <span className="panel__eyebrow">Tenant operations</span>
                <h2 id="tenant-management-title">Manage {selectedTenant?.name || tenantId}</h2>
                <p>
                  <span className="mono-value">{tenantId}</span>
                  {selectedTenant?.status && <span className={statusClassName(selectedTenant.status)}>{selectedTenant.status}</span>}
                </p>
              </div>
              <div className="modal__actions">
                <button className="btn btn--ghost" type="button" onClick={() => void refreshTenantManagement()}>
                  Refresh
                </button>
                <button className="btn btn--primary" type="button" onClick={() => setTenantModalOpen(false)}>
                  Close
                </button>
              </div>
            </header>

            <div className="modal__body">
              <section className="modal-section">
                <header className="modal-section__header">
                  <div>
                    <span className="panel__eyebrow">Overview</span>
                    <h3>Tenant health</h3>
                  </div>
                </header>
                <div className="stats-grid" aria-label="User summary">
                  <article className="stat-card">
                    <span className="stat-card__label">Users loaded</span>
                    <strong className="stat-card__value">{stats.total}</strong>
                    <span className="stat-card__caption">Accounts currently visible for this tenant</span>
                  </article>
                  <article className="stat-card">
                    <span className="stat-card__label">Active</span>
                    <strong className="stat-card__value">{stats.active}</strong>
                    <span className="stat-card__caption">Approved or enabled users</span>
                  </article>
                  <article className="stat-card">
                    <span className="stat-card__label">Pending</span>
                    <strong className="stat-card__value">{stats.pending}</strong>
                    <span className="stat-card__caption">Invites or approvals still in flight</span>
                  </article>
                  <article className="stat-card">
                    <span className="stat-card__label">Locked</span>
                    <strong className="stat-card__value">{stats.locked}</strong>
                    <span className="stat-card__caption">Accounts blocked by failed sign-in attempts</span>
                  </article>
                  <article className="stat-card">
                    <span className="stat-card__label">Restricted</span>
                    <strong className="stat-card__value">{stats.restricted}</strong>
                    <span className="stat-card__caption">Users outside the active and pending states</span>
                  </article>
                  <article className="stat-card">
                    <span className="stat-card__label">Distinct roles</span>
                    <strong className="stat-card__value">{stats.uniqueRoles}</strong>
                    <span className="stat-card__caption">Role names currently assigned in this tenant</span>
                  </article>
                  <article className="stat-card">
                    <span className="stat-card__label">Home nodes linked</span>
                    <strong className="stat-card__value">{stats.linkedHomeNodes}</strong>
                    <span className="stat-card__caption">Users with an active node assignment</span>
                  </article>
                </div>
              </section>

              <section className="modal-section">
                <header className="modal-section__header">
                  <div>
                    <span className="panel__eyebrow">Provisioning</span>
                    <h3>Invites and credentials</h3>
                  </div>
                </header>

                <div className="panel__subgrid">
                  <section className="surface-card">
                    <div className="surface-card__header">
                      <h3>Invite user</h3>
                      <p>Create a new invitation token and prepare the user record in one step.</p>
                    </div>
                    <form className="grid grid--two" onSubmit={invite}>
                      <label className="field">
                        <span>Invite email</span>
                        <input value={inviteEmail} onChange={(e) => setInviteEmail(e.target.value)} required />
                      </label>
                      <label className="field">
                        <span>Username (optional)</span>
                        <input value={inviteUsername} onChange={(e) => setInviteUsername(e.target.value)} />
                      </label>
                      <label className="field">
                        <span>Display name</span>
                        <input value={inviteName} onChange={(e) => setInviteName(e.target.value)} />
                      </label>
                      <div className="grid__actions">
                        <button className="btn btn--primary" type="submit" disabled={!tenantId}>
                          Send invite
                        </button>
                      </div>
                    </form>
                  </section>

                  <section className="surface-card">
                    <div className="surface-card__header">
                      <h3>Set password</h3>
                      <p>Activate or recover a selected account without leaving tenant operations.</p>
                    </div>
                    <form className="grid grid--two" onSubmit={setPassword}>
                      <label className="field">
                        <span>Set password for user</span>
                        <select
                          value={passwordUserId}
                          onChange={(event) => setPasswordUserId(event.target.value)}
                          required
                        >
                          <option value="">Select user</option>
                          {users.map((user) => (
                            <option key={user.user_id} value={user.user_id}>
                              {user.email} ({user.username || 'no-username'})
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="field">
                        <span>New password</span>
                        <input
                          value={passwordValue}
                          onChange={(event) => setPasswordValue(event.target.value)}
                          type="password"
                          required
                        />
                      </label>
                      <div className="grid__actions">
                        <button className="btn btn--primary" type="submit" disabled={!tenantId}>
                          Set password & activate
                        </button>
                      </div>
                    </form>
                  </section>
                </div>

                {inviteToken && (
                  <div className="callout">
                    <div>
                      <strong>Invite token</strong>
                      <div className="callout__token">{inviteToken}</div>
                    </div>
                    <button
                      className="btn btn--ghost"
                      type="button"
                      onClick={() => navigator.clipboard.writeText(inviteToken)}
                    >
                      Copy token
                    </button>
                  </div>
                )}

                {message && <div className="notice">{message}</div>}
              </section>

              <section className="modal-section">
                <header className="modal-section__header">
                  <div>
                    <span className="panel__eyebrow">Roles</span>
                    <h3>Role management</h3>
                  </div>
                </header>

                <form className="grid grid--two" onSubmit={createRole}>
                  <label className="field">
                    <span>Role name</span>
                    <input
                      value={roleName}
                      onChange={(event) => setRoleName(event.target.value)}
                      placeholder="admin"
                      required
                    />
                  </label>
                  <div className="grid__actions">
                    <button className="btn btn--primary" type="submit" disabled={!tenantId || !roleName.trim()}>
                      Add role
                    </button>
                  </div>
                </form>

                <div className="table">
                  <div className="table__row table__row--head table__row--roles">
                    <div>Role</div>
                    <div>ID</div>
                  </div>
                  {roles.length === 0 ? (
                    <div className="table__empty">No roles found for this tenant yet.</div>
                  ) : (
                    roles.map((role) => (
                      <div className="table__row table__row--roles" key={role.role_id}>
                        <div data-label="Role">{role.name}</div>
                        <div data-label="ID" className="mono-value">{role.role_id}</div>
                      </div>
                    ))
                  )}
                </div>
              </section>

              <section className="modal-section">
                <header className="modal-section__header">
                  <div>
                    <span className="panel__eyebrow">Roster</span>
                    <h3>User directory</h3>
                  </div>
                </header>

                <div className="table">
                  <div className="table__row table__row--head table__row--users">
                    <div>Email</div>
                    <div>Username</div>
                    <div>Status</div>
                    <div>Home node</div>
                    <div>Token version</div>
                    <div>Roles</div>
                    <div>Actions</div>
                  </div>
                  {homeNodeStatusMessage && (
                    <div className="notice notice--inline">{homeNodeStatusMessage}</div>
                  )}
                  {users.length === 0 ? (
                    <div className="table__empty">No users found for this tenant yet.</div>
                  ) : (
                    users.map((user) => (
                      <div className="table__row table__row--users" key={user.user_id}>
                        <div data-label="Email">
                          <div className="stack">
                            <span>{user.email}</span>
                            <small>{user.display_name}</small>
                          </div>
                        </div>
                        <div data-label="Username">{user.username || '-'}</div>
                        <div data-label="Status">
                          <div className="stack">
                            <span className={statusClassName(accountState(user))}>{accountState(user)}</span>
                            {accountState(user) !== user.status && <small>Lifecycle: {user.status}</small>}
                            {user.failed_login_count > 0 && <small>{user.failed_login_count} failed sign-in attempts</small>}
                            {user.lockout_until_utc && (
                              <small>{user.is_locked ? 'Locked until' : 'Last lockout'} {formatDateTime(user.lockout_until_utc)}</small>
                            )}
                          </div>
                        </div>
                        <div data-label="Home node">{renderHomeNodeStatus(user)}</div>
                        <div data-label="Token version" className="mono-value">{user.token_version}</div>
                        <div data-label="Roles">
                          <select
                            className="inline-input inline-input--multiselect"
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
                        </div>
                        <div data-label="Actions" className="table__actions">
                          <div className="stack stack--row">
                            <button className="btn btn--success" type="button" onClick={() => void approve(user)}>
                              Approve
                            </button>
                            <button
                              className="btn btn--ghost"
                              type="button"
                              onClick={() => void unlock(user)}
                              disabled={!user.can_unlock && !user.is_locked}
                            >
                              Unlock
                            </button>
                            <button className="btn btn--danger" type="button" onClick={() => void revoke(user)}>
                              Revoke
                            </button>
                            <button className="btn btn--danger" type="button" onClick={() => void removeFromTenant(user)}>
                              Remove from tenant
                            </button>
                            <select
                              className="inline-input"
                              value={switchTenantByUser[user.user_id] ?? ''}
                              onChange={(event) =>
                                setSwitchTenantByUser((current) => ({
                                  ...current,
                                  [user.user_id]: event.target.value,
                                }))
                              }
                            >
                              <option value="">Target tenant</option>
                              {tenantOptions
                                .filter((tenant) => tenant.tenant_id !== tenantId && tenant.status.toLowerCase() === 'active')
                                .map((tenant) => (
                                  <option key={tenant.tenant_id} value={tenant.tenant_id}>
                                    {tenant.tenant_id}
                                  </option>
                                ))}
                            </select>
                            <button
                              className="btn btn--ghost"
                              type="button"
                              onClick={() => void switchTenant(user)}
                              disabled={!switchTenantByUser[user.user_id]}
                            >
                              Switch tenant
                            </button>
                          </div>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </section>
            </div>
          </section>
        </div>
      )}
    </>
  );
}
