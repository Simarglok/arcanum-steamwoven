"""ARC-DEV-2225: run every tools/local_dev unit test, one module per process, a few modules at a time.

The root check "all launcher tests" (docs/workflow/toolchain.md#all-launcher-tests-in-one-command-arc-dev-1565-arc-dev-2225): one serial
`unittest discover` took about 250 s, the modules are independent (each builds its own temp folders), so a small pool runs them in
about a third of that. Quiet: a passing module prints one line, a failing one its whole output; exit 0 only when every module passed.

    python3 -B tools/local_dev/run_tests.py [--jobs N] [MODULE ...]    (MODULE: test_run_queue or tools.local_dev.test_run_queue)
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
DEFAULT_JOBS = 4


def modules() -> list[str]:
    return sorted(f"tools.local_dev.{path.stem}" for path in HERE.glob("test_*.py"))


def run_module(name: str) -> tuple[str, int, float, str]:
    started = time.monotonic()
    done = subprocess.run([sys.executable, "-B", "-m", "unittest", name], cwd=ROOT, capture_output=True, text=True,
                          env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
    return name, done.returncode, time.monotonic() - started, done.stdout + done.stderr


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--jobs", type=int, default=min(DEFAULT_JOBS, os.cpu_count() or 1))
    parser.add_argument("names", nargs="*", help="test modules (default: every tools/local_dev/test_*.py)")
    args = parser.parse_args(argv)
    names = [Path(n).stem if n.endswith(".py") else n for n in args.names]
    names = [n if n.startswith("tools.") else f"tools.local_dev.{n}" for n in names] or modules()
    started = time.monotonic()
    failed: list[str] = []
    # Longest first would need history; the pool keeps all workers busy anyway, and the slowest modules start with the first jobs.
    with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        for name, code, seconds, output in pool.map(run_module, names):
            tail = output.strip().splitlines()[-1] if output.strip() else ""
            print(f"{'ok  ' if code == 0 else 'FAIL'} {name} {seconds:.1f}s {tail if code == 0 else ''}".rstrip(), flush=True)
            if code != 0:
                failed.append(name)
                print(output, flush=True)
    total = time.monotonic() - started
    print(f"LOCAL_DEV TESTS {'FAIL' if failed else 'PASS'}: {len(names) - len(failed)}/{len(names)} modules in {total:.0f}s"
          + (f"; failed: {', '.join(failed)}" if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
