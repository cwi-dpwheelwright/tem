import { expect, test } from "@playwright/test";

function createJwt(expirySecondsFromNow = 3600) {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url");
  return `${encode({ alg: "none", typ: "JWT" })}.${encode({
    sub: "demo-user",
    tenant_id: "tenant-1",
    exp: Math.floor(Date.now() / 1000) + expirySecondsFromNow,
  })}.sig`;
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript((token) => {
    window.sessionStorage.setItem("cwi.user.web.token.access", token);
    window.sessionStorage.setItem("cwi.user.web.token.lastUsed", Date.now().toString());
    window.sessionStorage.setItem("cwi.user.web.user.label", "Demo User");
    window.localStorage.setItem("cwi.user.web.overrides", JSON.stringify({
      authMode: "token",
      tenantId: "tenant-1",
    }));
    window.localStorage.setItem("cwi.user.web.pack.selected.tenant:tenant-1:user:demo-user", "enterprise");
  }, createJwt());

  await page.route("**/api/health", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        ok: true,
        ready: true,
        checks: {
          config: {
            status: "ok",
            detail: null,
            missing_env: [],
            used_dev_defaults: [],
            auth_mode: "token",
            gateway_base_url: "http://127.0.0.1:3999",
          },
          api: {
            status: "ok",
            detail: null,
          },
        },
        timestamp: new Date().toISOString(),
      }),
    });
  });

  await page.route("**/api/history**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [] }),
    });
  });

  await page.route("**/api/packs/availability", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        generated_utc: new Date().toISOString(),
        status: "available",
        available_pack_count: 1,
        filtered_pack_count: 0,
        unindexed_pack_count: 0,
        indexed_pack_count: 1,
        discovered_pack_count: 1,
        reasons: [],
      }),
    });
  });

  await page.route("**/api/packs/index.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        packs: [{
          pack_id: "enterprise",
          name: "enterprise",
          latest: "0.0.2",
          versions: ["0.0.2"],
          visibility: "public",
          retired: false,
          retired_utc: null,
        }],
      }),
    });
  });

  await page.route("**/api/packs/enterprise/pack.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        pack_id: "enterprise",
        pack_version: "0.0.2",
        name: "enterprise",
        modules: [{ id: "module-enterprise", title: "General", actions: [] }],
      }),
    });
  });

  await page.route("**/api/packs/enterprise/anchor.txt", async (route) => {
    await route.fulfill({ status: 404, body: "" });
  });
});

test("type-anywhere focuses the main input and preserves text", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });

  const input = page.locator("#cwi-main-input");
  await expect(input).toBeVisible();

  await page.keyboard.type("hello");
  await expect(input).toBeFocused();
  await expect(input).toHaveValue("hello");

  await input.clear();
  await page.locator(".home-history-body").click({ position: { x: 20, y: 20 } });
  await page.keyboard.type("what is cwi");
  await expect(input).toBeFocused();
  await expect(input).toHaveValue("what is cwi");
});

test("slash focuses main input without inserting slash", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });

  const input = page.locator("#cwi-main-input");
  await expect(input).toBeVisible();

  await page.locator(".home-history-body").click({ position: { x: 20, y: 20 } });
  await page.keyboard.press("/");

  await expect(input).toBeFocused();
  await expect(input).toHaveValue("");
});
