import { test, expect } from "@playwright/test";

const tenantId = process.env.PLAYWRIGHT_APPROVALS_TENANT_ID;
const traceId = process.env.PLAYWRIGHT_TRACE_ID;
const authority = process.env.PLAYWRIGHT_OIDC_AUTHORITY;
const clientId = process.env.PLAYWRIGHT_OIDC_CLIENT_ID;
const token = process.env.PLAYWRIGHT_AUTH_TOKEN;
const baseUrl = process.env.PLAYWRIGHT_BASE_URL;

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

test("login control is visible", async ({ page }) => {
  await page.goto("/");
  const signButton = page.getByRole("button", { name: /sign in|sign out/i });
  await expect(signButton).toBeVisible({ timeout: 20000 });
});

test("approvals list loads", async ({ page }) => {
  test.skip(!tenantId, "PLAYWRIGHT_APPROVALS_TENANT_ID not set");
  test.skip(!authority || !clientId || !token, "OIDC token env vars not set");

  await page.goto("/");
  const signButton = page.getByRole("button", { name: /sign in|sign out/i });
  await expect(signButton).toBeVisible({ timeout: 20000 });
  const approvals = page.getByTestId("approvals-inbox");
  await approvals.getByPlaceholder("tenant_id").fill(tenantId ?? "");
  const loadButton = approvals.getByRole("button", { name: "Load approvals" });
  await expect(loadButton).toBeEnabled({ timeout: 15000 });
  await loadButton.click();

  const error = page.getByText(/failed to load approvals/i);
  await expect(error).toHaveCount(0);
});

test("trace lookup returns payload", async ({ page }) => {
  test.skip(!traceId, "PLAYWRIGHT_TRACE_ID not set");
  test.skip(!authority || !clientId || !token, "OIDC token env vars not set");

  await page.route("**/api/trace/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        trace: {
          trace_id: traceId ?? "trace-1",
          sources: ["doc-test-1:chunk-1"],
        },
        citations: [
          {
            doc_id: "doc-test-1",
            chunk_id: "chunk-1",
            score: 0.42,
          },
        ],
      }),
    });
  });

  await page.goto("/");
  const signButton = page.getByRole("button", { name: /sign in|sign out/i });
  await expect(signButton).toBeVisible({ timeout: 20000 });
  if (tenantId) {
    await page.getByLabel("Tenant ID").fill(tenantId);
  }
  const traceSection = page.getByTestId("trace-lookup");
  await traceSection.getByPlaceholder("trace_id").fill(traceId ?? "");
  const lookupButton = traceSection.getByRole("button", { name: "Lookup" });
  await expect(lookupButton).toBeEnabled({ timeout: 10000 });
  await lookupButton.click();

  await expect(page.locator("pre")).toBeVisible({ timeout: 10000 });
});

test("health view tolerates degraded backend", async ({ page }) => {
  await page.route("**/api/health", async (route) => {
    await route.fulfill({
      status: 503,
      contentType: "application/json",
      body: JSON.stringify({
        ok: false,
        services: {},
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  const signButton = page.getByRole("button", { name: /sign in|sign out/i });
  await expect(signButton).toBeVisible({ timeout: 20000 });
  await expect(page.getByText(/no health data available/i)).toBeVisible({ timeout: 15000 });
});
