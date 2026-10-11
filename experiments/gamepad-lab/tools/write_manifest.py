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

TASK = os.environ.get("ARCANUM_GAMEPAD_EXPORT_TASK", "ARC-GPAD-003")
if TASK not in ("ARC-GPAD-003", "ARC-GPAD-005"):
    raise SystemExit("unsupported gamepad export task")
REQUIRED_LOGS = (
    "sdk-info.log", "locked-restore.log", "locked-tests-restore.log", "release-build.log",
    "core-harness.log", "editor-import.log", "debug-build.log", "godot-self-test.log",
    "export.log", "exported-smoke.log", "persistence-write.log",
    "persistence-verify-nondefault.log", "persistence-verify-default.log", "automated-capture.log",
    "plist-lint.log", "codesign.log",
)
REQUIRED_REPORTS = (
    "exported-smoke.json", "persistence-write.json", "persistence-verify-nondefault.json",
    "persistence-verify-default.json", "automated-capture.png", "ordinary-launch-not-verified.json", "export-preflight.json",
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
    if (build_root / "manifest.json").exists():
        raise SystemExit("immutable candidate manifest already exists")
    sys.path.insert(0, str(candidate_root))
    from tools.local_dev.export_endpoint import bound_budget
    if git(candidate_root, "rev-parse", "HEAD") != head or git(candidate_root, "status", "--porcelain"):
        raise SystemExit("manifest requires the current clean candidate HEAD")
    artifact_dir = "gamepad-builds" if TASK == "ARC-GPAD-005" else "builds"
    if build_root != main_root / "artifacts" / artifact_dir / TASK / head or not app.is_file() or not app.is_relative_to(build_root / "GamepadLab.app"):
        raise SystemExit("build root or exported app is not the exact candidate package")
    evidence = build_root / "evidence"
    gates = GATES
    required_logs, required_reports = REQUIRED_LOGS, REQUIRED_REPORTS
    if TASK == "ARC-GPAD-005":
        from tools.local_dev.gamepad_export_gates import GATES as gates, validate_reports
        validate_reports(evidence, head)
        required_logs = tuple(gates.values())
        required_reports = ("exported-smoke.json", "ordinary-launch-not-verified.json", "export-preflight.json", "source-preset-matrix.json", "exported-preset-matrix.json", "source-preset-persistence.json", "exported-preset-persistence.json")
    missing = [name for name in (*required_logs, *required_reports) if not (evidence / name).is_file()]
    if missing or (TASK == "ARC-GPAD-003" and not (evidence / "automated-capture.png").stat().st_size):
        raise SystemExit(f"missing or empty durable evidence: {missing}")
    for report in (("exported-smoke.json",) if TASK == "ARC-GPAD-005" else ("exported-smoke.json", "persistence-write.json", "persistence-verify-nondefault.json", "persistence-verify-default.json")):
        validate_report(evidence / report)
    try:
        preflight = json.loads((evidence / "export-preflight.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise SystemExit(f"invalid export preflight evidence: {error}") from error
    expected_candidate_id = f"{root_id}:{TASK}:{head}"
    if (not isinstance(preflight, dict) or preflight.get("schema") != "arcanum-export-preflight-v1"
            or preflight.get("tool") != "gamepad" or preflight.get("candidateId") != expected_candidate_id
            or preflight.get("candidateCommit") != head):
        raise SystemExit("export preflight evidence is not bound to this candidate")
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
        "checks": [{"name": name, "outcome": "PASS", "evidenceRelativePath": f"evidence/{path}"} for name, path in gates.items()],
        "exportBudget": bound_budget(build_root, "gamepad", expected_candidate_id, payload_bytes),
        "acceptance": "not-accepted",
        "delivery": {"status": "verified-local-candidate; exact-head review pending", "physicalXboxBluetooth": "NOT_VERIFIED", "signingNotarization": "NOT_VERIFIED", "ordinaryLaunch": "NOT_VERIFIED; --capture evidence is automated"},
    }
    write_json(build_root / "manifest.json", manifest)


if __name__ == "__main__":
    main()
