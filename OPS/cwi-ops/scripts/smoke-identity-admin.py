#!/usr/bin/env python3
"""Smoke test for identity admin UI."""
import argparse
import sys
import urllib.request


def fetch(url):
    req = urllib.request.Request(url, method="GET")
    with urllib.request.urlopen(req, timeout=10) as resp:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
        return resp.getcode(), resp.read().decode("utf-8")


def main():
    parser = argparse.ArgumentParser(description="Smoke test identity admin UI.")
    parser.add_argument("--base-url", required=True, help="Base URL for admin UI, e.g. http://localhost:8090")
    args = parser.parse_args()

    base = args.base_url.rstrip("/")
    code, body = fetch(base + "/")
    if code != 200:
        print(f"admin UI failed: status={code}")
        return 1
    if "CWI Identity Admin" not in body and "Identity Admin" not in body:
        print("admin UI failed: unexpected response body")
        return 1

    print("identity admin smoke passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
