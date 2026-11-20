import argparse
import json
import sys
from pathlib import Path


def load_matrix(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        raise SystemExit(f"Failed to read env matrix: {exc}")


def parse_env(path: Path) -> dict:
    env = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            raise SystemExit(f"Invalid line in {path}: {line}")
        key, value = line.split("=", 1)
        env[key.strip()] = value.strip()
    return env


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate .env files against role requirements.")
    parser.add_argument("--matrix", default="OPS/cwi-ops/env/ENV_VAR_MATRIX.json")
    parser.add_argument("--role", required=True)
    parser.add_argument("--env-file", required=True)
    parser.add_argument("--allow-extra", action="store_true")
    args = parser.parse_args()

    matrix = load_matrix(Path(args.matrix))
    roles = matrix.get("roles", {})
    if args.role not in roles:
        raise SystemExit(f"Unknown role: {args.role}")

    required = set(roles[args.role].get("required", []))
    optional = set(roles[args.role].get("optional", []))
    env = parse_env(Path(args.env_file))
    present = set(env.keys())

    missing = sorted(required - present)
    allowed = required | optional
    extra = sorted(present - allowed)

    if missing:
        print("Missing required vars:")
        for name in missing:
            print(f"  - {name}")

    if extra and not args.allow_extra:
        print("Unexpected vars (not in required/optional; use --allow-extra to ignore):")
        for name in extra:
            print(f"  - {name}")

    if missing or (extra and not args.allow_extra):
        return 1

    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
