#!/usr/bin/env python3
"""Run one real export under its candidate lease and publish it only after success."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys
from typing import Sequence

from . import config, retention
from .manifests import SUPPORTED_TOOLS, sha256


def _candidate_path(root_id: str, task: str, candidate: str) -> tuple[Path, str]:
    if not root_id or not task or not candidate:
        raise ValueError("export endpoint requires root, task, and candidate identity")
    conf = config.load_config()
    roots = conf.get("artifactRoots")
    root_text = roots.get(root_id) if isinstance(roots, dict) else None
    if not isinstance(root_text, str):
        raise ValueError("export endpoint root is not explicitly configured")
    root = config.canonical_existing(root_text, directory=True)
    return root / task / candidate, f"{root_id}:{task}:{candidate}"


def _write_current(candidate: Path, manifest: Path) -> None:
    pointer = candidate.parent / "current.json"
    config.write_atomic(pointer, {"schema": "arcanum-build-current-v2",
                                  "manifestRelativePath": f"{candidate.name}/manifest.json",
                                  "manifestSha256": sha256(manifest)})


def run_export(args: argparse.Namespace) -> int:
    if args.tool not in SUPPORTED_TOOLS or args.task not in SUPPORTED_TOOLS[args.tool]:
        raise ValueError("export endpoint tool/task mapping is unsupported")
    candidate, candidate_id = _candidate_path(args.root_id, args.task, args.candidate)
    manifest = Path(args.manifest).resolve(strict=False)
    if manifest != (candidate / "manifest.json").resolve(strict=False):
        raise ValueError("export endpoint manifest must be the exact candidate manifest")
    candidate.mkdir(mode=0o700, parents=True, exist_ok=True)
    lease = retention._try_exclusive(candidate / ".export-lease.lock")
    if lease is None:
        raise ValueError("candidate is already exporting")
    try:
        completed = subprocess.run(args.command, check=False)
        if completed.returncode:
            return completed.returncode
        with config.registry_lock():
            # The root can change while an export is running; re-derive it
            # under the lifecycle lock and reject rather than registering an
            # artifact outside its explicitly configured canonical root.
            verified_candidate, verified_id = _candidate_path(args.root_id, args.task, args.candidate)
            if verified_candidate != candidate or verified_id != candidate_id:
                raise ValueError("configured export root changed during export")
            registry = config.load_registry()
            retention.register_export(registry, tool=args.tool, candidate_id=candidate_id,
                                      manifest=manifest, lease_held=True)
            config.save_registry(registry)
            _write_current(candidate, manifest)
        return 0
    finally:
        retention._release(lease)


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--tool", required=True, choices=SUPPORTED_TOOLS)
    result.add_argument("--root-id", required=True)
    result.add_argument("--task", required=True)
    result.add_argument("--candidate", required=True)
    result.add_argument("--manifest", required=True)
    result.add_argument("command", nargs=argparse.REMAINDER)
    return result


def main(argv: Sequence[str] | None = None) -> int:
    args = parser().parse_args(argv)
    if not args.command or args.command[0] != "--" or len(args.command) == 1:
        raise ValueError("export endpoint requires a command after --")
    args.command = args.command[1:]
    return run_export(args)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(f"export-endpoint: {error}", file=sys.stderr)
        raise SystemExit(2)
