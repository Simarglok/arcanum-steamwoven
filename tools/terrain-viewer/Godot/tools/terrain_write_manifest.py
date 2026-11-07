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

TASKS = {"ARC-DEV-001": "builds", "ARC-WORLD-001": "world-builds", "ARC-GPAD-004": "world-builds", "ARC-RENDER-006": "world-builds", "ARC-WORLD-003": "world-builds"}
TASKS["ARC-RENDER-007"] = "world-builds"
TASKS["ARC-INPUT-001"] = "world-builds"
TASKS["ARC-WORLD-007"] = "world-builds"
TASKS["ARC-WORLD-009"] = "world-builds"
TASKS["ARC-RENDER-008"] = "world-builds"
# ARC-DEV-015: universal map-mode builds, exported with the synthetic gates only (no pinned Crash Site run).
TASKS["ARC-DEV-015"] = "world-builds"
AREA_TASKS = ("ARC-RENDER-007", "ARC-INPUT-001", "ARC-WORLD-007", "ARC-WORLD-009", "ARC-RENDER-008")
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
    if len(sys.argv) not in (6, 7):
        raise SystemExit("usage: terrain_write_manifest.py CANDIDATE_ROOT MAIN_ROOT BUILD_ROOT HEAD APP [TASK]")
    task = sys.argv[6] if len(sys.argv) == 7 else "ARC-DEV-001"
    if task not in TASKS:
        raise SystemExit("Unsupported terrain export task")
    gates = dict(GATES)
    if task in ("ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003", "ARC-RENDER-007", "ARC-INPUT-001", "ARC-WORLD-007", "ARC-WORLD-009", "ARC-RENDER-008"):
        gates.update({"focused-tests": "tests.log", "source-world-original": "source-world-original.log",
                      "exported-world-original": "exported-world-original.log", "source-world-persistence": "source-world-persistence.log",
                      "exported-world-persistence": "exported-world-persistence.log", "dual-hostfxr": "hostfxr.log"})
    if task == "ARC-DEV-015":
        gates.update({"focused-tests": "tests.log", "dual-hostfxr": "hostfxr.log"})
    if task in ("ARC-RENDER-006", "ARC-WORLD-003", *AREA_TASKS):
        gates.update({"human-metadata": "human-metadata.log", "source-human-original": "source-human-original.log", "exported-human-original": "exported-human-original.log"})
    if task in ("ARC-WORLD-003", *AREA_TASKS):
        gates.update({"source-grid-original": "source-grid-original.log", "exported-grid-original": "exported-grid-original.log"})
    if task in AREA_TASKS:
        gates.update({f"{host}-{name}": f"{host}-{name}.log" for host in ("source", "exported")
                      for name in ("area-original", "area-grid", "area-seams", "area-nearest")})
    root_id = os.environ.get("ARCANUM_LOCAL_ROOT_ID")
    if not root_id:
        raise SystemExit("ARCANUM_LOCAL_ROOT_ID is required for v2 local export registration")
    candidate_root, main_root, build_root, head_path, app_path = map(Path, sys.argv[1:6])
    candidate_root = candidate_root.resolve(); main_root = main_root.resolve(); build_root = build_root.resolve(); head = str(head_path); app = app_path.resolve()
    if (build_root / "manifest.json").exists():
        raise SystemExit("immutable candidate manifest already exists")
    sys.path.insert(0, str(candidate_root))
    from tools.local_dev.export_endpoint import bound_budget
    if git(candidate_root, "rev-parse", "HEAD") != head or git(candidate_root, "status", "--porcelain"):
        raise SystemExit("manifest requires the current clean candidate HEAD")
    if build_root != main_root / "artifacts" / TASKS[task] / task / head or not app.is_file() or not app.is_relative_to(build_root / "ArcanumTerrainViewer.app"):
        raise SystemExit("build root or exported app is not the exact candidate package")
    evidence = build_root / "evidence"
    missing = [name for name in (*gates.values(), "export-preflight.json") if not (evidence / name).is_file() or not (evidence / name).stat().st_size]
    if missing:
        raise SystemExit(f"missing durable success-gate evidence: {missing}")
    try:
        preflight = json.loads((evidence / "export-preflight.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise SystemExit(f"invalid export preflight evidence: {error}") from error
    expected_candidate_id = f"{root_id}:{task}:{head}"
    if task in ("ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003", "ARC-RENDER-007", "ARC-INPUT-001", "ARC-WORLD-007", "ARC-WORLD-009", "ARC-RENDER-008"):
        for host in ("source", "exported"):
            for kind in ("original", "persistence"):
                bound = json.loads((evidence / f"{host}-world-{kind}.log").read_text(encoding="utf-8"))
                if (bound.get("schema"), bound.get("candidateCommit"), bound.get("host"), bound.get("kind"), bound.get("outcome")) != ("arcanum-world-validation-v1", head, host, kind, "PASS"):
                    raise SystemExit("WORLD gate evidence does not bind the candidate")
    if task in ("ARC-RENDER-006", "ARC-WORLD-003", *AREA_TASKS):
        from tools.local_dev.human_validation import validate_reports
        validate_reports(evidence, head, "ARC-WORLD-003" if task in AREA_TASKS else task)
    if task in ("ARC-WORLD-003", *AREA_TASKS):
        from tools.local_dev.grid_export_gates import validate_reports as validate_gate_reports
        validate_gate_reports(evidence, head, task)
        from tools.local_dev.grid_validation import validate_reports as validate_grid_reports
        validate_grid_reports(evidence, head, task)
    if task in AREA_TASKS:
        from tools.local_dev.area_validation import validate_reports as validate_area_reports
        validate_area_reports(evidence, head, task)
    if (not isinstance(preflight, dict) or preflight.get("schema") != "arcanum-export-preflight-v1"
            or preflight.get("tool") != "terrain" or preflight.get("candidateId") != expected_candidate_id
            or preflight.get("candidateCommit") != head):
        raise SystemExit("export preflight evidence is not bound to this candidate")
    payload_inventory = inventory(build_root / "ArcanumTerrainViewer.app", "ArcanumTerrainViewer.app")
    evidence_inventory = inventory(evidence, "evidence")
    payload_bytes = sum(file.stat().st_size for file in (build_root / "ArcanumTerrainViewer.app").rglob("*") if file.is_file() and not file.is_symlink())
    payload = {
        "schema": "arcanum-build-manifest-v2", "project": "arcanum-steamwoven", "logicalTool": "terrain", "rootId": root_id,
        "task": task, "generatedAt": datetime.now(timezone.utc).isoformat(),
        "candidate": {"baseCommit": git(candidate_root, "merge-base", "HEAD", "origin/main"), "commit": head,
                      "tree": git(candidate_root, "rev-parse", "HEAD^{tree}"), "branch": git(candidate_root, "branch", "--show-current")},
        "app": {"relativePath": "ArcanumTerrainViewer.app", "executableRelativePath": app.relative_to(build_root).as_posix(),
                "ordinaryArgs": ["--mode", "area"] if task in (*AREA_TASKS, "ARC-DEV-015") else ["--mode", "world-diagnostic"] if task in ("ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003") else []},
        "payloadInventory": payload_inventory, "evidence": evidence_inventory,
        "checks": [{"name": name, "outcome": "PASS", "evidenceRelativePath": f"evidence/{path}"} for name, path in gates.items()],
        "exportBudget": bound_budget(build_root, "terrain", expected_candidate_id, payload_bytes),
        "acceptance": "not-accepted",
        "delivery": {"status": "verified-local-candidate; exact-head review pending", "originalInputModes": "PASS" if task in ("ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003", "ARC-RENDER-007", "ARC-INPUT-001", "ARC-WORLD-007", "ARC-WORLD-009", "ARC-RENDER-008") else "NOT_VERIFIED unless separately configured", "ordinaryLaunch": "NOT_VERIFIED"},
    }
    (build_root / "manifest.json").write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
