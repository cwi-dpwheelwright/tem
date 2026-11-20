import { test, expect } from "@playwright/test";

const baseUrl = process.env.PLAYWRIGHT_BASE_URL;
const packsBaseUrl = process.env.PLAYWRIGHT_PACKS_BASE_URL;

test("pack catalog shows missing config warning", async ({ page }) => {
  test.skip(!baseUrl, "PLAYWRIGHT_BASE_URL not set");
  test.skip(!!packsBaseUrl, "Pack base URL configured");

  await page.goto("/");
  await expect(page.getByText(/missing pack service configuration/i)).toBeVisible();
});

test("pack catalog loads index and details", async ({ page }) => {
  test.skip(!baseUrl, "PLAYWRIGHT_BASE_URL not set");
  test.skip(!packsBaseUrl, "PLAYWRIGHT_PACKS_BASE_URL not set");

  await page.route("**/packs/index.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        packs: [
          {
            pack_id: "enterprise",
            name: "Enterprise",
            description: "Enterprise pack",
            latest: "1.0.0",
            versions: ["1.0.0"],
          },
        ],
      }),
    });
  });

  await page.route("**/packs/enterprise/pack.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        pack_id: "enterprise",
        pack_version: "1.0.0",
        name: "Enterprise",
        description: "Enterprise pack",
        modules: [{ name: "governance" }],
        dependencies: { packs: [] },
        metadata: { capability_map: ["ledger", "approvals"] },
      }),
    });
  });

  await page.route("**/packs/enterprise/anchor.txt", async (route) => {
    await route.fulfill({ status: 200, body: "abc123hash" });
  });

  const indexResponse = page.waitForResponse((response) =>
    response.url().includes("/packs/index.json"),
  );
  await page.goto("/");
  const signButton = page.getByRole("button", { name: /sign in|sign out/i });
  await expect(signButton).toBeVisible({ timeout: 20000 });
  await indexResponse;
  await expect(
    page.getByRole("button", { name: "Enterprise Enterprise pack" }),
  ).toBeVisible({ timeout: 15000 });
  await expect(page.getByText(/Anchor: abc123hash/i)).toBeVisible({ timeout: 15000 });
});
