import { defineConfig } from "@playwright/test";

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:8090";

export default defineConfig({
  testDir: "./tests/e2e",
  use: {
    baseURL,
    trace: "retain-on-failure",
  },
  webServer: {
    command: "npm run dev -- --host 0.0.0.0 --port 8090",
    url: baseURL,
    reuseExistingServer: true,
    timeout: 120000,
  },
  retries: 0,
});
