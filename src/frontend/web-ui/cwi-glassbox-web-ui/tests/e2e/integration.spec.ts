import { test, expect } from "@playwright/test";

const baseUrl = process.env.PLAYWRIGHT_BASE_URL;

test("gateway health responds", async ({ request }) => {
  test.skip(!baseUrl, "PLAYWRIGHT_BASE_URL not set");

  const response = await request.get(`${baseUrl}/api/health`);
  expect(response.status()).toBeLessThan(500);
});

test("readiness responds", async ({ request }) => {
  test.skip(!baseUrl, "PLAYWRIGHT_BASE_URL not set");

  const response = await request.get(`${baseUrl}/api/ready`);
  expect(response.status()).toBeLessThan(500);
});
