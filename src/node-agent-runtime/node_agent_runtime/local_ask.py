"""Local ask handler backed by bundle/cache artifact search."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any
import json
import re

from .activation import load_activation_state
from .config import AgentConfig
from .job_runner import JobSpec, SubprocessJobRunner
from .local_model import generate_local_model_answer, should_run_local_model
from .task_runtime import TaskResult

_TEXT_EXTENSIONS = {
    ".cfg",
    ".csv",
    ".html",
    ".json",
    ".log",
    ".md",
    ".prompt",
    ".rst",
    ".text",
    ".toml",
    ".txt",
    ".xml",
    ".yaml",
    ".yml",
}
_TEXT_FILENAMES = {"anchor.txt", "manifest.json", "pack.json"}
_STOP_WORDS = {
    "about",
    "after",
    "again",
    "also",
    "been",
    "does",
    "from",
    "give",
    "have",
    "into",
    "just",
    "like",
    "need",
    "only",
    "over",
    "that",
    "them",
    "then",
    "they",
    "this",
    "what",
    "when",
    "where",
    "which",
    "with",
    "your",
}
_LOW_SIGNAL_GENERIC_TERMS = {
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
}
_TOKEN_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]{2,}")
_MAX_RESULTS = 3
_MAX_FILES = 256
_MAX_FILE_BYTES = 2 * 1024 * 1024
_MAX_TOTAL_BYTES = 8 * 1024 * 1024
_SNIPPET_CHARS = 220


@dataclass(frozen=True)
class LocalArtifactMatch:
    source_kind: str
    title: str
    path: str
    score: int
    excerpt: str
    size_bytes: int


@dataclass(frozen=True)
class LocalSearchResult:
    matches: tuple[LocalArtifactMatch, ...]
    terms: tuple[str, ...]
    files_scanned: int
    bytes_scanned: int


@dataclass(frozen=True)
class LocalPackDescriptor:
    bundle_id: str | None
    pack_id: str | None
    pack_version: str | None
    pack_name: str | None
    anchor: str | None
    root_path: str | None


@dataclass(frozen=True)
class SearchRoot:
    source_kind: str
    root: Path
    pack_id: str | None = None
    pack_version: str | None = None
    pack_name: str | None = None
    module_roles: tuple[str, ...] = ()
    force_match_queries: tuple[str, ...] = ()
    load_order: int = 100


def describe_local_capabilities(config: AgentConfig) -> list[str]:
    capabilities = ["local_ask"]
    if _resolve_search_roots(config):
        capabilities.append("local_retrieval")
    if config.local_model.enabled:
        capabilities.append("local_model")
        capabilities.append("local_model_ollama")
    return capabilities


class LocalAskHandler:
    def __init__(self, config: AgentConfig) -> None:
        self._config = config
        self._runner = SubprocessJobRunner(config.job_runner)

    def run(self, payload: dict[str, Any]) -> TaskResult:
        job_payload = payload.get("job")
        if isinstance(job_payload, dict):
            command = job_payload.get("command")
            if isinstance(command, list) and all(isinstance(item, str) for item in command):
                return self._run_job(payload, command)

        question = _read_string(payload, "question") or "request"
        normalized_question = _normalize_question(question)
        search_roots = _resolve_search_roots(self._config)
        query_classification = _classify_query(normalized_question, search_roots)
        pack = payload.get("pack") if isinstance(payload.get("pack"), dict) else {}
        search = _search_local_artifacts(self._config, question)
        descriptor = _load_pack_descriptor(self._config)
        baseline_pack_ids = _baseline_pack_ids(self._config)
        baseline_source_count = _baseline_source_count(search.matches)
        local_model_result = None
        local_model_requested = should_run_local_model(self._config, payload)
        local_reasoning_fallback = (
            not local_model_requested
            and self._config.local_model.enabled
            and len(search.matches) == 0
        )
        local_model_error = None
        if local_model_requested or local_reasoning_fallback:
            try:
                local_model_result = generate_local_model_answer(
                    self._config,
                    question=question,
                    matches=search.matches,
                    payload=payload,
                )
            except Exception as exc:
                if local_reasoning_fallback:
                    local_model_error = str(exc)
                else:
                    return TaskResult(
                        status="failed",
                        details={
                            "message": f"Local model inference failed: {exc}",
                            "trace": {
                                "node_id": self._config.identity.node_id,
                                "policy_mode": "local",
                                "execution_mode": "local_model_ollama",
                                "local_policy_outcome": _resolve_local_policy_outcome(payload),
                                "route_reason": _execution_string(payload, "route_reason"),
                                "grounded": False,
                                "reasoning_based": False,
                            },
                        },
                    )
        if local_model_result is None:
            answer = _build_answer(
                question,
                search.matches,
                descriptor=descriptor,
                node_id=self._config.identity.node_id,
                query_classification=query_classification,
            )
        else:
            answer = local_model_result.answer
        grounded = len(search.matches) > 0
        reasoning_based = local_model_result is not None and not grounded
        return TaskResult(
            status="completed",
            details={
                "answer": answer,
                "sources": [
                    {
                        "source_kind": match.source_kind,
                        "title": match.title,
                        "path": match.path,
                        "score": match.score,
                        "excerpt": match.excerpt,
                        "size_bytes": match.size_bytes,
                    }
                    for match in search.matches
                ],
                "trace": {
                    "node_id": self._config.identity.node_id,
                    "policy_mode": "local",
                    "execution_mode": "local_artifact_search" if local_model_result is None else "local_model_ollama",
                    "local_policy_outcome": _resolve_local_policy_outcome(payload),
                    "local_model": local_model_result.model if local_model_result is not None else None,
                    "local_model_error": local_model_error,
                    "grounded": grounded,
                    "reasoning_based": reasoning_based,
                    "route_reason": _execution_string(payload, "route_reason"),
                    "search_terms": list(search.terms),
                    "files_scanned": search.files_scanned,
                    "bytes_scanned": search.bytes_scanned,
                    "bundle_id": descriptor.bundle_id,
                    "pack_id": descriptor.pack_id,
                    "pack_name": descriptor.pack_name,
                    "pack_hash": _read_string(pack, "hash"),
                    "pack_version": _read_string(pack, "version") or descriptor.pack_version,
                    "pack_anchor": _read_string(pack, "requested_anchor") or descriptor.anchor,
                    "source_count": len(search.matches),
                    "baseline_pack_ids": list(baseline_pack_ids),
                    "baseline_pack_count": len(baseline_pack_ids),
                    "baseline_source_count": baseline_source_count,
                    "query_classification": query_classification,
                },
            },
        )

    def _run_job(self, payload: dict[str, Any], command: list[str]) -> TaskResult:
        job_result = self._runner.run(JobSpec(command=command))
        if job_result.status == "ok":
            answer = job_result.stdout.strip() or "Local job completed."
            return TaskResult(
                status="completed",
                details={
                    "answer": answer,
                    "trace": {
                        "node_id": self._config.identity.node_id,
                        "execution_mode": "job_runner",
                        "local_policy_outcome": _resolve_local_policy_outcome(payload),
                        "exit_code": job_result.exit_code,
                        "duration_seconds": job_result.duration_seconds,
                    },
                },
            )
        return TaskResult(
            status="failed",
            details={
                "message": job_result.stderr or "Local job failed.",
                "exit_code": job_result.exit_code,
                "stdout": job_result.stdout,
                "stderr": job_result.stderr,
                "duration_seconds": job_result.duration_seconds,
            },
        )


def _search_local_artifacts(config: AgentConfig, question: str) -> LocalSearchResult:
    terms = _extract_terms(question)
    full_question = _normalize_question(question)
    search_roots = _resolve_search_roots(config)
    query_classification = _classify_query(full_question, search_roots)
    if query_classification is None:
        terms = tuple(term for term in terms if term not in _LOW_SIGNAL_GENERIC_TERMS)
    matches_by_key: dict[tuple[str, str], LocalArtifactMatch] = {}
    files_scanned = 0
    bytes_scanned = 0

    for search_root in search_roots:
        root = search_root.root
        for path in sorted(root.rglob("*")):
            if files_scanned >= _MAX_FILES or bytes_scanned >= _MAX_TOTAL_BYTES:
                break
            if not path.is_file() or not _is_supported_text_file(path):
                continue
            size_bytes = path.stat().st_size
            if size_bytes <= 0 or size_bytes > _MAX_FILE_BYTES:
                continue

            text = _read_text(path)
            if not text:
                continue

            files_scanned += 1
            bytes_scanned += size_bytes
            match = _score_match(
                path,
                search_root,
                text,
                terms,
                full_question,
                size_bytes,
                query_classification=query_classification,
            )
            if match is not None:
                dedupe_key = _match_dedupe_key(search_root, path)
                existing = matches_by_key.get(dedupe_key)
                if existing is None or match.score > existing.score or (
                    match.score == existing.score and match.path.lower() < existing.path.lower()
                ):
                    matches_by_key[dedupe_key] = match

    matches = list(matches_by_key.values())
    matches.sort(key=lambda item: (-item.score, _source_kind_priority(item.source_kind), item.path.lower()))
    return LocalSearchResult(
        matches=tuple(matches[:_MAX_RESULTS]),
        terms=terms,
        files_scanned=files_scanned,
        bytes_scanned=bytes_scanned,
    )


def _resolve_search_roots(config: AgentConfig) -> list[SearchRoot]:
    roots: list[SearchRoot] = []
    seen: set[Path] = set()

    candidates: list[tuple[str, Path | None, int]] = [
        ("baseline", None, 1),
        ("bundle", config.bundle.unseal_dir, 10),
        ("runtime", config.update.current_dir, 20),
        ("cache", config.cache.cache_dir / "assets" if config.cache.cache_dir else None, 30),
    ]
    baseline_roots = list(config.baseline_packs.roots)
    for source_kind, root, load_order in candidates:
        roots_to_add: list[Path] = []
        if source_kind == "baseline":
            for baseline_root in baseline_roots:
                roots_to_add.extend(_resolve_baseline_roots(baseline_root))
        elif root is not None:
            roots_to_add.append(root)

        for candidate_root in roots_to_add:
            if candidate_root is None or not candidate_root.exists():
                continue
            resolved = candidate_root.resolve()
            if resolved in seen:
                continue
            seen.add(resolved)
            roots.append(_build_search_root(source_kind, resolved, load_order))

    roots.sort(key=lambda item: item.load_order)
    return roots


def _resolve_baseline_roots(root: Path) -> list[Path]:
    if not root.exists():
        return []

    latest_root = root / "latest"
    if (latest_root / "pack.json").exists():
        return [latest_root]

    if (root / "pack.json").exists():
        return [root]

    versioned_roots = [child for child in root.iterdir() if child.is_dir() and (child / "pack.json").exists()]
    if not versioned_roots:
        return [root]

    versioned_roots.sort(key=lambda child: (_version_sort_key(child.name), child.name), reverse=True)
    return [versioned_roots[0]]


def _version_sort_key(value: str) -> tuple[int, ...]:
    parts: list[int] = []
    for part in re.split(r"[^0-9]+", value):
        if not part:
            continue
        try:
            parts.append(int(part))
        except ValueError:
            continue
    return tuple(parts)


def _build_search_root(source_kind: str, root: Path, load_order: int) -> SearchRoot:
    payload = _load_json(root / "pack.json")
    pack_id = _mapping_string(payload, "pack_id") if payload is not None else None
    pack_version = (
        _mapping_string(payload, "pack_version") or _mapping_string(payload, "version")
        if payload is not None
        else None
    )
    pack_name = _mapping_string(payload, "name") if payload is not None else None
    return SearchRoot(
        source_kind=source_kind,
        root=root,
        pack_id=pack_id,
        pack_version=pack_version,
        pack_name=pack_name,
        module_roles=_load_module_roles(payload),
        force_match_queries=_load_force_match_queries(payload),
        load_order=load_order,
    )


def _load_module_roles(payload: dict[str, Any] | None) -> tuple[str, ...]:
    if payload is None:
        return ()
    modules = payload.get("modules")
    if not isinstance(modules, list):
        return ()
    roles: list[str] = []
    seen: set[str] = set()
    for module in modules:
        if not isinstance(module, dict):
            continue
        role = _mapping_string(module, "role")
        if role is None:
            continue
        normalized = role.strip().lower()
        if normalized in seen:
            continue
        seen.add(normalized)
        roles.append(normalized)
    return tuple(roles)


def _load_force_match_queries(payload: dict[str, Any] | None) -> tuple[str, ...]:
    if payload is None:
        return ()
    retrieval = payload.get("retrieval")
    if not isinstance(retrieval, dict):
        return ()
    raw = retrieval.get("force_match_queries")
    if not isinstance(raw, list):
        return ()
    queries: list[str] = []
    seen: set[str] = set()
    for item in raw:
        if not isinstance(item, str):
            continue
        normalized = " ".join(item.strip().lower().split())
        if not normalized or normalized in seen:
            continue
        seen.add(normalized)
        queries.append(normalized)
    return tuple(queries)


def _classify_query(question: str, search_roots: list[SearchRoot]) -> str | None:
    normalized = " ".join(question.split())
    if not normalized:
        return None

    for search_root in search_roots:
        if search_root.source_kind != "baseline":
            continue
        if any(query and query in normalized for query in search_root.force_match_queries):
            if _is_identity_pack(search_root):
                return "identity_definition"
            return "system_truth"

    if any(fragment in normalized for fragment in ("home node", "home-node")):
        return "home_node_definition"
    if any(fragment in normalized for fragment in ("what is cwi", "what does cwi stand for", "define cwi")):
        return "identity_definition"
    if "cwi" in normalized:
        return "system_truth"
    return None


def _is_identity_pack(search_root: SearchRoot) -> bool:
    if search_root.pack_id and search_root.pack_id.strip().lower() == "cwi-identity":
        return True
    return "system_truth" in search_root.module_roles


def _is_communication_pack(search_root: SearchRoot) -> bool:
    if search_root.pack_id and search_root.pack_id.strip().lower() == "cwi-communication-core":
        return True
    return "global_communication_layer" in search_root.module_roles


def _source_kind_priority(source_kind: str) -> int:
    priorities = {
        "baseline": 0,
        "bundle": 1,
        "runtime": 2,
        "cache": 3,
    }
    return priorities.get(source_kind, 9)


def _governed_match_bonus(search_root: SearchRoot, *, query_classification: str | None) -> int:
    if search_root.source_kind != "baseline":
        return 0
    if query_classification in {"identity_definition", "system_truth", "home_node_definition"}:
        if _is_identity_pack(search_root):
            return 40
        if _is_communication_pack(search_root):
            return 6
        return 2
    return 1


def _baseline_pack_ids(config: AgentConfig) -> tuple[str, ...]:
    pack_ids: list[str] = []
    seen: set[str] = set()
    for search_root in _resolve_search_roots(config):
        if search_root.source_kind != "baseline" or not search_root.pack_id:
            continue
        normalized = search_root.pack_id.strip()
        if not normalized or normalized in seen:
            continue
        seen.add(normalized)
        pack_ids.append(normalized)
    return tuple(pack_ids)


def _baseline_source_count(matches: tuple[LocalArtifactMatch, ...]) -> int:
    return sum(1 for match in matches if match.source_kind == "baseline")


def _match_dedupe_key(search_root: SearchRoot, path: Path) -> tuple[str, str]:
    if search_root.source_kind == "baseline":
        return (search_root.source_kind, search_root.pack_id or search_root.root.as_posix())
    return (search_root.source_kind, str(path))


def _resolve_descriptor_roots(config: AgentConfig) -> list[SearchRoot]:
    return [search_root for search_root in _resolve_search_roots(config) if search_root.source_kind != "baseline"]


def _normalize_question(question: str) -> str:
    return " ".join(question.strip().lower().split())


def _extract_terms(question: str) -> tuple[str, ...]:
    tokens: list[str] = []
    seen: set[str] = set()
    for match in _TOKEN_RE.finditer(question.lower()):
        token = match.group(0)
        if token in _STOP_WORDS or token in seen:
            continue
        seen.add(token)
        tokens.append(token)
    if not tokens:
        normalized = question.strip().lower()
        if normalized:
            tokens.append(normalized)
    return tuple(tokens)


def _is_supported_text_file(path: Path) -> bool:
    suffix = path.suffix.lower()
    return suffix in _TEXT_EXTENSIONS or path.name.lower() in _TEXT_FILENAMES


def _read_text(path: Path) -> str:
    raw = path.read_text(encoding="utf-8", errors="ignore")
    if path.suffix.lower() != ".json":
        return raw
    try:
        parsed = json.loads(raw)
    except ValueError:
        return raw
    return json.dumps(parsed, indent=2, sort_keys=True)


def _score_match(
    path: Path,
    search_root: SearchRoot,
    text: str,
    terms: tuple[str, ...],
    full_question: str,
    size_bytes: int,
    *,
    query_classification: str | None,
) -> LocalArtifactMatch | None:
    text_lower = text.lower()
    filename_lower = path.name.lower()
    score = 0
    anchor = -1
    for term in terms:
        count = text_lower.count(term)
        if count:
            score += count * 3
            if anchor < 0:
                anchor = text_lower.find(term)
        if term in filename_lower:
            score += 2
    if full_question and full_question in text_lower:
        score += 5
        if anchor < 0:
            anchor = text_lower.find(full_question)
    if score <= 0:
        return None
    score += _governed_match_bonus(search_root, query_classification=query_classification)
    if search_root.source_kind == "baseline" and path.name.lower() == "pack.json":
        score = max(1, score - 20)
    if anchor < 0:
        anchor = 0

    relative = path.relative_to(search_root.root).as_posix()
    if search_root.source_kind == "cache":
        title = f"cache/{relative}"
    elif search_root.source_kind == "baseline":
        pack_label = search_root.pack_id or search_root.root.name
        title = f"baseline/{pack_label}/{relative}"
    else:
        title = relative
    return LocalArtifactMatch(
        source_kind=search_root.source_kind,
        title=title,
        path=str(path),
        score=score,
        excerpt=_excerpt(text, anchor),
        size_bytes=size_bytes,
    )


def _excerpt(text: str, anchor: int) -> str:
    compact = " ".join(text.split())
    if not compact:
        return ""
    anchor = min(max(anchor, 0), max(len(compact) - 1, 0))
    start = max(anchor - (_SNIPPET_CHARS // 2), 0)
    end = min(start + _SNIPPET_CHARS, len(compact))
    snippet = compact[start:end].strip()
    if start > 0:
        snippet = f"...{snippet}"
    if end < len(compact):
        snippet = f"{snippet}..."
    return snippet


def _load_pack_descriptor(config: AgentConfig) -> LocalPackDescriptor:
    activation_state = load_activation_state(config.storage.state_dir)
    bundle_id = activation_state.bundle_id if activation_state is not None else None

    for search_root in _resolve_descriptor_roots(config):
        root = search_root.root
        pack_path = root / "pack.json"
        if pack_path.exists():
            payload = _load_json(pack_path)
            if payload is not None:
                return LocalPackDescriptor(
                    bundle_id=bundle_id,
                    pack_id=_mapping_string(payload, "pack_id"),
                    pack_version=_mapping_string(payload, "pack_version"),
                    pack_name=_mapping_string(payload, "name"),
                    anchor=_load_anchor(root),
                    root_path=str(root),
                )

    return LocalPackDescriptor(
        bundle_id=bundle_id,
        pack_id=None,
        pack_version=None,
        pack_name=None,
        anchor=None,
        root_path=None,
    )


def _load_json(path: Path) -> dict[str, Any] | None:
    if not path.exists():
        return None
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except ValueError:
        return None
    return payload if isinstance(payload, dict) else None


def _load_anchor(root: Path) -> str | None:
    anchor_path = root / "anchor.txt"
    if not anchor_path.exists():
        return None
    value = anchor_path.read_text(encoding="utf-8", errors="ignore").strip()
    return value or None


def _mapping_string(mapping: dict[str, Any], key: str) -> str | None:
    value = mapping.get(key)
    return value if isinstance(value, str) and value.strip() else None


def _build_answer(
    question: str,
    matches: tuple[LocalArtifactMatch, ...],
    *,
    descriptor: LocalPackDescriptor,
    node_id: str,
    query_classification: str | None = None,
) -> str:
    if not matches:
        return f'I do not have matching local evidence for "{question}".'

    if query_classification == "identity_definition":
        preferred = _select_identity_match(matches)
        if preferred is not None:
            sentence = _first_sentence(preferred.excerpt)
            if sentence:
                return sentence

    parts = [f"{match.title}: {match.excerpt}" for match in matches[:2]]
    return f'I found local evidence for "{question}": ' + " | ".join(parts)


def _select_identity_match(matches: tuple[LocalArtifactMatch, ...]) -> LocalArtifactMatch | None:
    for match in matches:
        if "stands for" in match.excerpt.lower():
            return match
    for match in matches:
        if "/content/" in match.title or "/content/" in match.path:
            if "/cwi-identity/" in match.title or "/cwi-identity/" in match.path:
                return match
    for match in matches:
        if "/cwi-identity/" in match.title or "/cwi-identity/" in match.path:
            return match
    return None


def _first_sentence(text: str) -> str | None:
    compact = " ".join(text.split())
    if not compact:
        return None
    sentence, _, _ = compact.partition(".")
    sentence = sentence.strip()
    if not sentence:
        return None
    return sentence + "."


def _resolve_local_policy_outcome(payload: dict[str, Any]) -> str:
    classification = _read_string(payload, "data_classification")
    if classification == "LOCAL_ONLY":
        return "ALLOW_LOCAL_ONLY_NO_EGRESS"
    if classification == "REMOTE_ALLOWED":
        return "ALLOW_LOCAL_PLUS_REMOTE_ASSIST"
    policy = payload.get("policy")
    if isinstance(policy, dict):
        if policy.get("local_only") is True:
            return "ALLOW_LOCAL_ONLY_NO_EGRESS"
        rules = policy.get("rules")
        if isinstance(rules, dict):
            if rules.get("local_only") is True or rules.get("local_execution_required") is True:
                return "ALLOW_LOCAL_ONLY_NO_EGRESS"
            if rules.get("remote_fallback_allowed") is True:
                return "ALLOW_LOCAL_PLUS_REMOTE_ASSIST"
    return "ALLOW_LOCAL_EXECUTION"


def _execution_string(payload: dict[str, Any], key: str) -> str | None:
    execution = payload.get("execution")
    if not isinstance(execution, dict):
        return None
    return _read_string(execution, key)


def _read_string(mapping: dict[str, Any], key: str) -> str | None:
    value = mapping.get(key)
    return value if isinstance(value, str) and value.strip() else None
