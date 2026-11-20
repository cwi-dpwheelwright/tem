import re
from typing import Any


VERSION = "runtime-critic-external-v1"

BACKEND_LANGUAGE_RE = re.compile(
    r"\b(?:governed evidence|activated user memory|retrieval domain|source domain|repo area|runbook|graph priority|synced memory)\b",
    re.IGNORECASE,
)

INTERNAL_CONTEXT_PREFACE_RE = re.compile(
    r"^\s*(?:here\s+is|based\s+on|from)\b[^\n]{0,180}?\b(?:activated|imported|uploaded|stored|governed)\b",
    re.IGNORECASE,
)


def evaluate(payload: dict[str, Any]) -> dict[str, Any]:
    context = _dict(payload.get("context"))
    response = _dict(payload.get("response"))
    findings: list[dict[str, Any]] = []
    scores = [
        _score_routing(context, response, findings),
        _score_memory_relevance(context, response, findings),
        _score_source_quality(context, response, findings),
        _score_synthesis_quality(context, response, findings),
        _score_policy_alignment(context, response, findings),
    ]
    overall = round(sum(float(score["score"]) for score in scores) / len(scores), 3) if scores else 1.0
    return {
        "version": VERSION,
        "mode": "observe_only",
        "overall_score": overall,
        "scores": scores,
        "findings": findings,
    }


def _score_routing(
    context: dict[str, Any],
    response: dict[str, Any],
    findings: list[dict[str, Any]],
) -> dict[str, Any]:
    if _eq(response.get("execution_target"), "home_node") and not bool(context.get("home_node_id_present")):
        findings.append(
            _finding(
                "fail",
                "routing",
                "home_node_target_missing_node",
                "The response says the home-node route was used, but no home node ID is present.",
            )
        )
        return _score("routing_correctness", 0.2, "home_node_target_missing_node")

    if (
        bool(context.get("home_node_id_present"))
        and _eq(context.get("execution_state"), "completed")
        and _eq(response.get("execution_target"), "shared_compute")
        and _eq(context.get("home_node_execution_mode"), "local_ask")
    ):
        findings.append(
            _finding(
                "warn",
                "routing",
                "linked_node_context_returned_shared_compute",
                "A linked home-node context completed through shared compute after local ASK context was present.",
            )
        )
        return _score("routing_correctness", 0.7, "linked_node_context_returned_shared_compute")

    return _score("routing_correctness", 1.0, "routing_state_consistent")


def _score_memory_relevance(
    context: dict[str, Any],
    response: dict[str, Any],
    findings: list[dict[str, Any]],
) -> dict[str, Any]:
    answer = _string(response.get("answer"))
    counts = _dict(context.get("counts"))
    if INTERNAL_CONTEXT_PREFACE_RE.search(answer):
        findings.append(
            _finding(
                "warn",
                "memory",
                "backend_memory_preface_visible",
                "The answer still exposes backend memory/context wording.",
            )
        )
        return _score("memory_relevance", 0.45, "backend_memory_preface_visible")

    if _count(counts, "memory_results") > 0 and _eq(context.get("presentation_template_family"), "direct_answer"):
        findings.append(
            _finding(
                "warn",
                "memory",
                "memory_present_for_direct_answer",
                "Memory was active for a direct-answer response where it may not belong.",
            )
        )
        return _score("memory_relevance", 0.7, "memory_present_for_direct_answer")

    if (
        _eq(context.get("context_awareness_mode"), "memory")
        and _count(counts, "memory_results") == 0
        and _count(counts, "source_results") == 0
        and _count(counts, "graph_evidence") == 0
    ):
        findings.append(
            _finding(
                "warn",
                "memory",
                "memory_mode_without_evidence",
                "The response used a memory mode without active memory, source, or graph evidence.",
            )
        )
        return _score("memory_relevance", 0.65, "memory_mode_without_evidence")

    return _score("memory_relevance", 1.0, "memory_scope_consistent")


def _score_source_quality(
    context: dict[str, Any],
    response: dict[str, Any],
    findings: list[dict[str, Any]],
) -> dict[str, Any]:
    counts = _dict(context.get("counts"))
    has_evidence = any(
        _count(counts, name) > 0
        for name in [
            "memory_results",
            "source_results",
            "graph_evidence",
            "retrieved",
            "home_node_sources",
            "trusted_web_results",
        ]
    ) or _int(response.get("source_count")) > 0

    if bool(context.get("evidence_required")) and not has_evidence:
        findings.append(
            _finding(
                "fail",
                "source_quality",
                "required_evidence_missing",
                "The response required governed evidence but no supporting source was active.",
            )
        )
        return _score("source_quality", 0.2, "required_evidence_missing")

    if (
        bool(context.get("trusted_web_context_requested"))
        and bool(context.get("trusted_web_context_enabled"))
        and _count(counts, "trusted_web_results") == 0
    ):
        findings.append(
            _finding(
                "warn",
                "source_quality",
                "trusted_web_requested_without_results",
                "Trusted web was requested and enabled but returned no evidence.",
            )
        )
        return _score("source_quality", 0.65, "trusted_web_requested_without_results")

    return _score("source_quality", 1.0, "source_state_consistent")


def _score_synthesis_quality(
    context: dict[str, Any],
    response: dict[str, Any],
    findings: list[dict[str, Any]],
) -> dict[str, Any]:
    answer = _string(response.get("answer"))
    if not answer.strip():
        findings.append(
            _finding("fail", "synthesis", "empty_answer", "The response completed without a user-facing answer.")
        )
        return _score("synthesis_quality", 0.0, "empty_answer")

    if "```" not in answer and ("\\n" in answer or "\\t" in answer):
        findings.append(
            _finding(
                "warn",
                "synthesis",
                "escaped_formatting_visible",
                "The answer contains escaped formatting characters outside a code block.",
            )
        )
        return _score("synthesis_quality", 0.55, "escaped_formatting_visible")

    if _is_everyday_prompt(context) and BACKEND_LANGUAGE_RE.search(answer):
        findings.append(
            _finding(
                "warn",
                "synthesis",
                "backend_language_in_everyday_answer",
                "An everyday response contains internal ops, evidence, or memory wording.",
            )
        )
        return _score("synthesis_quality", 0.5, "backend_language_in_everyday_answer")

    if _is_message_draft(context) and len(answer.strip()) < 20:
        findings.append(
            _finding(
                "warn",
                "synthesis",
                "message_draft_too_thin",
                "The message draft appears too short to be useful or sendable.",
            )
        )
        return _score("synthesis_quality", 0.7, "message_draft_too_thin")

    return _score("synthesis_quality", 1.0, "answer_shape_consistent")


def _score_policy_alignment(
    context: dict[str, Any],
    response: dict[str, Any],
    findings: list[dict[str, Any]],
) -> dict[str, Any]:
    if bool(context.get("mcp_executed")) and _string(context.get("mcp_policy_decision")).lower() in {
        "blocked",
        "approval_required",
    }:
        findings.append(
            _finding(
                "fail",
                "policy",
                "tool_executed_despite_policy_boundary",
                "The trace indicates tool execution despite a blocked or approval-required policy decision.",
            )
        )
        return _score("policy_alignment", 0.0, "tool_executed_despite_policy_boundary")

    if _eq(response.get("execution_state"), "blocked_by_policy") and not _string(response.get("answer")).strip():
        findings.append(
            _finding(
                "warn",
                "policy",
                "policy_block_without_explanation",
                "A policy block should still return a clear user-facing explanation.",
            )
        )
        return _score("policy_alignment", 0.7, "policy_block_without_explanation")

    return _score("policy_alignment", 1.0, "policy_state_consistent")


def _is_everyday_prompt(context: dict[str, Any]) -> bool:
    return any(
        _eq(context.get(key), value)
        for key, value in [
            ("intent_type", "practical_guidance"),
            ("presentation_template_family", "practical_guidance"),
            ("chat_response_profile", "chat_practical_guidance"),
            ("query_classification", "general_reasoning"),
        ]
    )


def _is_message_draft(context: dict[str, Any]) -> bool:
    return any(
        _eq(context.get(key), value)
        for key, value in [
            ("intent_type", "message_draft"),
            ("presentation_template_family", "message_draft"),
            ("chat_response_profile", "chat_message_draft"),
        ]
    )


def _finding(severity: str, category: str, rule: str, detail: str) -> dict[str, Any]:
    return {
        "severity": severity,
        "category": category,
        "rule": rule,
        "detail": detail,
    }


def _score(dimension: str, score: float, reason: str) -> dict[str, Any]:
    return {
        "dimension": dimension,
        "score": score,
        "reason": reason,
    }


def _dict(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


def _count(values: dict[str, Any], key: str) -> int:
    return _int(values.get(key))


def _int(value: Any) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def _string(value: Any) -> str:
    return "" if value is None else str(value)


def _eq(value: Any, expected: str) -> bool:
    return _string(value).strip().lower() == expected.lower()
