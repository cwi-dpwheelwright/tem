import { cleanup, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

function buildToken(payload: Record<string, unknown>): string {
  const encodedPayload = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `header.${encodedPayload}.signature`;
}

async function renderSessionBadge(payload: Record<string, unknown>, adminTenantId: string) {
  vi.resetModules();
  vi.doMock("next/navigation", () => ({
    usePathname: () => "/",
    useRouter: () => ({
      replace: vi.fn(),
    }),
  }));
  vi.doMock("next/link", () => ({
    default: ({ href, children, ...props }: { href: string; children: ReactNode }) => (
      <a href={href} {...props}>{children}</a>
    ),
  }));

  process.env.NEXT_PUBLIC_CWI_AUTH_MODE = "token";
  process.env.NEXT_PUBLIC_CWI_DEFAULT_TENANT = "default-tenant";
  process.env.NEXT_PUBLIC_CWI_ADMIN_TENANT = adminTenantId;
  process.env.NEXT_PUBLIC_CWI_GATEWAY_BASE_URL = "http://localhost:3000";

  window.localStorage.clear();
  window.sessionStorage.clear();
  window.sessionStorage.setItem("cwi.user.web.token.access", buildToken(payload));

  const { SessionBadge } = await import("@/components/session-badge");
  render(<SessionBadge />);
}

describe("SessionBadge", () => {
  afterEach(() => {
    cleanup();
    window.localStorage.clear();
    window.sessionStorage.clear();
    delete process.env.NEXT_PUBLIC_CWI_AUTH_MODE;
    delete process.env.NEXT_PUBLIC_CWI_DEFAULT_TENANT;
    delete process.env.NEXT_PUBLIC_CWI_ADMIN_TENANT;
    delete process.env.NEXT_PUBLIC_CWI_GATEWAY_BASE_URL;
    vi.resetModules();
    vi.clearAllMocks();
  });

  it("shows administration for platform administrators outside the admin tenant", async () => {
    await renderSessionBadge(
      {
        tenant_id: "customer-tenant",
        sub: "user-1",
        name: "Admin User",
        cwi_admin: true,
      },
      "admin-tenant",
    );

    await waitFor(() => expect(screen.getByText("Admin User")).toBeTruthy());
    expect(screen.getByRole("link", { name: /go to administration/i })).toBeTruthy();
  });

  it("shows administration for configured superadmin claims outside the admin tenant", async () => {
    await renderSessionBadge(
      {
        tenant_id: "customer-tenant",
        sub: "user-2",
        name: "Customer User",
        is_superadmin: true,
      },
      "admin-tenant",
    );

    await waitFor(() => expect(screen.getByText("Customer User")).toBeTruthy());
    expect(screen.getByRole("link", { name: /go to administration/i })).toBeTruthy();
  });

  it("hides administration for normal users in the old admin tenant without entitlement", async () => {
    await renderSessionBadge(
      {
        tenant_id: "admin-tenant",
        sub: "user-3",
        name: "Plain User",
      },
      "admin-tenant",
    );

    await waitFor(() => expect(screen.getByText("Plain User")).toBeTruthy());
    expect(screen.queryByRole("link", { name: /go to administration/i })).toBeNull();
  });
});
