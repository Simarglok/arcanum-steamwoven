#!/usr/bin/env python3
"""Bounded WORLD validation children. Private original argv/output never leave memory."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time


def run_child(argv: list[str], env: dict[str, str], timeout: int) -> tuple[int, str]:
    child = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                             env=env, start_new_session=True)
    try:
        output, _ = child.communicate(timeout=timeout)
        return child.returncode, output
    except subprocess.TimeoutExpired:
        os.killpg(child.pid, signal.SIGTERM)
        try:
            child.communicate(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(child.pid, signal.SIGKILL)
            child.communicate()
        return 124, ""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", type=Path, required=True)
    parser.add_argument("--app", type=Path)
    parser.add_argument("--kind", choices=("original", "synthetic", "persistence"), default="original")
    parser.add_argument("--headless", action="store_true")
    parser.add_argument("--timeout", type=int, default=60)
    args = parser.parse_args()
    if not 1 <= args.timeout <= 180:
        raise ValueError("bounded timeout outside permitted range")
    root = args.candidate_root.resolve(strict=True)
    sys.path.insert(0, str(root))
    from tools.local_dev import config, launch
    executable = str(args.app.resolve(strict=True)) if args.app else "/Applications/Godot_mono.app/Contents/MacOS/Godot"
    prefix = [executable] + ([] if args.app else ["--path", str(root / "tools/terrain-viewer/Godot")])
    if args.headless:
        prefix += ["--headless"]
    env = dict(os.environ)
    env.update(__GODOT_SHELL_ENV_SET="1", DOTNET_ROOT=str(Path.home() / ".local/share/dotnet"),
               PATH=str(Path.home() / ".local/share/dotnet") + ":/usr/bin:/bin:/usr/sbin:/sbin")
    head = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], text=True, capture_output=True, check=True).stdout.strip()
    began = datetime.now(timezone.utc).isoformat(); start = time.monotonic()
    if args.kind == "original":
        if args.headless:
            raise ValueError("original readback requires graphical rendering")
        argv = prefix + ["--", "--native-smoke", *launch.terrain_arguments(config.load_config().get("inputs", {}), "world-diagnostic")]
        code, output = run_child(argv, env, args.timeout)
        passed = code == 0 and any(line.startswith("ARC-WORLD-001 native-smoke PASS ")
                                  and "fingerprints=immutable rendered=PASS" in line for line in output.splitlines())
    elif args.kind == "synthetic":
        code, output = run_child(prefix + ["--", "--self-test"], env, args.timeout)
        passed = code == 0 and "ARC-WORLD-001 self-test PASS" in output
    else:
        # This file is created by this runner alone, never ordinary preferences.
        import tempfile
        with tempfile.TemporaryDirectory(prefix="world-persistence-", dir=os.environ.get("TMPDIR")) as directory:
            env["WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH"] = str(Path(directory) / "owned.json")
            passed = True; code = 0
            for stage in ("write", "read", "reset", "defaults"):
                code, output = run_child(prefix + ["--", "--world-settings-test", stage], env, min(args.timeout, 30))
                passed &= code == 0 and f"ARC-WORLD-001 settings-test PASS {stage}" in output
                if not passed:
                    break
    # No raw output, argv, input path, pixel or protected content in durable logs.
    summary = {"schema": "arcanum-world-validation-v1", "kind": args.kind,
               "candidateCommit": head, "host": "exported" if args.app else "source", "startedAt": began,
               "elapsedSeconds": round(time.monotonic() - start, 3), "exitCode": code,
               "graphical": not args.headless, "outcome": "PASS" if passed else "FAIL",
               "diagnostics": [line for line in output.splitlines() if line.startswith(("ARC-WORLD-001", "ARC-RENDER-003 self-test FAIL", "ARC-RENDER-004 native-smoke failure", "ARC-RENDER-004 native-smoke readback", "ARC-RENDER-005 actor native raster"))]}
    print(json.dumps(summary, sort_keys=True))
    return 0 if passed else 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, subprocess.SubprocessError):
        print('WORLD validation FAIL category=environment-or-input')
        raise SystemExit(2)
