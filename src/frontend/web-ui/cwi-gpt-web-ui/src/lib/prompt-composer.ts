export const MAX_ATTACHMENTS = 8;
export const MAX_ATTACHMENT_EXCERPT_CHARS = 2500;
export const MAX_IMPORTED_HISTORY_CHARS = 8000;
export const MAX_RECENT_CHAT_TURNS = 3;
export const MAX_RELEVANT_CHAT_TURNS = 3;
export const MAX_CHAT_CONTEXT_CHARS = 6000;
export const MAX_CHAT_CONTEXT_TURN_CHARS = 900;
const MIN_CHAT_HISTORY_RELEVANCE_SCORE = 4;
const MIN_CHAT_HISTORY_HIGH_SIGNAL_MATCHES = 2;
export const MAX_RELEVANT_SOURCES = 4;
export const MAX_SOURCE_CONTEXT_CHARS = 5000;
export const MAX_SOURCE_CONTEXT_ITEM_CHARS = 1200;
const RESPONSE_CONSTRAINTS = [
  "Response constraints:",
  "- Keep the answer simple, fast, concise, and actionable.",
  "- Limit priorities to 3-5, risks to 1-2, changes to 3-5, and suggestions to 3.",
  "- Do not expose internal routing, evidence review, template analysis, or response-generation steps.",
].join("\n");

export interface ImportedChatHistoryParseOptions {
  maxChars?: number | null;
}

const TEXT_LIKE_EXTENSIONS = new Set([
  "c",
  "cc",
  "cpp",
  "cs",
  "css",
  "csv",
  "go",
  "graphql",
  "h",
  "html",
  "htm",
  "java",
  "js",
  "json",
  "jsonl",
  "kt",
  "log",
  "lua",
  "markdown",
  "md",
  "mjs",
  "py",
  "rb",
  "rs",
  "sh",
  "sql",
  "svg",
  "swift",
  "tex",
  "toml",
  "ts",
  "tsx",
  "txt",
  "xml",
  "yaml",
  "yml",
]);

export type ComposerAttachmentKind = "file" | "image";

export interface ComposerAttachment {
  id: string;
  name: string;
  mimeType: string;
  size: number;
  kind: ComposerAttachmentKind;
  excerpt?: string;
  truncated?: boolean;
}

export interface ImportedChatHistory {
  name: string;
  content: string;
  messageCount: number | null;
  truncated: boolean;
}

export interface RecentChatTurn {
  question: string;
  answer: string;
  createdAt?: string;
}

export interface ChatHistoryContext {
  content: string;
  recentTurnCount: number;
  relevantTurnCount: number;
  truncated: boolean;
}

export interface SourceContext {
  content: string;
  sourceCount: number;
  truncated: boolean;
}

interface StructuredMessage {
  role: string;
  text: string;
  createTime: number | null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function normalizeTextBlock(value: string): string {
  return value.replace(/\r\n/g, "\n").replace(/\u0000/g, "").replace(/\n{3,}/g, "\n\n").trim();
}

function truncateText(value: string, maxChars: number): { text: string; truncated: boolean } {
  const normalized = normalizeTextBlock(value);
  if (normalized.length <= maxChars) {
    return { text: normalized, truncated: false };
  }

  return {
    text: `${normalized.slice(0, Math.max(0, maxChars)).trimEnd()}\n...[truncated]`,
    truncated: true,
  };
}

function truncateInlineText(value: string, maxChars: number): { text: string; truncated: boolean } {
  const normalized = normalizeTextBlock(value);
  if (normalized.length <= maxChars) {
    return { text: normalized, truncated: false };
  }

  return {
    text: `${normalized.slice(0, Math.max(0, maxChars)).trimEnd()}...[truncated]`,
    truncated: true,
  };
}

function formatTurnCount(count: number): string {
  return count === 1 ? "1 turn" : `${count} turns`;
}

const CHAT_CONTEXT_STOP_WORDS = new Set([
  "about",
  "after",
  "again",
  "also",
  "and",
  "are",
  "can",
  "could",
  "did",
  "does",
  "for",
  "from",
  "give",
  "have",
  "how",
  "into",
  "just",
  "like",
  "more",
  "need",
  "please",
  "should",
  "that",
  "the",
  "this",
  "was",
  "were",
  "what",
  "when",
  "where",
  "with",
  "would",
  "you",
]);

const LOW_SIGNAL_CHAT_CONTEXT_TOKENS = new Set([
  "deploy",
  "deployed",
  "deployment",
  "home",
  "local",
  "node",
  "nodes",
  "plan",
  "runtime",
  "step",
  "steps",
]);

const CONTEXT_DEPENDENT_PROMPT_TOKENS = new Set([
  "above",
  "continue",
  "earlier",
  "follow-up",
  "last",
  "previous",
  "prior",
  "recent",
  "summarize",
  "summary",
]);

const SOURCE_INTENT_TOKENS = new Set([
  "attachment",
  "attachments",
  "context",
  "document",
  "documents",
  "file",
  "files",
  "source",
  "sources",
  "upload",
  "uploaded",
]);

function tokenizeForChatContext(value: string): Set<string> {
  const tokens = normalizeTextBlock(value)
    .toLowerCase()
    .match(/[a-z0-9][a-z0-9_-]{2,}/g);

  return new Set((tokens ?? []).filter((token) => !CHAT_CONTEXT_STOP_WORDS.has(token)));
}

function scoreTurnAgainstPrompt(turn: RecentChatTurn, promptTokens: Set<string>): number {
  if (promptTokens.size === 0) {
    return 0;
  }

  const turnTokens = tokenizeForChatContext(`${turn.question}\n${turn.answer}`);
  let score = 0;
  let highSignalMatches = 0;
  promptTokens.forEach((token) => {
    if (turnTokens.has(token)) {
      if (LOW_SIGNAL_CHAT_CONTEXT_TOKENS.has(token)) {
        score += 1;
      } else {
        highSignalMatches += 1;
        score += 2;
      }
    }
  });
  return highSignalMatches >= MIN_CHAT_HISTORY_HIGH_SIGNAL_MATCHES &&
    score >= MIN_CHAT_HISTORY_RELEVANCE_SCORE
    ? score
    : 0;
}

function isContextDependentPrompt(prompt: string, promptTokens: Set<string>): boolean {
  const normalized = ` ${normalizeTextBlock(prompt).toLowerCase()} `;
  if (/\b(that|this|those|these|it|them)\b/.test(normalized)) {
    return true;
  }

  for (const token of promptTokens) {
    if (CONTEXT_DEPENDENT_PROMPT_TOKENS.has(token)) {
      return true;
    }
  }
  return false;
}

function scoreSourceAgainstPrompt(source: ComposerAttachment, promptTokens: Set<string>): number {
  if (promptTokens.size === 0) {
    return 0;
  }

  const sourceTokens = tokenizeForChatContext(`${source.name}\n${source.excerpt ?? ""}`);
  let score = 0;
  promptTokens.forEach((token) => {
    if (sourceTokens.has(token)) {
      score += source.name.toLowerCase().includes(token) ? 3 : 1;
    }
  });
  return score;
}

function hasSourceIntent(promptTokens: Set<string>): boolean {
  for (const token of promptTokens) {
    if (SOURCE_INTENT_TOKENS.has(token)) {
      return true;
    }
  }
  return false;
}

function getString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function getNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function extractText(value: unknown, depth = 0): string {
  if (depth > 6 || value === null || value === undefined) {
    return "";
  }
  if (typeof value === "string") {
    return value.trim();
  }
  if (typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }
  if (Array.isArray(value)) {
    return value
      .map((item) => extractText(item, depth + 1))
      .filter(Boolean)
      .join("\n")
      .trim();
  }
  if (!isRecord(value)) {
    return "";
  }

  const directText =
    getString(value.text) ??
    getString(value.value) ??
    getString(value.body) ??
    getString(value.caption);
  if (directText) {
    return directText;
  }

  const nested =
    extractText(value.parts, depth + 1) ||
    extractText(value.content, depth + 1) ||
    extractText(value.message, depth + 1) ||
    extractText(value.messages, depth + 1);
  if (nested) {
    return nested;
  }

  return "";
}

function normalizeRole(role: string | null): string {
  switch ((role ?? "").toLowerCase()) {
    case "assistant":
      return "Assistant";
    case "system":
      return "System";
    case "tool":
      return "Tool";
    case "developer":
      return "Developer";
    default:
      return "User";
  }
}

function parseStructuredMessage(candidate: unknown): StructuredMessage | null {
  if (!isRecord(candidate)) {
    return null;
  }

  const nestedMessage = isRecord(candidate.message) ? candidate.message : candidate;
  const author = isRecord(nestedMessage.author)
    ? nestedMessage.author
    : isRecord(candidate.author)
      ? candidate.author
      : null;
  const role = normalizeRole(getString(nestedMessage.role) ?? getString(author?.role));
  const text = extractText(
    nestedMessage.content ??
      candidate.content ??
      nestedMessage.parts ??
      candidate.parts ??
      nestedMessage.text ??
      candidate.text,
  );

  if (!text) {
    return null;
  }

  return {
    role,
    text: normalizeTextBlock(text),
    createTime:
      getNumber(nestedMessage.create_time) ??
      getNumber(candidate.create_time) ??
      getNumber(nestedMessage.timestamp) ??
      getNumber(candidate.timestamp),
  };
}

function collectStructuredMessages(parsed: unknown): StructuredMessage[] {
  const candidates: StructuredMessage[] = [];

  const appendMessages = (items: unknown[]) => {
    items.forEach((item) => {
      const parsedItem = parseStructuredMessage(item);
      if (parsedItem) {
        candidates.push(parsedItem);
      }
    });
  };

  if (Array.isArray(parsed)) {
    appendMessages(parsed);
  } else if (isRecord(parsed)) {
    if (Array.isArray(parsed.messages)) {
      appendMessages(parsed.messages);
    }
    if (Array.isArray(parsed.items)) {
      appendMessages(parsed.items);
    }
    if (isRecord(parsed.mapping)) {
      appendMessages(Object.values(parsed.mapping));
    }
  }

  return candidates
    .map((item, index) => ({ item, index }))
    .sort((left, right) => {
      const leftTime = left.item.createTime ?? Number.POSITIVE_INFINITY;
      const rightTime = right.item.createTime ?? Number.POSITIVE_INFINITY;
      if (leftTime === rightTime) {
        return left.index - right.index;
      }
      return leftTime - rightTime;
    })
    .map(({ item }) => item);
}

function formatAttachmentPromptBlock(attachment: ComposerAttachment, index: number): string {
  const label = attachment.kind === "image" ? "Image" : "File";
  const typeLabel = attachment.mimeType || "unknown type";
  const header = `${index + 1}. ${label}: ${attachment.name} (${typeLabel}, ${formatFileSize(attachment.size)})`;

  if (attachment.excerpt?.trim()) {
    return [
      header,
      attachment.excerpt.trim(),
      attachment.truncated ? "[Attachment excerpt truncated for prompt length.]" : "",
    ]
      .filter(Boolean)
      .join("\n");
  }

  if (attachment.kind === "image") {
    return [
      header,
      "Image attached in the web UI. This client forwards the filename and image metadata, but not OCR text.",
    ].join("\n");
  }

  return [
    header,
    "No readable text preview was extracted from this file. Use the filename and metadata only.",
  ].join("\n");
}

export function isImageFileType(mimeType: string | null | undefined): boolean {
  return Boolean(mimeType && mimeType.toLowerCase().startsWith("image/"));
}

export function isTextLikeFile(name: string, mimeType: string | null | undefined): boolean {
  const normalizedType = mimeType?.toLowerCase() ?? "";
  if (
    normalizedType.startsWith("text/") ||
    normalizedType.includes("json") ||
    normalizedType.includes("xml") ||
    normalizedType.includes("yaml") ||
    normalizedType.includes("javascript") ||
    normalizedType.includes("typescript")
  ) {
    return true;
  }

  const extension = name.includes(".") ? name.split(".").pop()?.toLowerCase() ?? "" : "";
  return TEXT_LIKE_EXTENSIONS.has(extension);
}

export function buildAttachmentExcerpt(rawText: string): { excerpt: string; truncated: boolean } {
  const result = truncateText(rawText, MAX_ATTACHMENT_EXCERPT_CHARS);
  return {
    excerpt: result.text,
    truncated: result.truncated,
  };
}

export function parseImportedChatHistory(
  fileName: string,
  rawText: string,
  options?: ImportedChatHistoryParseOptions,
): ImportedChatHistory {
  const normalizedRaw = normalizeTextBlock(rawText);
  let content = normalizedRaw;
  let messageCount: number | null = null;

  try {
    const parsed = JSON.parse(rawText) as unknown;
    const messages = collectStructuredMessages(parsed);
    if (messages.length > 0) {
      content = messages.map((message) => `${message.role}: ${message.text}`).join("\n\n");
      messageCount = messages.length;
    }
  } catch {
    content = normalizedRaw;
  }

  const maxChars = options?.maxChars ?? MAX_IMPORTED_HISTORY_CHARS;
  const truncated =
    maxChars === null
      ? { text: normalizeTextBlock(content), truncated: false }
      : truncateText(content, maxChars);

  return {
    name: fileName,
    content: truncated.text,
    messageCount,
    truncated: truncated.truncated,
  };
}

function formatChatTurns(turns: RecentChatTurn[], heading: string, maxTurnChars: number): {
  text: string;
  truncated: boolean;
} {
  let truncated = false;
  const text = [
    heading,
    ...turns.map((turn, index) => {
      const question = truncateInlineText(turn.question, maxTurnChars);
      const answer = truncateInlineText(turn.answer, maxTurnChars);
      truncated ||= question.truncated || answer.truncated;

      const timestamp = turn.createdAt ? ` (${turn.createdAt})` : "";
      return [
        `Turn ${index + 1}${timestamp}`,
        `User: ${question.text}`,
        `Assistant: ${answer.text}`,
      ].join("\n");
    }),
  ].join("\n\n");

  return { text, truncated };
}

export function buildChatHistoryContext(
  prompt: string,
  turns: RecentChatTurn[],
  options?: {
    maxRecentTurns?: number;
    maxRelevantTurns?: number;
    maxChars?: number;
    maxTurnChars?: number;
    includeRecentWithoutPrompt?: boolean;
  },
): ChatHistoryContext | null {
  const maxRecentTurns = options?.maxRecentTurns ?? MAX_RECENT_CHAT_TURNS;
  const maxRelevantTurns = options?.maxRelevantTurns ?? MAX_RELEVANT_CHAT_TURNS;
  const maxChars = options?.maxChars ?? MAX_CHAT_CONTEXT_CHARS;
  const maxTurnChars = options?.maxTurnChars ?? MAX_CHAT_CONTEXT_TURN_CHARS;
  const eligibleTurns = turns
    .filter((turn) => normalizeTextBlock(turn.question) && normalizeTextBlock(turn.answer));

  if (eligibleTurns.length === 0) {
    return null;
  }

  const recentTurns = eligibleTurns.slice(-Math.max(0, maxRecentTurns));
  const olderTurns = eligibleTurns.slice(0, Math.max(0, eligibleTurns.length - recentTurns.length));
  const promptTokens = tokenizeForChatContext(prompt);
  const shouldIncludeRecent =
    options?.includeRecentWithoutPrompt === true || isContextDependentPrompt(prompt, promptTokens);
  const relevantRecentTurns = shouldIncludeRecent
    ? recentTurns
    : recentTurns
      .map((turn, index) => ({
        turn,
        index,
        score: scoreTurnAgainstPrompt(turn, promptTokens),
      }))
      .filter((item) => item.score > 0)
      .sort((left, right) => left.index - right.index)
      .map((item) => item.turn);
  const relevantOlderTurns = olderTurns
    .map((turn, index) => ({
      turn,
      index,
      score: scoreTurnAgainstPrompt(turn, promptTokens),
    }))
    .filter((item) => item.score > 0)
    .sort((left, right) => {
      if (left.score !== right.score) {
        return right.score - left.score;
      }
      return right.index - left.index;
    })
    .slice(0, Math.max(0, maxRelevantTurns))
    .sort((left, right) => left.index - right.index)
    .map((item) => item.turn);

  if (relevantRecentTurns.length === 0 && relevantOlderTurns.length === 0) {
    return null;
  }

  const sections: string[] = [];
  let truncated = false;

  if (relevantRecentTurns.length > 0) {
    const recent = formatChatTurns(
      relevantRecentTurns,
      `Recent chat history (${formatTurnCount(relevantRecentTurns.length)}):`,
      maxTurnChars,
    );
    sections.push(recent.text);
    truncated ||= recent.truncated;
  }

  if (relevantOlderTurns.length > 0) {
    const relevant = formatChatTurns(
      relevantOlderTurns,
      `Relevant older chat history selected for this prompt (${formatTurnCount(relevantOlderTurns.length)}):`,
      maxTurnChars,
    );
    sections.push(relevant.text);
    truncated ||= relevant.truncated;
  }

  const limited = truncateText(sections.join("\n\n"), maxChars);
  return {
    content: limited.text,
    recentTurnCount: relevantRecentTurns.length,
    relevantTurnCount: relevantOlderTurns.length,
    truncated: truncated || limited.truncated,
  };
}

export function buildRecentChatHistory(
  turns: RecentChatTurn[],
  options?: { maxTurns?: number; maxChars?: number; maxTurnChars?: number },
): ChatHistoryContext | null {
  return buildChatHistoryContext("", turns, {
    maxRecentTurns: options?.maxTurns,
    maxRelevantTurns: 0,
    maxChars: options?.maxChars,
    maxTurnChars: options?.maxTurnChars,
    includeRecentWithoutPrompt: true,
  });
}

export function buildSourceContext(
  prompt: string,
  sources: ComposerAttachment[],
  options?: { maxSources?: number; maxChars?: number; maxSourceChars?: number },
): SourceContext | null {
  const maxSources = options?.maxSources ?? MAX_RELEVANT_SOURCES;
  const maxChars = options?.maxChars ?? MAX_SOURCE_CONTEXT_CHARS;
  const maxSourceChars = options?.maxSourceChars ?? MAX_SOURCE_CONTEXT_ITEM_CHARS;
  const promptTokens = tokenizeForChatContext(prompt);
  const eligibleSources = sources.filter((source) => normalizeTextBlock(`${source.name}\n${source.excerpt ?? ""}`));

  if (eligibleSources.length === 0 || maxSources <= 0) {
    return null;
  }

  const scoredSources = eligibleSources
    .map((source, index) => ({
      source,
      index,
      score: scoreSourceAgainstPrompt(source, promptTokens),
    }))
    .filter((item) => item.score > 0)
    .sort((left, right) => {
      if (left.score !== right.score) {
        return right.score - left.score;
      }
      return right.index - left.index;
    });

  const selected =
    scoredSources.length > 0
      ? scoredSources.slice(0, maxSources)
      : hasSourceIntent(promptTokens)
        ? eligibleSources.slice(0, maxSources).map((source, index) => ({ source, index, score: 0 }))
        : [];

  if (selected.length === 0) {
    return null;
  }

  let truncated = false;
  const content = selected
    .sort((left, right) => left.index - right.index)
    .map(({ source }, index) => {
      const excerpt = source.excerpt
        ? truncateText(source.excerpt, maxSourceChars)
        : {
            text:
              source.kind === "image"
                ? "Image source saved in the web UI. No OCR text is available yet; use filename and metadata only."
                : "No readable text preview was extracted from this source; use filename and metadata only.",
            truncated: false,
          };
      truncated ||= excerpt.truncated || Boolean(source.truncated);

      return [
        `${index + 1}. ${source.kind === "image" ? "Image" : "File"}: ${source.name} (${source.mimeType || "unknown type"}, ${formatFileSize(source.size)})`,
        excerpt.text,
        source.truncated ? "[Source excerpt truncated during upload.]" : "",
      ]
        .filter(Boolean)
        .join("\n");
    })
    .join("\n\n");

  const limited = truncateText(content, maxChars);
  return {
    content: limited.text,
    sourceCount: selected.length,
    truncated: truncated || limited.truncated,
  };
}

export function buildAskQuestion(
  prompt: string,
  attachments: ComposerAttachment[],
  importedHistory: ImportedChatHistory | null,
  recentHistory: ChatHistoryContext | null = null,
  sourceContext: SourceContext | null = null,
): string {
  const trimmedPrompt = normalizeTextBlock(prompt);
  if (!trimmedPrompt) {
    return "";
  }
  const sections = [
    `Primary question:\n${trimmedPrompt}`,
    "The primary question is authoritative. Use the user-provided context below only when it directly supports the primary question.",
    RESPONSE_CONSTRAINTS,
  ];

  if (attachments.length > 0) {
    sections.push(
      [
        "Attached files and images:",
        ...attachments.map((attachment, index) => formatAttachmentPromptBlock(attachment, index)),
      ].join("\n\n"),
    );
  }

  if (importedHistory) {
    const metadata = [
      importedHistory.name,
      importedHistory.messageCount !== null ? `${importedHistory.messageCount} messages` : null,
    ]
      .filter(Boolean)
      .join(", ");

    sections.push(
      [
        `Imported external chat history (${metadata}):`,
        importedHistory.content,
        importedHistory.truncated ? "[Imported history truncated for prompt length.]" : "",
      ]
        .filter(Boolean)
        .join("\n"),
    );
  }

  if (sourceContext) {
    sections.push(
      [
        `Relevant workspace Sources selected for this prompt (${sourceContext.sourceCount} ${sourceContext.sourceCount === 1 ? "source" : "sources"}):`,
        sourceContext.content,
        sourceContext.truncated ? "[Workspace Sources truncated for prompt length.]" : "",
      ]
        .filter(Boolean)
        .join("\n"),
    );
  }

  if (recentHistory) {
    sections.push(
      [
        "Conversation context from this chat:",
        recentHistory.content,
        recentHistory.truncated ? "[Conversation context truncated for prompt length.]" : "",
      ]
        .filter(Boolean)
        .join("\n"),
    );
  }

  sections.push("Answer the primary question directly and mention when the uploaded context is incomplete.");
  return sections.join("\n\n");
}

export function buildAskQuestionSummary(
  prompt: string,
  attachments: ComposerAttachment[],
  importedHistory: ImportedChatHistory | null,
  recentHistory: ChatHistoryContext | null = null,
  sourceContext: SourceContext | null = null,
): string {
  const trimmedPrompt = normalizeTextBlock(prompt);
  if (!trimmedPrompt) {
    return "";
  }

  const contextParts: string[] = [];
  if (attachments.length > 0) {
    contextParts.push(attachments.length === 1 ? "1 file/image" : `${attachments.length} files/images`);
  }
  if (importedHistory) {
    contextParts.push("external chat history");
  }
  if (recentHistory) {
    contextParts.push("conversation context");
  }
  if (sourceContext) {
    contextParts.push("workspace Sources");
  }

  if (contextParts.length === 0) {
    return trimmedPrompt;
  }

  return `${trimmedPrompt}\n\n[Context included: ${contextParts.join(", ")}]`;
}

export function formatFileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 1024) {
    return `${Math.max(0, Math.round(bytes || 0))} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  if (bytes < 1024 * 1024 * 1024) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}
