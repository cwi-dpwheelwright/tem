import { expect, test, type Page } from "@playwright/test";

function createJwt(expirySecondsFromNow = 3600) {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url");
  return `${encode({ alg: "none", typ: "JWT" })}.${encode({
    sub: "demo-user",
    tenant_id: "tenant-1",
    exp: Math.floor(Date.now() / 1000) + expirySecondsFromNow,
  })}.sig`;
}

function createTinyPngBuffer() {
  return Buffer.from(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5l0AAAAASUVORK5CYII=",
    "base64",
  );
}

function promptUploadInput(page: Page) {
  return page.locator("input[type='file'][accept*='image/*']").nth(1);
}

function sourceUploadInput(page: Page) {
  return page.locator("input[type='file'][accept*='image/*']").first();
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
    window.localStorage.setItem("cwi.user.web.pack.selected.tenant:tenant-1:user:demo-user", "demo-pack");
    window.localStorage.setItem("cwi.user.web.pack.verified.demo-pack", JSON.stringify({
      name: "demo-pack",
      displayName: "Demo Pack",
      version: "1.0.0",
      hash: "demo-pack-hash",
      verifiedAt: "2026-04-03T12:00:00Z",
      modules: [],
    }));
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

  await page.route("**/api/packs/index.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([{ pack_id: "demo-pack", name: "Demo Pack", latest: "1.0.0" }]),
    });
  });

  await page.route("**/api/packs/demo-pack/pack.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        pack_id: "demo-pack",
        pack_version: "1.0.0",
        name: "Demo Pack",
        modules: [],
      }),
    });
  });

  await page.route("**/api/packs/demo-pack/anchor.txt", async (route) => {
    await route.fulfill({
      status: 404,
      contentType: "text/plain",
      body: "",
    });
  });

  await page.route("**/api/history**", async (route) => {
    const method = route.request().method().toUpperCase();
    if (method === "POST") {
      const body = route.request().postDataJSON() as Record<string, unknown>;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: "hist-1",
          request_id: body.request_id ?? "req-1",
          question: body.question ?? "",
          answer: body.answer ?? "",
          created_at: new Date().toISOString(),
          trace_id: body.trace_id ?? "trace-1",
          pack_name: body.pack_name ?? "demo-pack",
          pack_hash: body.pack_hash ?? "demo-pack-hash",
          trace: body.trace ?? null,
        }),
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [] }),
    });
  });
});

test("shows the upload modal while file/image attachments are being prepared", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });
  await expect(page.locator("button[aria-label='Add files']")).toBeVisible();

  await promptUploadInput(page).setInputFiles({
    name: "attachment.txt",
    mimeType: "text/plain",
    buffer: Buffer.from("Attachment body for upload modal test."),
  });

  const dialog = page.getByRole("dialog", { name: "File upload status" });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText("Uploading file/image");
  await expect(dialog).toContainText("attachment.txt");

  await expect(dialog).toBeHidden();
  await expect(page.locator(".home-compose-chip-title")).toContainText("attachment.txt");
  await expect(page.locator(".home-compose-note")).toContainText("Uploaded context will be included with this ask.");
});

test("shows a notice modal when uploads exceed the composer attachment limit", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });
  await expect(page.locator("button[aria-label='Add files']")).toBeVisible();

  await promptUploadInput(page).setInputFiles(
    Array.from({ length: 9 }, (_, index) => ({
      name: `attachment-${index + 1}.txt`,
      mimeType: "text/plain",
      buffer: Buffer.from(`Attachment ${index + 1}`),
    })),
  );

  const dialog = page.getByRole("dialog", { name: "File upload status" });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText("Some uploads were skipped");
  await expect(dialog).toContainText("composer limit is 8");

  await page.getByRole("button", { name: "Continue" }).click();
  await expect(dialog).toBeHidden();
  await expect(page.locator(".home-compose-chip-title")).toHaveCount(8);
});

test("attaches uploaded images side-by-side until a prompt with text is sent", async ({ page }) => {
  await page.route("**/api/ask", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        answer: "Images received.",
        citations: [],
        trace: {
          request_id: "req-image-1",
          trace_id: "trace-image-1",
          confidence: 0.92,
          sources: [],
        },
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });

  await promptUploadInput(page).setInputFiles([
    {
      name: "diagram-1.png",
      mimeType: "image/png",
      buffer: createTinyPngBuffer(),
    },
    {
      name: "diagram-2.png",
      mimeType: "image/png",
      buffer: createTinyPngBuffer(),
    },
  ]);

  const dialog = page.getByRole("dialog", { name: "File upload status" });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText("Uploading file/image");
  await expect(dialog).toBeHidden();

  const imageCards = page.locator(".home-compose-image-card");
  await expect(imageCards).toHaveCount(2);
  await expect(page.locator(".home-compose-stage-title")).toContainText("2 images attached to this prompt");
  await expect(page.locator(".home-compose-stage-hint")).toContainText("Add text and send");
  await expect(page.locator(".home-compose-note")).toContainText("Add text when you're ready to send them.");

  const firstBox = await imageCards.nth(0).boundingBox();
  const secondBox = await imageCards.nth(1).boundingBox();
  expect(firstBox).not.toBeNull();
  expect(secondBox).not.toBeNull();
  expect((secondBox?.x ?? 0)).toBeGreaterThan(firstBox?.x ?? 0);

  await page.locator(".home-input").fill("Please review these diagrams.");
  await page.getByRole("button", { name: "Send" }).click();

  await expect(imageCards).toHaveCount(0);
  await expect(page.locator(".home-history-message--question")).toContainText("Please review these diagrams.");
  await expect(page.locator(".home-history-attachment-image")).toHaveCount(2);
});

test("sends bounded conversation context with follow-up prompts", async ({ page }) => {
  await page.route("**/api/history**", async (route) => {
    const method = route.request().method().toUpperCase();
    if (method === "POST") {
      const body = route.request().postDataJSON() as Record<string, unknown>;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: "hist-follow-up",
          request_id: body.request_id ?? "req-follow-up",
          question: body.question ?? "",
          answer: body.answer ?? "",
          created_at: new Date().toISOString(),
          trace_id: body.trace_id ?? "trace-follow-up",
          pack_name: body.pack_name ?? "demo-pack",
          pack_hash: body.pack_hash ?? "demo-pack-hash",
          trace: body.trace ?? null,
        }),
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [
          {
            id: "hist-prior",
            request_id: "req-prior",
            question: "What did the health check show?",
            answer: "The gateway was healthy, but Redis latency was elevated.",
            created_at: "2026-04-20T12:00:00Z",
            trace_id: "trace-prior",
            pack_name: "demo-pack",
            pack_hash: "demo-pack-hash",
            trace: null,
            attachments: [],
          },
        ],
      }),
    });
  });

  let askedQuestion = "";
  await page.route("**/api/ask", async (route) => {
    const body = route.request().postDataJSON() as { question?: string };
    askedQuestion = body.question ?? "";
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        answer: "Use the prior Redis latency detail in the update.",
        citations: [],
        trace: {
          request_id: "req-follow-up",
          trace_id: "trace-follow-up",
          confidence: 0.9,
          sources: [],
        },
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await expect(page.locator(".home-history-message--question")).toContainText("What did the health check show?");

  await page.locator(".home-input").fill("Can you turn that into an executive update?");
  await page.getByRole("button", { name: "Send" }).click();

  await expect.poll(() => askedQuestion).toContain("Primary question:\nCan you turn that into an executive update?");
  expect(askedQuestion).toContain("Conversation context from this chat:");
  expect(askedQuestion).toContain("Recent chat history (1 turn):");
  expect(askedQuestion).toContain("User: What did the health check show?");
  expect(askedQuestion).toContain("Assistant: The gateway was healthy, but Redis latency was elevated.");
});

test("retrieves relevant workspace Sources without sending unrelated Sources", async ({ page }) => {
  let askedQuestion = "";
  const ingestedSourceTitles: string[] = [];
  await page.route("**/api/ingest/document", async (route) => {
    const body = route.request().postDataJSON() as { document?: { doc_id?: string; title?: string } };
    ingestedSourceTitles.push(body.document?.title ?? "");
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        accepted: true,
        ingest_id: `ingest-${ingestedSourceTitles.length}`,
        trace_id: `trace-ingest-${ingestedSourceTitles.length}`,
        doc_id: body.document?.doc_id ?? null,
        message: "accepted",
      }),
    });
  });
  await page.route("**/api/ask", async (route) => {
    const body = route.request().postDataJSON() as { question?: string };
    askedQuestion = body.question ?? "";
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        answer: "Redis latency increased after the deploy.",
        citations: [],
        trace: {
          request_id: "req-sources",
          trace_id: "trace-sources",
          confidence: 0.9,
          sources: [],
        },
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });

  await sourceUploadInput(page).setInputFiles([
    {
      name: "redis-incident-notes.md",
      mimeType: "text/markdown",
      buffer: Buffer.from("Redis latency increased after the deployment and slowed responses."),
    },
    {
      name: "button-copy.md",
      mimeType: "text/markdown",
      buffer: Buffer.from("Use direct action copy for upload buttons."),
    },
  ]);

  const dialog = page.getByRole("dialog", { name: "File upload status" });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText("Uploading file/image");
  await expect(dialog).toBeHidden();
  await expect(page.locator(".home-sources-card-title")).toHaveCount(2);
  await expect.poll(() => ingestedSourceTitles).toEqual(["redis-incident-notes.md", "button-copy.md"]);

  await page.getByRole("tab", { name: /Chats/ }).click();
  await page.locator(".home-input").fill("What should we say about Redis latency?");
  await page.getByRole("button", { name: "Send" }).click();

  await expect.poll(() => askedQuestion).toContain("Relevant workspace Sources selected for this prompt (1 source):");
  expect(askedQuestion).toContain("redis-incident-notes.md");
  expect(askedQuestion).toContain("Redis latency increased after the deployment");
  expect(askedQuestion).not.toContain("button-copy.md");
});
