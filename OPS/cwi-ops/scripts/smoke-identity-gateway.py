#!/usr/bin/env python3
"""Smoke test for identity routing through the gateway."""
import argparse
import json
import sys
import urllib.request


def request_json(url, method="GET", body=None):
    data = None
    headers = {"Content-Type": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
            return resp.getcode(), resp.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8")


def main():
    parser = argparse.ArgumentParser(description="Smoke test identity routes via gateway.")
    parser.add_argument("--base-url", required=True, help="Gateway base URL, e.g. http://localhost:18081")
    args = parser.parse_args()

    base = args.base_url.rstrip("/")

    status, body = request_json(f"{base}/jwks")
    if status != 200 or "keys" not in body:
        print(f"jwks check failed: status={status} body={body}")
        return 1

    status, body = request_json(
        f"{base}/auth/password/reset",
        method="POST",
        body={"tenant_id": "smoke", "email": "smoke@example.com"},
    )
    if status != 200:
        print(f"password reset check failed: status={status} body={body}")
        return 1

    print("identity gateway smoke passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
