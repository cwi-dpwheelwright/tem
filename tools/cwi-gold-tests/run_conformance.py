import argparse
import os
import sys
import schemathesis


SAFE_METHODS = {"GET", "HEAD", "OPTIONS"}


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


def main() -> int:
    parser = argparse.ArgumentParser(description="CWI OpenAPI conformance runner")
    parser.add_argument("--schema", default=os.environ.get("CWI_CONFORMANCE_SCHEMA", ""))
    parser.add_argument("--base-url", default=os.environ.get("CWI_CONFORMANCE_BASE_URL", ""))
    parser.add_argument(
        "--allowlist",
        default=os.environ.get("CWI_CONFORMANCE_ALLOWLIST", ""),
        help="Comma-separated METHOD path entries, e.g. 'GET /api/health,GET /api/ready'",
    )
    parser.add_argument(
        "--safe-only",
        action="store_true",
        default=os.environ.get("CWI_CONFORMANCE_SAFE_ONLY", "true").lower() in ("1", "true", "yes"),
    )
    parser.add_argument(
        "--timeout-seconds",
        type=float,
        default=float(os.environ.get("CWI_CONFORMANCE_TIMEOUT_SECONDS", "10")),
    )
    args = parser.parse_args()

    if not args.schema:
        print("CWI_CONFORMANCE_SCHEMA is required.")
        return 2
    if not args.base_url:
        print("CWI_CONFORMANCE_BASE_URL is required.")
        return 2

    try:
        allowlist = _parse_allowlist(args.allowlist)
    except ValueError as exc:
        print(str(exc))
        return 2

    if hasattr(schemathesis, "from_path"):
        schema = schemathesis.from_path(args.schema, base_url=args.base_url)
    elif hasattr(schemathesis, "openapi") and hasattr(schemathesis.openapi, "from_path"):
        schema = schemathesis.openapi.from_path(args.schema)
        try:
            schema.config.update(base_url=args.base_url)
        except Exception:
            pass
    else:
        print("schemathesis.from_path not available")
        return 2
    failures = []

    for operation in schema.get_all_operations():
        if hasattr(operation, "ok"):
            operation = operation.ok()
        method = operation.method.upper()
        path = operation.path
        if args.safe_only and method not in SAFE_METHODS:
            continue
        if allowlist is not None and (method, path) not in allowlist:
            continue

        try:
            case = operation.Case()
            response = case.call(base_url=args.base_url, timeout=args.timeout_seconds)
            operation.validate_response(response)
            print(f"OK {method} {path} {response.status_code}")
        except Exception as exc:
            failures.append((method, path, str(exc)))
            print(f"FAIL {method} {path}: {exc}")

    if failures:
        print(f"{len(failures)} operation(s) failed.")
        return 1

    print("Conformance checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
