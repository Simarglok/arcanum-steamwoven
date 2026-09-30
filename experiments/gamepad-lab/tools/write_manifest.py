#!/usr/bin/env python3
"""Write ARC-GPAD-003's immutable local v2 export manifest after every gate."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

TASK = "ARC-GPAD-003"
REQUIRED_LOGS = (
    "sdk-info.log", "locked-restore.log", "locked-tests-restore.log", "release-build.log",
    "core-harness.log", "editor-import.log", "debug-build.log", "godot-self-test.log",
    "export.log", "exported-smoke.log", "persistence-write.log",
    "persistence-verify-nondefault.log", "persistence-verify-default.log", "ordinary-launch.log",
    "plist-lint.log", "codesign.log",
)
REQUIRED_REPORTS = (
    "exported-smoke.json", "persistence-write.json", "persistence-verify-nondefault.json",
    "persistence-verify-default.json", "ordinary-launch.png",
)
GATES = {
    "core-harness": "core-harness.log", "godot-self-test": "godot-self-test.log", "export": "export.log",
    "exported-smoke": "exported-smoke.log", "codesign-deep-strict": "codesign.log",
}


def git(root: Path, *args: str) -> str:
    return subprocess.run(["git", "-C", str(root), *args], check=True, text=True,
                          stdout=subprocess.PIPE).stdout.strip()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def inventory(root: Path, prefix: str) -> list[dict[str, object]]:
    return [{"relativePath": f"{prefix}/{path.relative_to(root).as_posix()}",
             "bytes": path.stat().st_size, "sha256": sha256(path)}
            for path in sorted(item for item in root.rglob("*") if item.is_file() and not item.is_symlink())]


def validate_report(path: Path) -> None:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise SystemExit(f"invalid persistence/smoke report {path.name}: {error}") from error
    if payload.get("status") != "passed" or payload.get("isolatedTestStorage") is not True:
        raise SystemExit(f"report did not pass with isolated storage: {path.name}")


def write_json(path: Path, payload: object) -> None:
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> None:
    if len(sys.argv) != 6:
        raise SystemExit("usage: write_manifest.py CANDIDATE_ROOT MAIN_ROOT BUILD_ROOT HEAD APP")
    root_id = os.environ.get("ARCANUM_LOCAL_ROOT_ID")
    if not root_id:
        raise SystemExit("ARCANUM_LOCAL_ROOT_ID is required for v2 local export registration")
    candidate_root, main_root, build_root, head_path, app_path = map(Path, sys.argv[1:])
    candidate_root = candidate_root.resolve(); main_root = main_root.resolve(); build_root = build_root.resolve()
    head = str(head_path); app = app_path.resolve()
    if git(candidate_root, "rev-parse", "HEAD") != head or git(candidate_root, "status", "--porcelain"):
        raise SystemExit("manifest requires the current clean candidate HEAD")
    if build_root != main_root / "artifacts" / "builds" / TASK / head or not app.is_file() or not app.is_relative_to(build_root / "GamepadLab.app"):
        raise SystemExit("build root or exported app is not the exact candidate package")
    evidence = build_root / "evidence"
    missing = [name for name in (*REQUIRED_LOGS, *REQUIRED_REPORTS) if not (evidence / name).is_file()]
    if missing or not (evidence / "ordinary-launch.png").stat().st_size:
        raise SystemExit(f"missing or empty durable evidence: {missing}")
    for report in REQUIRED_REPORTS[:-1]:
        validate_report(evidence / report)
    base = git(candidate_root, "merge-base", "HEAD", "origin/main")
    tree = git(candidate_root, "rev-parse", "HEAD^{tree}")
    payload_inventory = inventory(build_root / "GamepadLab.app", "GamepadLab.app")
    evidence_inventory = inventory(evidence, "evidence")
    payload_bytes = sum(file.stat().st_size for file in (build_root / "GamepadLab.app").rglob("*") if file.is_file() and not file.is_symlink())
    manifest = {
        "schema": "arcanum-build-manifest-v2", "project": "arcanum-steamwoven", "logicalTool": "gamepad",
        "rootId": root_id, "task": TASK, "generatedAt": datetime.now(timezone.utc).isoformat(),
        "candidate": {"baseCommit": base, "commit": head, "tree": tree, "branch": git(candidate_root, "branch", "--show-current")},
        "app": {"relativePath": "GamepadLab.app", "executableRelativePath": app.relative_to(build_root).as_posix()},
        "payloadInventory": payload_inventory, "evidence": evidence_inventory,
        "checks": [{"name": name, "outcome": "PASS", "evidenceRelativePath": f"evidence/{path}"} for name, path in GATES.items()],
        "exportBudget": {"outputBytes": payload_bytes, "temporaryLowWaterBytes": 0},
        "acceptance": "not-accepted",
        "delivery": {"status": "verified-local-candidate; exact-head review pending", "physicalXboxBluetooth": "NOT_VERIFIED", "signingNotarization": "NOT_VERIFIED"},
    }
    write_json(build_root / "manifest.json", manifest)


if __name__ == "__main__":
    main()
