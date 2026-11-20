import { test, expect } from "@playwright/test";

const baseUrl = process.env.PLAYWRIGHT_BASE_URL;
const authority = process.env.PLAYWRIGHT_OIDC_AUTHORITY;
const clientId = process.env.PLAYWRIGHT_OIDC_CLIENT_ID;
const token = process.env.PLAYWRIGHT_AUTH_TOKEN;

test.use({ acceptDownloads: true });

test.beforeEach(async ({ page }) => {
  test.skip(!baseUrl, "PLAYWRIGHT_BASE_URL not set");
  if (authority && clientId && token) {
    await page.addInitScript(
      ({ authorityValue, clientIdValue, tokenValue }) => {
        const baseKey = `oidc.user:${authorityValue}:${clientIdValue}`;
        const altAuthority = authorityValue.endsWith("/")
          ? authorityValue.slice(0, -1)
          : `${authorityValue}/`;
        const altKey = `oidc.user:${altAuthority}:${clientIdValue}`;
        const expiresAt = Math.floor(Date.now() / 1000) + 3600;
        const payload = {
          access_token: tokenValue,
          token_type: "Bearer",
          scope: "openid profile email",
          profile: {
            sub: "playwright",
            email: "playwright@example.com",
            preferred_username: "playwright",
            roles: ["operator", "admin"],
          },
          expires_at: expiresAt,
        };
        window.sessionStorage.setItem(baseKey, JSON.stringify(payload));
        window.sessionStorage.setItem(altKey, JSON.stringify(payload));
      },
      {
        authorityValue: authority,
        clientIdValue: clientId,
        tokenValue: token,
      },
    );
  }
});

test("break-glass request issues and revokes token", async ({ page }) => {
  let issuePayload: Record<string, unknown> | undefined;
  let revokePayload: Record<string, unknown> | undefined;

  await page.route("**/api/break-glass/issue", async (route) => {
    issuePayload = route.request().postDataJSON();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        token_id: "bg-123",
        expires_utc: "2099-01-01T00:00:00Z",
      }),
    });
  });

  await page.route("**/api/break-glass/revoke", async (route) => {
    revokePayload = route.request().postDataJSON();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        token_id: "bg-123",
        revoked: true,
      }),
    });
  });

  await page.route("**/api/ingest/telemetry", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ok: true }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  const signButton = page.getByRole("button", { name: /sign in|sign out/i });
  await expect(signButton).toBeVisible({ timeout: 20000 });

  const requestButton = page.getByTestId("break-glass-request-open");
  await requestButton.scrollIntoViewIfNeeded();
  await page.evaluate(() => {
    const button = document.querySelector(
      '[data-testid="break-glass-request-open"]',
    ) as HTMLElement | null;
    button?.click();
  });
  const modal = page.getByTestId("break-glass-request-modal");
  await expect(modal).toBeVisible({ timeout: 15000 });

  await modal.getByPlaceholder("tenant_id").fill("tenant-1");
  await modal.getByPlaceholder("user_id").fill("user-1");
  await modal.getByPlaceholder("role").fill("admin");
  await modal.getByPlaceholder("scope").fill("admin");
  await modal.getByPlaceholder("duration_minutes").fill("30");
  await modal.getByPlaceholder("reason (required)").fill("incident");

  await modal.getByTestId("break-glass-issue").click();
  await expect(page.getByText("Break-glass token issued.")).toBeVisible();

  await modal.getByRole("button", { name: "Close" }).click();
  const revokeButton = page.getByTestId("break-glass-revoke");
  await expect(revokeButton).toBeEnabled();
  await revokeButton.click();
  await expect(page.getByText("Break-glass token revoked.")).toBeVisible();

  expect(issuePayload).toMatchObject({
    tenant_id: "tenant-1",
    user_id: "user-1",
    role: "admin",
    scope: "admin",
    duration_minutes: 30,
    reason: "incident",
  });
  expect(revokePayload).toMatchObject({ token_id: "bg-123" });
});

test("break-glass audit export downloads JSON", async ({ page }) => {
  let auditTenant: string | null = null;
  await page.route("**/api/break-glass/audit**", async (route) => {
    const url = new URL(route.request().url());
    auditTenant = url.searchParams.get("tenant_id");
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        page: 1,
        page_size: 20,
        total: 1,
        items: [
          {
            token_id: "bg-123",
            tenant_id: "tenant-1",
            user_id: "user-1",
            role: "admin",
            scope: "admin",
            created_utc: "2026-02-01T00:00:00Z",
            expires_utc: "2099-01-01T00:00:00Z",
            revoked_utc: null,
            reason: "incident",
          },
        ],
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  const signButton = page.getByRole("button", { name: /sign in|sign out/i });
  await expect(signButton).toBeVisible({ timeout: 20000 });
  const auditSection = page.getByTestId("break-glass-audit");

  await auditSection.getByTestId("break-glass-audit-tenant").fill("tenant-1");
  const loadButton = auditSection.getByTestId("break-glass-audit-load");
  await expect(loadButton).toBeEnabled({ timeout: 15000 });
  await loadButton.click();

  await expect(page.getByText("Total events: 1")).toBeVisible();
  expect(auditTenant).toBe("tenant-1");

  const [auditDownload] = await Promise.all([
    page.waitForEvent("download"),
    auditSection.getByTestId("break-glass-audit-export").click(),
  ]);
  expect(auditDownload.suggestedFilename()).toBe("break-glass-audit.json");

  const [itemDownload] = await Promise.all([
    page.waitForEvent("download"),
    auditSection.getByTestId("break-glass-audit-export-bg-123").click(),
  ]);
  expect(itemDownload.suggestedFilename()).toBe("break-glass-bg-123.json");
});
