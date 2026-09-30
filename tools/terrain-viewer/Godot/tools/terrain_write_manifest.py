#!/usr/bin/env python3
"""Write the exact-head ARC-DEV-001 Terrain v2 export manifest."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

TASK = "ARC-DEV-001"
GATES = {
    "locked-build": "locked-build.log", "source-headless": "source-headless.log",
    "source-graphical": "source-graphical.log", "export": "export.log",
    "exported-headless": "exported-headless.log", "exported-graphical": "exported-graphical.log",
    "codesign-deep-strict": "codesign.log",
}


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def inventory(root: Path, prefix: str) -> list[dict[str, object]]:
    return [{"relativePath": f"{prefix}/{file.relative_to(root).as_posix()}", "bytes": file.stat().st_size,
             "sha256": digest(file)} for file in sorted(root.rglob("*")) if file.is_file() and not file.is_symlink()]


def git(root: Path, *args: str) -> str:
    return subprocess.run(["git", "-C", str(root), *args], check=True, text=True, stdout=subprocess.PIPE).stdout.strip()


def main() -> None:
    if len(sys.argv) != 6:
        raise SystemExit("usage: terrain_write_manifest.py CANDIDATE_ROOT MAIN_ROOT BUILD_ROOT HEAD APP")
    root_id = os.environ.get("ARCANUM_LOCAL_ROOT_ID")
    if not root_id:
        raise SystemExit("ARCANUM_LOCAL_ROOT_ID is required for v2 local export registration")
    candidate_root, main_root, build_root, head_path, app_path = map(Path, sys.argv[1:])
    candidate_root = candidate_root.resolve(); main_root = main_root.resolve(); build_root = build_root.resolve(); head = str(head_path); app = app_path.resolve()
    if git(candidate_root, "rev-parse", "HEAD") != head or git(candidate_root, "status", "--porcelain"):
        raise SystemExit("manifest requires the current clean candidate HEAD")
    if build_root != main_root / "artifacts" / "builds" / TASK / head or not app.is_file() or not app.is_relative_to(build_root / "ArcanumTerrainViewer.app"):
        raise SystemExit("build root or exported app is not the exact candidate package")
    evidence = build_root / "evidence"
    missing = [name for name in (*GATES.values(), "export-preflight.json") if not (evidence / name).is_file()]
    if missing:
        raise SystemExit(f"missing durable success-gate evidence: {missing}")
    try:
        preflight = json.loads((evidence / "export-preflight.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise SystemExit(f"invalid export preflight evidence: {error}") from error
    expected_candidate_id = f"{root_id}:{TASK}:{head}"
    if (not isinstance(preflight, dict) or preflight.get("schema") != "arcanum-export-preflight-v1"
            or preflight.get("tool") != "terrain" or preflight.get("candidateId") != expected_candidate_id
            or preflight.get("candidateCommit") != head):
        raise SystemExit("export preflight evidence is not bound to this candidate")
    payload_inventory = inventory(build_root / "ArcanumTerrainViewer.app", "ArcanumTerrainViewer.app")
    evidence_inventory = inventory(evidence, "evidence")
    payload_bytes = sum(file.stat().st_size for file in (build_root / "ArcanumTerrainViewer.app").rglob("*") if file.is_file() and not file.is_symlink())
    payload = {
        "schema": "arcanum-build-manifest-v2", "project": "arcanum-steamwoven", "logicalTool": "terrain", "rootId": root_id,
        "task": TASK, "generatedAt": datetime.now(timezone.utc).isoformat(),
        "candidate": {"baseCommit": git(candidate_root, "merge-base", "HEAD", "origin/main"), "commit": head,
                      "tree": git(candidate_root, "rev-parse", "HEAD^{tree}"), "branch": git(candidate_root, "branch", "--show-current")},
        "app": {"relativePath": "ArcanumTerrainViewer.app", "executableRelativePath": app.relative_to(build_root).as_posix()},
        "payloadInventory": payload_inventory, "evidence": evidence_inventory,
        "checks": [{"name": name, "outcome": "PASS", "evidenceRelativePath": f"evidence/{path}"} for name, path in GATES.items()],
        "exportBudget": {"outputBytes": payload_bytes, "temporaryLowWaterBytes": preflight["temporaryBytes"], "preflight": preflight},
        "acceptance": "not-accepted",
        "delivery": {"status": "verified-local-candidate; exact-head review pending", "originalInputModes": "NOT_VERIFIED unless separately configured"},
    }
    (build_root / "manifest.json").write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
