import argparse
import json
import os
import subprocess
import sys
import tempfile
from defusedxml import ElementTree as ET

import requests


def main() -> int:
    parser = argparse.ArgumentParser(description="CWI gold tests runner")
    parser.add_argument(
        "--suite",
        default="smoke",
        choices=["smoke", "negative", "e2e", "integration", "simulation", "performance", "contract"],
    )
    parser.add_argument("--base-url", default=os.environ.get("CWI_BASE_URL", ""))
    parser.add_argument("--junit-xml", default=os.environ.get("CWI_JUNIT_XML", ""))
    parser.add_argument("--json-summary", default=os.environ.get("CWI_JSON_SUMMARY", ""))
    args = parser.parse_args()

    env = os.environ.copy()
    if args.suite != "contract":
        if not args.base_url:
            print("CWI_BASE_URL is required.")
            return 2
        env["CWI_BASE_URL"] = args.base_url
        try:
            _ensure_auth_token(env)
        except RuntimeError as exc:
            print(str(exc))
            return 2

    junit_path = args.junit_xml
    temp_junit = None
    if args.json_summary and not junit_path:
        temp_junit = tempfile.NamedTemporaryFile(prefix="cwi-gold-tests-", suffix=".xml", delete=False)
        junit_path = temp_junit.name
        temp_junit.close()

    tests_root = os.path.join(os.path.dirname(__file__), "tests")

    if args.suite == "performance":
        cmd = [sys.executable, os.path.join(os.path.dirname(__file__), "run_perf.py")]
        if args.base_url:
            cmd.extend(["--base-url", args.base_url])
        if junit_path:
            cmd.extend(["--junit-xml", junit_path])
        if args.json_summary:
            cmd.extend(["--json-summary", args.json_summary])
        exit_code = subprocess.call(cmd, env=env, cwd=os.path.dirname(__file__))
    else:
        cmd = [sys.executable, "-m", "pytest", "-m", args.suite, tests_root]
        if junit_path:
            cmd.extend(["--junitxml", junit_path])

        exit_code = subprocess.call(cmd, env=env, cwd=os.path.dirname(__file__))

    if args.json_summary:
        summary = _build_summary(junit_path, exit_code)
        _write_summary(args.json_summary, summary)

    return exit_code


def _build_summary(junit_path, exit_code):
    summary = {
        "suite": None,
        "tests": 0,
        "failures": 0,
        "errors": 0,
        "skipped": 0,
        "time_seconds": 0.0,
        "exit_code": exit_code,
    }

    if not junit_path or not os.path.exists(junit_path):
        return summary

    tree = ET.parse(junit_path)
    root = tree.getroot()
    if root.tag == "testsuites":
        suites = root.findall("testsuite")
        if suites:
            for suite in suites:
                summary["tests"] += int(suite.attrib.get("tests", "0"))
                summary["failures"] += int(suite.attrib.get("failures", "0"))
                summary["errors"] += int(suite.attrib.get("errors", "0"))
                summary["skipped"] += int(suite.attrib.get("skipped", "0"))
                summary["time_seconds"] += float(suite.attrib.get("time", "0") or 0.0)
            return summary
        summary["tests"] = int(root.attrib.get("tests", "0"))
        summary["failures"] = int(root.attrib.get("failures", "0"))
        summary["errors"] = int(root.attrib.get("errors", "0"))
        summary["skipped"] = int(root.attrib.get("skipped", "0"))
        summary["time_seconds"] = float(root.attrib.get("time", "0") or 0.0)
        return summary

    if root.tag == "testsuite":
        summary["tests"] = int(root.attrib.get("tests", "0"))
        summary["failures"] = int(root.attrib.get("failures", "0"))
        summary["errors"] = int(root.attrib.get("errors", "0"))
        summary["skipped"] = int(root.attrib.get("skipped", "0"))
        summary["time_seconds"] = float(root.attrib.get("time", "0") or 0.0)
        return summary

    return summary


def _write_summary(path, summary):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2, sort_keys=True)


def _ensure_auth_token(env):
    if env.get("CWI_AUTH_TOKEN"):
        return

    auth_raw = env.get("AUTH_REQUIRED")
    auth_required = True if auth_raw is None or not str(auth_raw).strip() else str(auth_raw).lower() in ("1", "true", "yes")
    if not auth_required:
        return

    tenant_id = env.get("IDENTITY_BOOTSTRAP_TENANT_ID") or env.get("CWI_TENANT_ID") or "tenant-1"
    email = env.get("IDENTITY_BOOTSTRAP_ADMIN_EMAIL")
    username = env.get("IDENTITY_BOOTSTRAP_ADMIN_USERNAME")
    password = env.get("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD")
    if not password:
        raise RuntimeError("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD is required to fetch CWI_AUTH_TOKEN.")

    payload = {"tenant_id": tenant_id, "password": password}
    if email:
        payload["email"] = email
    elif username:
        payload["username"] = username
    else:
        raise RuntimeError("IDENTITY_BOOTSTRAP_ADMIN_EMAIL or IDENTITY_BOOTSTRAP_ADMIN_USERNAME is required.")

    errors = []
    for identity_base in _candidate_identity_urls(env):
        url = f"{identity_base.rstrip('/')}/auth/login"
        try:
            resp = requests.post(url, json=payload, timeout=10)
        except requests.RequestException as exc:
            errors.append(f"{url} -> {type(exc).__name__}")
            continue

        if resp.status_code != 200:
            snippet = resp.text[:200].replace("\n", " ")
            errors.append(f"{url} -> {resp.status_code} {snippet}")
            continue

        token = (resp.json() or {}).get("access_token")
        if not token:
            errors.append(f"{url} -> missing access_token")
            continue

        env["CWI_AUTH_TOKEN"] = token
        return

    error_text = "; ".join(errors) if errors else "no identity endpoints attempted"
    raise RuntimeError(f"Failed to fetch CWI_AUTH_TOKEN. Attempts: {error_text}")


def _candidate_identity_urls(env):
    urls = []
    port = env.get("IDENTITY_HTTP_PORT")
    if port:
        urls.append(f"http://localhost:{port}")

    for name in ("CWI_IDENTITY_BASE_URL", "IDENTITY_BASE_URL", "IDENTITY_URL"):
        value = env.get(name)
        if value:
            urls.append(value)

    base_url = env.get("CWI_BASE_URL")
    if base_url:
        urls.append(base_url)

    seen = set()
    ordered = []
    for url in urls:
        normalized = url.rstrip("/")
        if normalized and normalized not in seen:
            ordered.append(normalized)
            seen.add(normalized)
    return ordered


if __name__ == "__main__":
    raise SystemExit(main())
