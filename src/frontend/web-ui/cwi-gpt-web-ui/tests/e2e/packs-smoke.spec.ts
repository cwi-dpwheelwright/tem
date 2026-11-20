import { expect, test } from "@playwright/test";

function createJwt(expirySecondsFromNow = 3600) {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url");
  return `${encode({ alg: "none", typ: "JWT" })}.${encode({
    sub: "demo-user",
    tenant_id: "tenant-1",
    exp: Math.floor(Date.now() / 1000) + expirySecondsFromNow,
  })}.sig`;
}

const livePacks = [
  {
    pack_id: "enterprise",
    name: "enterprise",
    latest: "0.0.2",
    versions: ["0.0.2"],
    visibility: "public",
    retired: false,
    retired_utc: null,
  },
  {
    pack_id: "engineering-pack",
    name: "engineering-pack",
    latest: "1.0.0",
    versions: ["1.0.0"],
    visibility: "public",
    retired: false,
    retired_utc: null,
  },
  {
    pack_id: "personal",
    name: "personal",
    latest: "1.0.0",
    versions: ["1.0.0"],
    visibility: "public",
    retired: false,
    retired_utc: null,
  },
];

const manifests: Record<string, Record<string, unknown>> = {
  enterprise: {
    pack_id: "enterprise",
    pack_version: "0.0.2",
    name: "enterprise",
    description: "Enterprise policy pack",
    modules: [{ id: "module-enterprise", title: "General (default)", actions: [] }],
  },
  "engineering-pack": {
    pack_id: "engineering-pack",
    pack_version: "1.0.0",
    name: "engineering-pack",
    description: "Engineering knowledge pack",
    modules: [{ id: "module-engineering", title: "Engineering", actions: [] }],
  },
  personal: {
    pack_id: "personal",
    pack_version: "1.0.0",
    name: "personal",
    description: "Personal knowledge pack",
    modules: [{ id: "module-personal", title: "Personal", actions: [] }],
  },
};

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
        available_pack_count: livePacks.length,
        filtered_pack_count: 0,
        unindexed_pack_count: 0,
        indexed_pack_count: livePacks.length,
        discovered_pack_count: livePacks.length,
        reasons: [],
      }),
    });
  });

  await page.route("**/api/packs/index.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ packs: livePacks }),
    });
  });

  await page.route("**/api/packs/*/*", async (route) => {
    const url = new URL(route.request().url());
    const parts = url.pathname.split("/").filter(Boolean);
    const packName = parts[parts.length - 2];
    const fileName = parts[parts.length - 1];

    if (!packName || !fileName) {
      await route.fulfill({ status: 404, body: "not found" });
      return;
    }

    if (fileName === "pack.json" && manifests[packName]) {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(manifests[packName]),
      });
      return;
    }

    if (fileName === "anchor.txt") {
      await route.fulfill({ status: 404, body: "not found" });
      return;
    }

    await route.fulfill({ status: 404, body: "not found" });
  });
});

test("loads multiple packs through the app pack proxy and surfaces them in the home UI", async ({ page }) => {
  const requestedPackUrls: string[] = [];
  page.on("request", (request) => {
    const url = request.url();
    if (url.includes("/packs/")) {
      requestedPackUrls.push(url);
    }
  });

  const packsResponsePromise = page.waitForResponse((response) => response.url().includes("/api/packs/index.json"));
  await page.goto("/", { waitUntil: "domcontentloaded" });
  const packsResponse = await packsResponsePromise;
  const packsPayload = await packsResponse.json() as { packs?: Array<{ name?: string }> };
  const apiPackCount = packsPayload.packs?.length ?? 0;
  const packSwitcher = page.locator(".home-pack-switcher");

  await expect(page.getByText(`${apiPackCount} live verified packs available.`)).toBeVisible();
  await expect(packSwitcher.getByRole("button", { name: "enterprise" })).toBeVisible();
  await expect(packSwitcher.getByRole("button", { name: "engineering-pack" })).toBeVisible();
  await expect(packSwitcher.getByRole("button", { name: "personal" })).toBeVisible();
  await expect(page.locator(".home-pack-chip")).toHaveCount(apiPackCount);

  expect(requestedPackUrls.some((url) => url.includes("/api/packs/index.json"))).toBe(true);
  expect(requestedPackUrls.some((url) => url.includes("/api/packs/enterprise/pack.json"))).toBe(true);
  expect(requestedPackUrls.every((url) => !url.includes("/packs/") || url.includes("/api/packs/"))).toBe(true);
});

test("shows the full live verified pack list in Trust and allows switching packs", async ({ page }) => {
  const packsResponsePromise = page.waitForResponse((response) => response.url().includes("/api/packs/index.json"));
  await page.goto("/", { waitUntil: "domcontentloaded" });
  const packsResponse = await packsResponsePromise;
  const packsPayload = await packsResponse.json() as { packs?: Array<{ name?: string }> };
  const apiPackCount = packsPayload.packs?.length ?? 0;

  await page.getByRole("button", { name: "View all" }).click();

  const dialog = page.getByRole("dialog", { name: "Pack trust" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText("Selected pack:")).toContainText("enterprise");
  await expect(dialog.locator(".pack-option")).toHaveCount(apiPackCount);
  await expect(dialog.getByRole("button", { name: /enterprise/i })).toBeVisible();
  await expect(dialog.getByRole("button", { name: /engineering-pack/i })).toBeVisible();
  await expect(dialog.getByRole("button", { name: /personal/i })).toBeVisible();

  await dialog.getByRole("button", { name: /personal/i }).click();
  await expect(dialog.locator(".pack-selected-name")).toHaveText("personal");
});
