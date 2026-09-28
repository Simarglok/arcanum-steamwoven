#!/usr/bin/env python3
"""Run a local validation command with an explicit wall-clock deadline."""
from __future__ import annotations

import subprocess
import sys


def main() -> int:
    if len(sys.argv) < 3:
        raise SystemExit("usage: run_with_timeout.py SECONDS COMMAND [ARG ...]")
    try:
        seconds = float(sys.argv[1])
    except ValueError as error:
        raise SystemExit(f"invalid timeout: {sys.argv[1]!r}") from error
    if seconds <= 0:
        raise SystemExit("timeout must be positive")
    try:
        return subprocess.run(sys.argv[2:], timeout=seconds).returncode
    except subprocess.TimeoutExpired:
        print(f"validation command exceeded {seconds:g}s timeout", file=sys.stderr)
        return 124


if __name__ == "__main__":
    raise SystemExit(main())
