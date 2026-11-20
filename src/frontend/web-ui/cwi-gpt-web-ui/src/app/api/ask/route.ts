import { NextResponse } from "next/server";
import { randomUUID } from "crypto";
import { proxyGatewayRequest } from "@/app/api/_shared/gateway";
import { isDirectLlmEnabled, requestLlmAnswer, resolveLlmConfig } from "@/app/api/_shared/llm";
import type { AskRequest, AskResponse, ModelTier, Trace } from "@/lib/api/types";

const FALLBACK_MODEL_TIER: ModelTier = "edge";
const DIRECT_LLM_TIMEOUT_MESSAGE = "CWI is still working, but the model request took longer than the wait limit. Please retry in a moment.";

function buildTrace(request: AskRequest, model: ModelTier): Trace {
  const deterministic = /^(?:what\s+is|calculate|compute|solve)?\s*[-+]?\d+(?:\.\d+)?\s*(?:\+|-|\*|\/|x)\s*[-+]?\d+(?:\.\d+)?\s*\??$/i.test(
    request.question.trim(),
  );
  const trustedWebRequested = request.context?.web_retrieval?.enabled === true;
  return {
    trace_id: randomUUID(),
    sources: [],
    confidence: deterministic ? 0.95 : 0.8,
    confidence_basis: deterministic ? "deterministic_reasoning_non_grounded" : "non_grounded_general_reasoning",
    grounding_mode: "non_grounded",
    grounded: false,
    retrieval_allowed: false,
    retrieval_skipped_reason: deterministic
      ? "deterministic_reasoning_does_not_require_sources"
      : "direct_llm_path_has_no_enterprise_retrieval",
    policy_mode: request.context?.mode ?? "observe",
    model,
    pack_hash: request.pack?.anchor ?? "local",
    pack_version: undefined,
    trusted_web_requested: trustedWebRequested || undefined,
    trusted_web_enabled: trustedWebRequested ? false : undefined,
    trusted_web_hits: trustedWebRequested ? 0 : undefined,
    trusted_web_skipped_reason: trustedWebRequested ? "direct_llm_path_has_no_trusted_web_context" : undefined,
  };
}

async function handleLlmRequest(request: Request) {
  let payload: AskRequest;
  try {
    payload = (await request.json()) as AskRequest;
  } catch {
    return NextResponse.json(
      { error: "invalid_request", message: "Invalid JSON payload." },
      { status: 400 },
    );
  }

  if (!payload.question?.trim()) {
    return NextResponse.json(
      { error: "invalid_request", message: "Question is required." },
      { status: 400 },
    );
  }

  const llmConfig = resolveLlmConfig();
  if (!llmConfig) {
    return NextResponse.json(
      { error: "config_missing", message: "LLM configuration is missing." },
      { status: 500 },
    );
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(DIRECT_LLM_TIMEOUT_MESSAGE), llmConfig.timeoutMs);

  try {
    const answer = await requestLlmAnswer(payload.question, llmConfig, controller.signal);
    const response: AskResponse = {
      answer,
      citations: [],
      trace: buildTrace(payload, FALLBACK_MODEL_TIER),
    };
    return NextResponse.json(response);
  } catch (err) {
    const message = isAbortLikeError(err, controller.signal)
      ? DIRECT_LLM_TIMEOUT_MESSAGE
      : err instanceof Error ? err.message : "LLM request failed.";
    return NextResponse.json({ error: "llm_request_failed", message }, { status: 502 });
  } finally {
    clearTimeout(timeout);
  }
}

function isAbortLikeError(error: unknown, signal: AbortSignal): boolean {
  if (signal.aborted) {
    return true;
  }

  if (!(error instanceof Error)) {
    return false;
  }

  return error.name === "AbortError"
    || /aborted without reason/i.test(error.message)
    || /operation was aborted/i.test(error.message);
}

export async function POST(request: Request) {
  const llmConfig = resolveLlmConfig();
  if (llmConfig && isDirectLlmEnabled()) {
    return handleLlmRequest(request);
  }

  return proxyGatewayRequest(request, "/api/ask");
}
