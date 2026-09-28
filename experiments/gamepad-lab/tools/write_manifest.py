#!/usr/bin/env python3
"""Write the ignored exact-head ARC-GPAD-002 manifest and current pointer after all gates pass."""
import hashlib
import json
import pathlib
import subprocess
import sys

candidate_root, main_root, build_root, head, app = map(pathlib.Path, sys.argv[1:])
head_text = str(head)
if subprocess.run(["git", "-C", str(candidate_root), "status", "--porcelain"], check=True, capture_output=True, text=True).stdout:
    raise SystemExit("candidate became dirty during validation")
app_path = pathlib.Path(app)
if not app_path.is_file():
    raise SystemExit("exported app executable is missing")
checks = ["sdk-info.log", "locked-restore.log", "locked-tests-restore.log", "release-build.log", "core-harness.log", "editor-import.log", "debug-build.log", "godot-self-test.log", "export.log", "exported-smoke.log", "plist-lint.log", "codesign.log"]
evidence = build_root / "evidence"
missing = [name for name in checks if not (evidence / name).is_file()]
if missing:
    raise SystemExit(f"missing gate logs: {missing}")
manifest = {
    "schemaVersion": 1,
    "task": "ARC-GPAD-002",
    "head": head_text,
    "candidateRoot": str(candidate_root),
    "app": str(app_path),
    "appSha256": hashlib.sha256(app_path.read_bytes()).hexdigest(),
    "checks": checks,
    "provenance": "SIMULATION; physical Xbox evidence not verified",
}
(build_root / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
pointer = main_root / "artifacts" / "builds" / "ARC-GPAD-002" / "current.json"
pointer.parent.mkdir(parents=True, exist_ok=True)
temporary = pointer.with_suffix(".json.tmp")
temporary.write_text(json.dumps({"schemaVersion": 1, "head": head_text, "manifest": str(build_root / "manifest.json")}, indent=2) + "\n")
temporary.replace(pointer)
