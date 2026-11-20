"""Local model routing helpers for home-node execution."""

from __future__ import annotations

from dataclasses import dataclass
import json
from typing import Any, Protocol
from urllib import request

from .config import AgentConfig


class EvidenceMatch(Protocol):
    title: str
    path: str
    excerpt: str


@dataclass(frozen=True)
class LocalModelResult:
    answer: str
    model: str


def describe_local_capability_details(config: AgentConfig) -> dict[str, Any]:
    if not config.local_model.enabled:
        return {}

    return {
        "local_model_profile": _public_model_profile(config),
        "routing": {
            "home_node_profile": "small_quantized_local",
            "pack_guidance_mode": "compact_cached",
            "retrieval_mode": "relevant_chunks_only",
            "heavy_reasoning_target": "rack_core",
        },
    }


def describe_local_model_health(config: AgentConfig) -> dict[str, Any]:
    if not config.local_model.enabled:
        return {}

    return {
        "model_profile": _public_model_profile(config),
        "hardware": {
            "configured_gpu_vram_gb": config.local_model.max_vram_gb,
        },
    }


def should_run_local_model(config: AgentConfig, payload: dict[str, Any]) -> bool:
    if not config.local_model.enabled:
        return False

    execution = payload.get("execution")
    if not isinstance(execution, dict):
        return False

    route_mode = _read_string(execution, "route_mode") or _read_string(execution, "mode")
    if route_mode and route_mode.strip().lower() == "local_model":
        return True

    return _read_bool(execution, "use_local_model") is True


def generate_local_model_answer(
    config: AgentConfig,
    *,
    question: str,
    matches: tuple[EvidenceMatch, ...],
    payload: dict[str, Any],
) -> LocalModelResult:
    prompt = _build_prompt(question, matches, payload)
    body = {
        "model": config.local_model.model,
        "prompt": prompt,
        "stream": False,
        "options": {
            "num_ctx": _resolve_context_tokens(config, payload),
        },
    }
    data = json.dumps(body).encode("utf-8")
    req = request.Request(
        _ollama_generate_url(config.local_model.base_url),
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with request.urlopen(req, timeout=config.local_model.timeout_seconds) as response:
        raw = response.read().decode("utf-8")
    payload_json = json.loads(raw)
    if not isinstance(payload_json, dict):
        raise RuntimeError("local model returned an invalid response")

    answer = payload_json.get("response")
    if not isinstance(answer, str) or not answer.strip():
        raise RuntimeError("local model returned an empty response")

    model = payload_json.get("model")
    return LocalModelResult(
        answer=answer.strip(),
        model=model if isinstance(model, str) and model.strip() else config.local_model.model,
    )


def _public_model_profile(config: AgentConfig) -> dict[str, Any]:
    return {
        "enabled": True,
        "provider": config.local_model.provider,
        "model": config.local_model.model,
        "model_family": config.local_model.model_family,
        "quantization": config.local_model.quantization,
        "context_tokens": config.local_model.context_tokens,
        "max_vram_gb": config.local_model.max_vram_gb,
        "timeout_seconds": config.local_model.timeout_seconds,
    }


def _build_prompt(question: str, matches: tuple[EvidenceMatch, ...], payload: dict[str, Any]) -> str:
    evidence_lines = []
    for index, match in enumerate(matches[:4], start=1):
        source = match.title or match.path or f"source-{index}"
        excerpt = " ".join((match.excerpt or "").split())
        if excerpt:
            evidence_lines.append(f"[{index}] {source}: {excerpt}")

    evidence = "\n".join(evidence_lines) if evidence_lines else "No matching local evidence was found."
    grounding_mode = "grounded_local_evidence" if evidence_lines else "non_grounded_reasoning"
    return "\n".join(
        [
            "You are the CWI home-node local model.",
            "Use local evidence when it is available.",
            (
                "If no local evidence is available, still answer general knowledge, math, "
                "simple reasoning, and conceptual questions from model reasoning."
            ),
            "Keep the answer concise, honest, and natural.",
            (
                "Do not expose secrets or invent private/project-specific facts. "
                "For private or project-specific questions without evidence, say local evidence is not available."
            ),
            "",
            "Compact governance:",
            "- Keep private/simple work local.",
            "- Use relevant chunks only; do not assume unavailable pack text.",
            "- Preserve safety and honesty over completeness.",
            "",
            f"Route reason: {_route_reason(payload)}",
            f"Grounding mode: {grounding_mode}",
            "",
            "Question:",
            question.strip(),
            "",
            "Local evidence:",
            evidence,
        ]
    )


def _resolve_context_tokens(config: AgentConfig, payload: dict[str, Any]) -> int:
    execution = payload.get("execution")
    if isinstance(execution, dict):
        raw = execution.get("max_context_tokens")
        if isinstance(raw, int) and raw > 0:
            return min(raw, config.local_model.context_tokens)
        if isinstance(raw, str):
            try:
                parsed = int(raw.strip())
                if parsed > 0:
                    return min(parsed, config.local_model.context_tokens)
            except ValueError:
                pass
    return config.local_model.context_tokens


def _route_reason(payload: dict[str, Any]) -> str:
    execution = payload.get("execution")
    if isinstance(execution, dict):
        reason = _read_string(execution, "route_reason")
        if reason:
            return reason
    return "local_model_route"


def _ollama_generate_url(base_url: str) -> str:
    return base_url.rstrip("/") + "/api/generate"


def _read_string(values: dict[str, Any], key: str) -> str | None:
    value = values.get(key)
    return value if isinstance(value, str) and value.strip() else None


def _read_bool(values: dict[str, Any], key: str) -> bool | None:
    value = values.get(key)
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        lowered = value.strip().lower()
        if lowered in {"true", "1", "yes", "y"}:
            return True
        if lowered in {"false", "0", "no", "n"}:
            return False
    return None
