import { defineConfig } from "@playwright/test";

const port = 3202;
const baseUrl = `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 30_000,
  expect: {
    timeout: 10_000,
  },
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: baseUrl,
    headless: true,
    trace: "retain-on-failure",
  },
  webServer: {
    command: "npm run build && npm run start -- --hostname 127.0.0.1 --port 3202",
    cwd: __dirname,
    url: baseUrl,
    timeout: 300_000,
    reuseExistingServer: !process.env.CI,
    env: {
      HOSTNAME: "127.0.0.1",
      PORT: String(port),
      CWI_GATEWAY_BASE_URL: "http://127.0.0.1:3999",
      NEXT_PUBLIC_CWI_GATEWAY_BASE_URL: "http://127.0.0.1:3999",
      NEXT_PUBLIC_CWI_DEFAULT_TENANT: "tenant-1",
      NEXT_PUBLIC_CWI_AUTH_MODE: "token",
      NEXT_PUBLIC_CWI_REQUEST_TIMEOUT_MS: "5000",
    },
  },
});
