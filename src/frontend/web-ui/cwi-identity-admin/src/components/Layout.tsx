import { NavLink, useLocation, useNavigate } from "react-router-dom";
import { DeprecationBanner } from "./DeprecationBanner";
import { clearToken, getBaseUrl, getToken } from "../api/client";

export function Layout({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const location = useLocation();

  const pageMeta: Record<string, { eyebrow: string; title: string; description: string }> = {
    "/tenants": {
      eyebrow: "Identity Admin",
      title: "Tenant operations",
      description: "Provision tenants, keep lifecycle state clean, and make org-level access changes from one governed workspace.",
    },
    "/users": {
      eyebrow: "Identity Admin",
      title: "User operations",
      description: "Invite admins, activate accounts, rotate credentials, and keep role assignments in step with live tenant policy.",
    },
  };

  const currentPage = pageMeta[location.pathname] ?? pageMeta["/tenants"];
  const hasToken = Boolean(getToken());
  const baseUrl = getBaseUrl().trim();

  const handleLogout = () => {
    clearToken();
    navigate("/login");
  };

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar__panel">
          <div className="brand">
            <span className="brand__mark">CWI</span>
            <div>
              <div className="brand__title">Identity Admin</div>
              <div className="brand__subtitle">Governed tenant and user controls</div>
            </div>
          </div>

          <div className="sidebar__intro">
            <span className="sidebar__eyebrow">Shared theme</span>
            <p className="sidebar__summary">
              The legacy identity workspace now carries the same navy-first visual language as the GPT experience while preserving the fast admin flows.
            </p>
          </div>

          <nav className="nav" aria-label="Identity admin navigation">
            <NavLink
              to="/tenants"
              className={({ isActive }) => (isActive ? "nav__link nav__link--active" : "nav__link")}
            >
              <span className="nav__title">Tenants</span>
              <span className="nav__subtitle">Provision and lifecycle control</span>
            </NavLink>
            <NavLink
              to="/users"
              className={({ isActive }) => (isActive ? "nav__link nav__link--active" : "nav__link")}
            >
              <span className="nav__title">Users</span>
              <span className="nav__subtitle">Invites, passwords, and roles</span>
            </NavLink>
          </nav>

          <dl className="sidebar__meta">
            <div className="sidebar__meta-row">
              <dt>Session</dt>
              <dd>{hasToken ? "Authenticated" : "Signed out"}</dd>
            </div>
            <div className="sidebar__meta-row">
              <dt>Identity API</dt>
              <dd className="sidebar__meta-row--break">{baseUrl || "Not configured"}</dd>
            </div>
          </dl>

          <div className="sidebar__footer">
            {hasToken && (
              <button type="button" className="btn btn--ghost" onClick={handleLogout}>
                Sign out
              </button>
            )}
          </div>
        </div>
      </aside>
      <main className="content">
        <DeprecationBanner />
        <div className="content__inner">
          <header className="page-header">
            <div className="page-header__copy">
              <span className="page-header__eyebrow">{currentPage.eyebrow}</span>
              <h1>{currentPage.title}</h1>
              <p>{currentPage.description}</p>
            </div>
            <div className="page-header__panel">
              <div className="page-header__chip">{hasToken ? "Authenticated session" : "Awaiting login"}</div>
              <div className="page-header__meta">
                <span className="page-header__meta-label">Connected endpoint</span>
                <span className="page-header__meta-value">{baseUrl || "Identity base URL not configured"}</span>
              </div>
            </div>
          </header>
          {children}
        </div>
      </main>
    </div>
  );
}
