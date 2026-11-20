import { describe, expect, it } from "vitest";
import {
  buildAskQuestion,
  buildAskQuestionSummary,
  buildAttachmentExcerpt,
  buildChatHistoryContext,
  buildRecentChatHistory,
  buildSourceContext,
  formatFileSize,
  parseImportedChatHistory,
  type ComposerAttachment,
} from "@/lib/prompt-composer";

describe("prompt-composer", () => {
  it("formats structured JSON chat history into a readable transcript", () => {
    const history = parseImportedChatHistory(
      "chat.json",
      JSON.stringify({
        messages: [
          { role: "system", content: "Stay concise." },
          { role: "user", content: "Summarize the outage." },
          { role: "assistant", content: { parts: ["The outage started at 9:14 AM."] } },
        ],
      }),
    );

    expect(history.messageCount).toBe(3);
    expect(history.content).toContain("System: Stay concise.");
    expect(history.content).toContain("User: Summarize the outage.");
    expect(history.content).toContain("Assistant: The outage started at 9:14 AM.");
  });

  it("supports ChatGPT-style mapping exports", () => {
    const history = parseImportedChatHistory(
      "chatgpt-export.json",
      JSON.stringify({
        mapping: {
          a: {
            create_time: 1,
            message: {
              author: { role: "user" },
              content: { parts: ["How do I prepare the board update?"] },
            },
          },
          b: {
            create_time: 2,
            message: {
              author: { role: "assistant" },
              content: { parts: ["Start with the topline risks and mitigations."] },
            },
          },
        },
      }),
    );

    expect(history.messageCount).toBe(2);
    expect(history.content).toContain("User: How do I prepare the board update?");
    expect(history.content).toContain("Assistant: Start with the topline risks and mitigations.");
  });

  it("builds a prompt with attachment and imported history sections", () => {
    const attachments: ComposerAttachment[] = [
      {
        id: "file-1",
        name: "notes.md",
        mimeType: "text/markdown",
        size: 2048,
        kind: "file",
        excerpt: "Maintenance steps:\n1. Drain the line.\n2. Swap the valve.",
      },
    ];
    const importedHistory = parseImportedChatHistory(
      "external.txt",
      "User: We already tried resetting the controller.\nAssistant: That cleared the alarm for 5 minutes.",
    );

    const prompt = buildAskQuestion("What should we try next?", attachments, importedHistory);

    expect(prompt).toContain("Primary question:\nWhat should we try next?");
    expect(prompt).toContain("Attached files and images:");
    expect(prompt).toContain("notes.md");
    expect(prompt).toContain("Imported external chat history (external.txt):");
    expect(prompt).toContain("User: We already tried resetting the controller.");
  });

  it("includes bounded recent chat history for follow-up questions", () => {
    const recentHistory = buildRecentChatHistory([
      {
        question: "What did the uptime check show?",
        answer: "The gateway was healthy, but Redis latency was elevated.",
        createdAt: "2026-04-20T12:00:00Z",
      },
      {
        question: "What should we do next?",
        answer: "Check the Redis container logs and recent deployment events.",
        createdAt: "2026-04-20T12:01:00Z",
      },
    ]);

    const prompt = buildAskQuestion("Can you summarize that into an update?", [], null, recentHistory);

    expect(prompt).toContain("Primary question:\nCan you summarize that into an update?");
    expect(prompt).toContain("Conversation context from this chat:");
    expect(prompt).toContain("Recent chat history (2 turns):");
    expect(prompt).toContain("User: What did the uptime check show?");
    expect(prompt).toContain("Assistant: Check the Redis container logs");
  });

  it("limits recent chat history to the most recent completed turns", () => {
    const recentHistory = buildRecentChatHistory(
      Array.from({ length: 8 }, (_, index) => ({
        question: `Question ${index + 1}`,
        answer: `Answer ${index + 1}`,
      })),
      { maxTurns: 3 },
    );

    expect(recentHistory?.recentTurnCount).toBe(3);
    expect(recentHistory?.content).not.toContain("Question 5");
    expect(recentHistory?.content).toContain("Question 6");
    expect(recentHistory?.content).toContain("Question 8");
  });

  it("adds only relevant older turns beyond the small recent window", () => {
    const context = buildChatHistoryContext(
      "Can you give me a Redis latency executive update?",
      [
        {
          question: "What did Redis latency look like?",
          answer: "Redis latency was elevated after the deployment.",
        },
        {
          question: "What color should the button be?",
          answer: "Use the existing blue button style.",
        },
        {
          question: "What did the uptime check show?",
          answer: "The gateway was healthy.",
        },
        {
          question: "What should we do next?",
          answer: "Check the current gateway logs.",
        },
      ],
      { maxRecentTurns: 2, maxRelevantTurns: 2 },
    );

    expect(context?.recentTurnCount).toBe(0);
    expect(context?.relevantTurnCount).toBe(1);
    expect(context?.content).toContain("Relevant older chat history selected for this prompt (1 turn):");
    expect(context?.content).toContain("Redis latency was elevated");
    expect(context?.content).not.toContain("current gateway logs");
    expect(context?.content).not.toContain("blue button style");
  });

  it("does not inject unrelated chat history for a standalone prompt", () => {
    const context = buildChatHistoryContext("What is 2 + 2?", [
      {
        question: "How should we deploy the home node?",
        answer: "Use the private deploy inventory and verify the QR endpoint.",
      },
      {
        question: "What was the latest memory pack anchor?",
        answer: "The latest runtime anchor was still being verified.",
      },
    ]);

    expect(context).toBeNull();
  });

  it("does not use low-signal deploy and node overlap as relevant memory", () => {
    const context = buildChatHistoryContext("Give me a plan to deploy a node", [
      {
        question: "Deploy the current home node implementation",
        answer: "The home node runtime was rebuilt and linked to the coordinator.",
      },
    ]);

    expect(context).toBeNull();
  });

  it("keeps recent history for context-dependent follow-up prompts", () => {
    const context = buildChatHistoryContext("Can you summarize that into an update?", [
      {
        question: "What did the uptime check show?",
        answer: "The gateway was healthy, but Redis latency was elevated.",
        createdAt: "2026-04-20T12:00:00Z",
      },
      {
        question: "What should we do next?",
        answer: "Check the Redis container logs and recent deployment events.",
        createdAt: "2026-04-20T12:01:00Z",
      },
    ]);

    expect(context?.recentTurnCount).toBe(2);
    expect(context?.content).toContain("Recent chat history (2 turns):");
    expect(context?.content).toContain("Redis latency was elevated");
  });

  it("selects only relevant workspace Sources for the prompt", () => {
    const sourceContext = buildSourceContext("What should we say about Redis latency?", [
      {
        id: "source-1",
        name: "redis-incident-notes.md",
        mimeType: "text/markdown",
        size: 1024,
        kind: "file",
        excerpt: "Redis latency increased after the deployment and caused slower responses.",
      },
      {
        id: "source-2",
        name: "button-copy.md",
        mimeType: "text/markdown",
        size: 512,
        kind: "file",
        excerpt: "Use direct action copy for the upload button.",
      },
    ]);

    expect(sourceContext?.sourceCount).toBe(1);
    expect(sourceContext?.content).toContain("redis-incident-notes.md");
    expect(sourceContext?.content).toContain("Redis latency increased");
    expect(sourceContext?.content).not.toContain("button-copy.md");
  });

  it("falls back to available Sources when the prompt explicitly asks about uploaded context", () => {
    const sourceContext = buildSourceContext("What can you tell me from the uploaded files?", [
      {
        id: "source-1",
        name: "operations-notes.md",
        mimeType: "text/markdown",
        size: 1024,
        kind: "file",
        excerpt: "The operations handoff notes mention delayed approvals.",
      },
    ]);

    expect(sourceContext?.sourceCount).toBe(1);
    expect(sourceContext?.content).toContain("operations-notes.md");
  });

  it("summarizes attached context for history display", () => {
    const attachments: ComposerAttachment[] = [
      {
        id: "file-1",
        name: "notes.md",
        mimeType: "text/markdown",
        size: 2048,
        kind: "file",
      },
      {
        id: "image-1",
        name: "diagram.png",
        mimeType: "image/png",
        size: 4096,
        kind: "image",
      },
    ];
    const importedHistory = parseImportedChatHistory("external.txt", "User: Context");

    const recentHistory = buildRecentChatHistory([{ question: "What failed?", answer: "The deploy gate failed." }]);

    const sourceContext = buildSourceContext("How do we recover from the uploaded recovery source?", [
      {
        id: "source-1",
        name: "recovery.md",
        mimeType: "text/markdown",
        size: 1024,
        kind: "file",
        excerpt: "Recovery requires validating the deploy gate.",
      },
    ]);

    expect(buildAskQuestionSummary("How do we recover?", attachments, importedHistory, recentHistory, sourceContext)).toBe(
      "How do we recover?\n\n[Context included: 2 files/images, external chat history, conversation context, workspace Sources]",
    );
  });

  it("builds readable excerpts and file sizes", () => {
    expect(buildAttachmentExcerpt("A short note.").excerpt).toBe("A short note.");
    expect(formatFileSize(900)).toBe("900 B");
    expect(formatFileSize(2048)).toBe("2.0 KB");
    expect(formatFileSize(2 * 1024 * 1024)).toBe("2.0 MB");
    expect(formatFileSize(3 * 1024 * 1024 * 1024)).toBe("3.0 GB");
  });
});
