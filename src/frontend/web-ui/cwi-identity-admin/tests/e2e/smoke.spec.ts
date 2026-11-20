import { expect, test, type Page } from "@playwright/test";
const identityBaseUrl =
  process.env.PLAYWRIGHT_IDENTITY_BASE_URL ?? "http://localhost:8085";
const adminIdentifier = process.env.PLAYWRIGHT_ADMIN_IDENTIFIER;
const adminPassword = process.env.PLAYWRIGHT_ADMIN_PASSWORD;
const adminTenantId = process.env.PLAYWRIGHT_ADMIN_TENANT_ID;
const usersTenantId = process.env.PLAYWRIGHT_TENANT_ID;

const requireEnv = (names: string[]) => {
  const missing = names.filter((name) => !process.env[name]);
  if (missing.length > 0) {
    test.skip(true, `Missing env: ${missing.join(", ")}`);
  }
};

const login = async (page: Page) => {
  requireEnv(["PLAYWRIGHT_ADMIN_IDENTIFIER", "PLAYWRIGHT_ADMIN_PASSWORD"]);

  await page.goto("/login");
  await page.getByLabel(/identity base url/i).fill(identityBaseUrl);
  if (adminTenantId?.trim()) {
    await page.getByLabel(/tenant id/i).fill(adminTenantId);
  }
  await page.getByLabel(/admin username or email/i).fill(adminIdentifier ?? "");
  await page.getByLabel(/password/i).fill(adminPassword ?? "");

  const loginResponsePromise = page.waitForResponse((response) => {
    return (
      response.url().includes("/auth/login") &&
      response.request().method() === "POST"
    );
  });

  await page.getByRole("button", { name: /enter admin console/i }).click();

  const loginResponse = await loginResponsePromise;
  expect(loginResponse.ok()).toBeTruthy();
  await expect(page.getByRole("heading", { name: "Tenants" })).toBeVisible();
};

test("redirects unauthenticated users to login", async ({ page }) => {
  await page.goto("/tenants");
  await expect(page).toHaveURL(/\/login$/);
  await expect(
    page.getByRole("heading", { name: /identity command center/i }),
  ).toBeVisible();
});

test("login flow reaches tenants list", async ({ page }) => {
  const tenantsResponsePromise = page.waitForResponse((response) => {
    return (
      response.url().includes("/admin/tenants") &&
      response.request().method() === "GET"
    );
  });

  await login(page);

  const tenantsResponse = await tenantsResponsePromise;
  expect(tenantsResponse.ok()).toBeTruthy();
  await expect(page.getByText(/failed to load tenants/i)).toHaveCount(0);
});

test("users view loads roles and users", async ({ page }) => {
  requireEnv(["PLAYWRIGHT_TENANT_ID"]);
  await login(page);

  await page.goto("/users");

  const usersResponsePromise = page.waitForResponse((response) => {
    return (
      response.url().includes("/admin/users") &&
      response.request().method() === "GET"
    );
  });

  const rolesResponsePromise = page.waitForResponse((response) => {
    return (
      response.url().includes("/admin/roles") &&
      response.request().method() === "GET"
    );
  });

  await page.getByLabel(/tenant id/i).fill(usersTenantId ?? "");

  const [usersResponse, rolesResponse] = await Promise.all([
    usersResponsePromise,
    rolesResponsePromise,
  ]);

  expect(usersResponse.ok()).toBeTruthy();
  expect(rolesResponse.ok()).toBeTruthy();
  await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();
  await expect(page.getByText(/failed to load users/i)).toHaveCount(0);
});
