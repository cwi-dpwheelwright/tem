import { useState } from "react";
import { DeprecationBanner } from "../components/DeprecationBanner";
import { useLocation, useNavigate } from "react-router-dom";
import { apiPost, getBaseUrl, setToken } from "../api/client";

export default function Login() {
  const baseUrl = getBaseUrl();
  const [tenantId, setTenantId] = useState("");
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const navigate = useNavigate();
  const location = useLocation();

  const onSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!baseUrl.trim()) {
      setError("Identity base URL is not configured.");
      return;
    }
    if (!identifier.trim() || !password.trim()) {
      setError("Provide username or email, and password.");
      return;
    }
    const trimmedIdentifier = identifier.trim();
    const isEmail = trimmedIdentifier.includes("@");
    const response = await apiPost<{ access_token: string }>("/auth/login", {
      tenant_id: tenantId.trim() ? tenantId.trim() : undefined,
      email: isEmail ? trimmedIdentifier : undefined,
      username: isEmail ? undefined : trimmedIdentifier,
      password: password
    });
    if (!response.ok || !response.data?.access_token) {
      setError(response.error || "Login failed.");
      return;
    }
    setToken(response.data.access_token);
    const next = (location.state as { from?: string } | null)?.from || "/tenants";
    navigate(next, { replace: true });
  };

  return (
    <div className="login">
      <div className="login__card">
        <div className="login__hero">
          <div className="login__brand">
            <span className="login__brand-mark">CWI</span>
            <div>
              <div className="login__brand-title">Identity Admin</div>
              <div className="login__brand-subtitle">Same visual system as the GPT workspace</div>
            </div>
          </div>
          <div className="login__header">
            <span className="pill">Operator Console</span>
            <h1>Identity Command Center</h1>
            <p className="login__lede">
              Connect to the identity server, manage tenants and users, and keep access control work in the same visual family as the main CWI product surfaces.
            </p>
          </div>
          <div className="login__stats">
            <div className="login__stat">
              <span className="login__stat-label">Theme</span>
              <strong className="login__stat-value">GPT-aligned</strong>
            </div>
            <div className="login__stat">
              <span className="login__stat-label">Focus</span>
              <strong className="login__stat-value">Tenant + user ops</strong>
            </div>
            <div className="login__stat">
              <span className="login__stat-label">Endpoint</span>
              <strong className="login__stat-value login__stat-value--break">{baseUrl || "Not configured"}</strong>
            </div>
          </div>
        </div>
        <div className="login__surface">
          <DeprecationBanner compact />
          <div className="login__surface-copy">
            <span className="login__eyebrow">Access</span>
            <h2>Sign in to continue</h2>
            <p>Use an admin username or email to open the legacy identity tools.</p>
          </div>
          <form onSubmit={onSubmit} className="form">
            <label className="field">
              <span>Tenant ID (optional for super admin)</span>
              <input
                value={tenantId}
                onChange={(event) => setTenantId(event.target.value)}
                placeholder="tenant-1"
              />
            </label>
            <label className="field">
              <span>Admin username or email</span>
              <input
                value={identifier}
                onChange={(event) => setIdentifier(event.target.value)}
                placeholder="admin@tenant.local or admin"
              />
            </label>
            <label className="field">
              <span>Password</span>
              <input
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="enter password"
                type="password"
              />
            </label>
            {error && <div className="error">{error}</div>}
            <button type="submit" className="btn btn--primary">
              Enter Admin Console
            </button>
          </form>
          <div className="login__footer">
            Login uses the identity API to obtain a bearer token. Tokens are stored locally and never sent to third parties.
          </div>
        </div>
      </div>
    </div>
  );
}
