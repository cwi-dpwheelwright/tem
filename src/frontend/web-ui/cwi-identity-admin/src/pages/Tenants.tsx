import { useEffect, useMemo, useState } from 'react';
import { apiGet, apiPatch, apiPost } from '../api/client';

type Tenant = {
  tenant_id: string;
  name: string;
  status: string;
};

export default function Tenants() {
  const [tenants, setTenants] = useState<Tenant[]>([]);
  const [tenantId, setTenantId] = useState('');
  const [name, setName] = useState('');
  const [message, setMessage] = useState('');

  const stats = useMemo(() => {
    const activeCount = tenants.filter((tenant) => tenant.status === 'active').length;
    const disabledCount = tenants.filter((tenant) => tenant.status === 'disabled').length;

    return {
      total: tenants.length,
      active: activeCount,
      disabled: disabledCount,
    };
  }, [tenants]);

  const load = async () => {
    const response = await apiGet<Tenant[]>('/admin/tenants');
    if (response.ok && response.data) {
      setTenants(response.data);
      setMessage('');
    } else {
      setMessage(response.error || 'Failed to load tenants.');
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const createTenant = async (event: React.FormEvent) => {
    event.preventDefault();
    const response = await apiPost<Tenant>('/admin/tenants', {
      tenant_id: tenantId.trim(),
      name: name.trim()
    });
    if (response.ok) {
      setTenantId('');
      setName('');
      await load();
    } else {
      setMessage(response.error || 'Failed to create tenant.');
    }
  };

  const disableTenant = async (tenant: Tenant) => {
    const response = await apiPost<Tenant>(`/admin/tenants/${tenant.tenant_id}/disable`);
    if (response.ok) {
      await load();
    } else {
      setMessage(response.error || 'Failed to disable tenant.');
    }
  };

  const updateTenant = async (tenant: Tenant, updates: Partial<Tenant>) => {
    const response = await apiPatch<Tenant>(`/admin/tenants/${tenant.tenant_id}`, updates);
    if (response.ok) {
      await load();
    } else {
      setMessage(response.error || 'Failed to update tenant.');
    }
  };

  return (
    <>
      <section className="stats-grid" aria-label="Tenant summary">
        <article className="stat-card">
          <span className="stat-card__label">Total tenants</span>
          <strong className="stat-card__value">{stats.total}</strong>
          <span className="stat-card__caption">All tenant records currently loaded</span>
        </article>
        <article className="stat-card">
          <span className="stat-card__label">Active</span>
          <strong className="stat-card__value">{stats.active}</strong>
          <span className="stat-card__caption">Tenants able to authenticate and operate</span>
        </article>
        <article className="stat-card">
          <span className="stat-card__label">Disabled</span>
          <strong className="stat-card__value">{stats.disabled}</strong>
          <span className="stat-card__caption">Tenants intentionally removed from service</span>
        </article>
      </section>

      <section className="panel">
        <header className="panel__header">
          <div>
            <span className="panel__eyebrow">Provisioning</span>
            <h2>Create tenant</h2>
            <p>Stand up a new tenant record with clean identifiers before downstream onboarding begins.</p>
          </div>
        </header>

        <form className="grid grid--two" onSubmit={createTenant}>
          <label className="field">
            <span>Tenant ID</span>
            <input value={tenantId} onChange={(e) => setTenantId(e.target.value)} required />
          </label>
          <label className="field">
            <span>Tenant name</span>
            <input value={name} onChange={(e) => setName(e.target.value)} required />
          </label>
          <div className="grid__actions">
            <button className="btn btn--primary" type="submit">
              Create tenant
            </button>
          </div>
        </form>

        {message && <div className="notice">{message}</div>}
      </section>

      <section className="panel">
        <header className="panel__header">
          <div>
            <span className="panel__eyebrow">Directory</span>
            <h2>Tenant roster</h2>
            <p>Review lifecycle state, make inline naming updates, and disable tenants when needed.</p>
          </div>
          <div className="panel__actions">
            <button className="btn btn--ghost" type="button" onClick={() => void load()}>
              Refresh roster
            </button>
          </div>
        </header>

        <div className="table">
          <div className="table__row table__row--head table__row--tenants">
            <div>Tenant ID</div>
            <div>Name</div>
            <div>Status</div>
            <div>Actions</div>
          </div>
          {tenants.length === 0 ? (
            <div className="table__empty">No tenants found yet. Create the first tenant above to begin.</div>
          ) : (
            tenants.map((tenant) => (
              <div className="table__row table__row--tenants" key={tenant.tenant_id}>
                <div data-label="Tenant ID" className="mono-value">{tenant.tenant_id}</div>
                <div data-label="Name">
                  <input
                    className="inline-input"
                    defaultValue={tenant.name}
                    onBlur={(event) => {
                      const next = event.target.value.trim();
                      if (next && next !== tenant.name) {
                        void updateTenant(tenant, { name: next });
                      }
                    }}
                  />
                </div>
                <div data-label="Status" className="stack">
                  <span className={`status-badge ${tenant.status === 'disabled' ? 'status-badge--disabled' : 'status-badge--active'}`}>
                    {tenant.status}
                  </span>
                  <select
                    className="inline-input inline-input--status"
                    defaultValue={tenant.status}
                    onChange={(event) => void updateTenant(tenant, { status: event.target.value })}
                  >
                    <option value="active">active</option>
                    <option value="disabled">disabled</option>
                  </select>
                </div>
                <div data-label="Actions" className="table__actions">
                  <button
                    className="btn btn--danger"
                    type="button"
                    onClick={() => void disableTenant(tenant)}
                    disabled={tenant.status === 'disabled'}
                  >
                    Disable
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      </section>
    </>
  );
}
