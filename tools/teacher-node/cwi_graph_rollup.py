#!/usr/bin/env python
from __future__ import annotations

import sys

from graph_cli import main


if __name__ == "__main__":
    raise SystemExit(main(["rollup", *sys.argv[1:]]))
