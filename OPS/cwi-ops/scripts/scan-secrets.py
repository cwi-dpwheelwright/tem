import argparse
import re
import sys
from pathlib import Path

SAFE_PLACEHOLDERS = {
    "",
    "changeme",
    "redacted",
    "placeholder",
    "example",
    "your_value",
    "your-token",
    "your_secret",
    "your-secret",
    "true",
    "false",
}

SUSPICIOUS_KEYS = re.compile(r"(secret|token|password|apikey|api_key|key)", re.IGNORECASE)


def is_safe_value(value: str) -> bool:
    lowered = value.strip().lower()
    if lowered in SAFE_PLACEHOLDERS:
        return True
    if lowered.startswith("change_me"):
        return True
    if lowered.isdigit():
        return True
    if lowered.startswith("<") and lowered.endswith(">"):
        return True
    if lowered.startswith("${") and lowered.endswith("}"):
        return True
    return False


def scan_file(path: Path) -> list:
    findings = []
    for idx, raw_line in enumerate(path.read_text(encoding="utf-8", errors="ignore").splitlines(), 1):
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        normalized_key = key.strip().upper()
        if (
            normalized_key.endswith("_DIR")
            or normalized_key.endswith("_PATH")
            or normalized_key.endswith("_URL")
            or normalized_key.endswith("_ISSUER")
            or normalized_key.endswith("_AUDIENCE")
        ):
            continue
        if SUSPICIOUS_KEYS.search(key) and not is_safe_value(value):
            findings.append((path, idx, key.strip()))
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description="Scan .env.template files for secrets.")
    parser.add_argument("--path", required=True, help="File or directory to scan")
    args = parser.parse_args()

    target = Path(args.path)
    files = []
    if target.is_dir():
        files = list(target.rglob("*.env.template"))
    else:
        files = [target]

    all_findings = []
    for path in files:
        all_findings.extend(scan_file(path))

    if all_findings:
        print("Potential secrets detected:")
        for path, idx, key in all_findings:
            print(f"  - {path}:{idx}: {key}")
        return 1

    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
