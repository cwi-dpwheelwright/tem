import os
import uuid
from pathlib import Path

import pytest
import schemathesis

pytestmark = [pytest.mark.contract]

REPO_ROOT = Path(__file__).resolve().parents[3]
SAFE_METHODS = {"GET", "HEAD", "OPTIONS"}


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return {}
    return {"Authorization": f"Bearer {token}"}


def _parse_allowlist(raw):
    if not raw:
        return None
    entries = []
    for item in raw.split(","):
        token = item.strip()
        if not token:
            continue
        parts = token.split(None, 1)
        if len(parts) != 2:
            raise ValueError(f"Invalid allowlist entry: {token}")
        method, path = parts[0].upper(), parts[1].strip()
        entries.append((method, path))
    return set(entries) if entries else None


def _resolve_base_url(env_names):
    for name in env_names:
        value = os.environ.get(name)
        if value:
            return value.rstrip("/")
    return None


def _load_schema(path: Path, base_url: str):
    if hasattr(schemathesis, "from_path"):
        return schemathesis.from_path(str(path), base_url=base_url)
    if hasattr(schemathesis, "openapi") and hasattr(schemathesis.openapi, "from_path"):
        schema = schemathesis.openapi.from_path(str(path))
        try:
            schema.config.update(base_url=base_url)
        except Exception:
            pass
        return schema
    raise AttributeError("schemathesis.from_path is not available")




def _default_value(name, schema):
    if isinstance(schema, dict):
        if "example" in schema:
            return schema["example"]
        if "default" in schema:
            return schema["default"]
        fmt = schema.get("format")
        schema_type = schema.get("type")
    else:
        fmt = None
        schema_type = None

    lowered = name.lower()
    if fmt == "uuid":
        return str(uuid.uuid4())

    if schema_type == "integer":
        return 1
    if schema_type == "number":
        return 1.0
    if schema_type == "boolean":
        return True
    if schema_type == "string":
        if "tenant" in lowered:
            return "tenant-1"
        if "trace" in lowered:
            return "trace-1"
        if "report" in lowered:
            return "report-1"
        if "doc" in lowered:
            return "doc-1"
        if "artifact" in lowered:
            return "artifact-1"
        if "record" in lowered:
            return "record-1"
        return "sample"

    return "sample"


def _required_params(param_set):
    params = {}
    if param_set is None:
        return params
    for param in param_set:
        if not getattr(param, "is_required", False):
            continue
        definition = getattr(param, "definition", {}) or {}
        schema = definition.get("schema") or {}
        params[param.name] = _default_value(param.name, schema)
    return params

SPEC_CONFIGS = [
    (
        "orchestrator",
        REPO_ROOT / "contracts/openapi/cwi-orchestrator.openapi_10H_frozen.yaml",
        ["CWI_ORCHESTRATOR_BASE_URL", "ORCHESTRATOR_BASE_URL", "CWI_BASE_URL"],
    ),
    (
        "router",
        REPO_ROOT / "contracts/openapi/cwi-router.openapi_10H_frozen.yaml",
        ["CWI_ROUTER_BASE_URL", "ROUTER_BASE_URL", "CWI_BASE_URL"],
    ),
    (
        "identity",
        REPO_ROOT / "contracts/openapi/cwi-identity-server.openapi_10H_frozen.yaml",
        ["CWI_IDENTITY_BASE_URL", "IDENTITY_BASE_URL", "IDENTITY_URL"],
    ),
    (
        "coordination",
        REPO_ROOT / "contracts/openapi/cwi-coordination-layer.openapi_10H_frozen.yaml",
        ["CWI_COORDINATION_URL", "COORDINATION_BASE_URL"],
    ),
]


@pytest.mark.parametrize(
    "name,schema_path,env_names",
    SPEC_CONFIGS,
    ids=[config[0] for config in SPEC_CONFIGS],
)
def test_openapi_conformance_safe_ops(name, schema_path: Path, env_names):
    if not schema_path.exists():
        pytest.skip(f"Missing schema {schema_path}")

    base_url = _resolve_base_url(env_names)
    if not base_url:
        pytest.skip(f"{name} base URL not configured ({', '.join(env_names)})")

    allowlist_raw = os.environ.get(f"CWI_CONFORMANCE_ALLOWLIST_{name.upper()}")
    if allowlist_raw is None:
        allowlist_raw = os.environ.get("CWI_CONFORMANCE_ALLOWLIST", "")

    try:
        allowlist = _parse_allowlist(allowlist_raw)
    except ValueError as exc:
        pytest.fail(str(exc))
        return

    safe_only = os.environ.get("CWI_CONFORMANCE_SAFE_ONLY", "true").lower() in ("1", "true", "yes")
    timeout_seconds = float(os.environ.get("CWI_CONFORMANCE_TIMEOUT_SECONDS", "10"))

    schema = _load_schema(schema_path, base_url)
    headers = _auth_headers()
    failures = []
    selected = 0

    for operation in schema.get_all_operations():
        if hasattr(operation, "ok"):
            operation = operation.ok()
        method = operation.method.upper()
        path = operation.path
        if safe_only and method not in SAFE_METHODS:
            continue
        if allowlist is not None and (method, path) not in allowlist:
            continue
        selected += 1
        try:
            path_params = _required_params(getattr(operation, "path_parameters", None))
            query_params = _required_params(getattr(operation, "query", None))
            case = operation.Case(
                headers=headers,
                path_parameters=path_params or None,
                query=query_params or None,
            )
            response = case.call(base_url=base_url, headers=headers, timeout=timeout_seconds)
            operation.validate_response(response)
        except Exception as exc:
            failures.append(f"{method} {path}: {exc}")

    if selected == 0:
        pytest.skip("No operations selected for conformance run; check allowlist/safe-only settings.")

    if failures:
        joined = "\n".join(failures)
        pytest.fail(f"OpenAPI conformance failures for {name}:\n{joined}")
