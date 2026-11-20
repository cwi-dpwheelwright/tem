import { normalizeUrl } from "@/lib/config";

export type LlmProvider = "openai" | "ollama";

export interface LlmConfig {
  baseUrl: string;
  model: string;
  provider: LlmProvider;
  apiKey?: string;
  chatPath: string;
  healthPath: string;
  timeoutMs: number;
  temperature?: number;
  maxTokens?: number;
  systemPrompt?: string;
}

const DEFAULT_TIMEOUT_MS = 15000;
const DEFAULT_TEMPERATURE = 0.2;
const DEFAULT_MAX_TOKENS = 512;
const DEFAULT_OPENAI_CHAT_PATH = "/v1/chat/completions";
const DEFAULT_OPENAI_HEALTH_PATH = "/v1/models";
const DEFAULT_OLLAMA_CHAT_PATH = "/api/generate";
const DEFAULT_OLLAMA_HEALTH_PATH = "/api/tags";

function readEnv(name: string): string | null {
  const value = process.env[name];
  if (!value || !value.trim()) {
    return null;
  }
  return value.trim();
}

function readBoolEnv(name: string, fallback: boolean): boolean {
  const value = readEnv(name);
  if (!value) {
    return fallback;
  }

  return value.toLowerCase() === "true";
}

function normalizeProvider(value: string | null): LlmProvider {
  if (value && value.toLowerCase() === "ollama") {
    return "ollama";
  }
  return "openai";
}

function normalizePath(value: string | null, fallback: string): string {
  const raw = value && value.trim() ? value.trim() : fallback;
  if (!raw.startsWith("/")) {
    return `/${raw}`;
  }
  return raw;
}

function parseNumber(value: string | null, fallback: number): number {
  if (!value) {
    return fallback;
  }
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }
  return parsed;
}

export function resolveLlmConfig(): LlmConfig | null {
  const baseUrl = readEnv("CWI_LLM_BASE_URL");
  const model = readEnv("CWI_LLM_MODEL");
  if (!baseUrl || !model) {
    return null;
  }

  const provider = normalizeProvider(readEnv("CWI_LLM_PROVIDER"));
  const apiKey = readEnv("CWI_LLM_API_KEY") ?? undefined;
  const chatPath = normalizePath(
    readEnv("CWI_LLM_CHAT_PATH"),
    provider === "ollama" ? DEFAULT_OLLAMA_CHAT_PATH : DEFAULT_OPENAI_CHAT_PATH,
  );
  const healthPath = normalizePath(
    readEnv("CWI_LLM_HEALTH_PATH"),
    provider === "ollama" ? DEFAULT_OLLAMA_HEALTH_PATH : DEFAULT_OPENAI_HEALTH_PATH,
  );
  const timeoutMs = Math.max(1000, parseNumber(readEnv("CWI_LLM_TIMEOUT_MS"), DEFAULT_TIMEOUT_MS));
  const temperature = parseNumber(readEnv("CWI_LLM_TEMPERATURE"), DEFAULT_TEMPERATURE);
  const maxTokens = Math.max(1, Math.floor(parseNumber(readEnv("CWI_LLM_MAX_TOKENS"), DEFAULT_MAX_TOKENS)));
  const systemPrompt = readEnv("CWI_LLM_SYSTEM_PROMPT") ?? undefined;

  return {
    baseUrl: normalizeUrl(baseUrl),
    model,
    provider,
    apiKey,
    chatPath,
    healthPath,
    timeoutMs,
    temperature,
    maxTokens,
    systemPrompt,
  };
}

export function isDirectLlmEnabled(): boolean {
  return readBoolEnv("CWI_UI_DIRECT_LLM_ENABLED", false);
}

export function resolveLlmChatUrl(config: LlmConfig): string {
  return `${normalizeUrl(config.baseUrl)}${config.chatPath}`;
}

export function resolveLlmHealthUrl(config: LlmConfig): string {
  return `${normalizeUrl(config.baseUrl)}${config.healthPath}`;
}

export function buildLlmHeaders(config: LlmConfig): Headers {
  const headers = new Headers();
  headers.set("Accept", "application/json");
  headers.set("Content-Type", "application/json");
  if (config.apiKey) {
    headers.set("Authorization", `Bearer ${config.apiKey}`);
  }
  return headers;
}

export async function requestLlmAnswer(
  question: string,
  config: LlmConfig,
  signal: AbortSignal,
): Promise<string> {
  const headers = buildLlmHeaders(config);
  const url = resolveLlmChatUrl(config);

  if (config.provider === "ollama") {
    const prompt = config.systemPrompt
      ? `${config.systemPrompt}\n\n${question}`
      : question;

    const body = {
      model: config.model,
      prompt,
      stream: false,
      options: {
        temperature: config.temperature,
      },
    };

    const response = await fetch(url, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
      signal,
    });

    const text = await response.text();
    if (!response.ok) {
      throw new Error(text || `LLM request failed with status ${response.status}`);
    }

    if (!text) {
      return "";
    }

    const data = JSON.parse(text) as { response?: string };
    return data.response?.trim() ?? "";
  }

  const messages: Array<{ role: "system" | "user"; content: string }> = [];
  if (config.systemPrompt) {
    messages.push({ role: "system", content: config.systemPrompt });
  }
  messages.push({ role: "user", content: question });

  const body: Record<string, unknown> = {
    model: config.model,
    messages,
    temperature: config.temperature,
    max_tokens: config.maxTokens,
  };

  const response = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify(body),
    signal,
  });

  const text = await response.text();
  if (!response.ok) {
    throw new Error(text || `LLM request failed with status ${response.status}`);
  }

  if (!text) {
    return "";
  }

  const data = JSON.parse(text) as {
    choices?: Array<{ message?: { content?: string } }>;
  };

  return data.choices?.[0]?.message?.content?.trim() ?? "";
}
