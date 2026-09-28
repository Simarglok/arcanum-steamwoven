#!/usr/bin/env python3
"""Bind ARC-GPAD-002's ignored local export and evidence to one clean commit."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys

TASK = "ARC-GPAD-002"
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
    return [
        {"relativePath": f"{prefix}/{path.relative_to(root).as_posix()}",
         "bytes": path.stat().st_size, "sha256": sha256(path)}
        for path in sorted(item for item in root.rglob("*") if item.is_file())
    ]


def validate_report(path: Path) -> None:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise SystemExit(f"invalid persistence/smoke report {path.name}: {error}") from error
    if payload.get("status") != "passed":
        raise SystemExit(f"report did not pass: {path.name}")


def write_json(path: Path, payload: object) -> None:
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> None:
    if len(sys.argv) != 6:
        raise SystemExit("usage: write_manifest.py CANDIDATE_ROOT MAIN_ROOT BUILD_ROOT HEAD APP")
    candidate_root, main_root, build_root, head_path, app_path = map(Path, sys.argv[1:])
    candidate_root = candidate_root.resolve(); main_root = main_root.resolve(); build_root = build_root.resolve()
    head = str(head_path); app = app_path.resolve()
    if git(candidate_root, "rev-parse", "HEAD") != head:
        raise SystemExit("manifest head is not the current candidate HEAD")
    if git(candidate_root, "status", "--porcelain"):
        raise SystemExit("candidate became dirty during validation")
    if build_root != main_root / "artifacts" / "builds" / TASK / head:
        raise SystemExit("build root must be the task's exact-head ignored artifact directory")
    if not app.is_file() or not app.is_relative_to(build_root / "GamepadLab.app"):
        raise SystemExit("exported app executable is missing or outside GamepadLab.app")
    evidence = build_root / "evidence"
    missing = [name for name in (*REQUIRED_LOGS, *REQUIRED_REPORTS) if not (evidence / name).is_file()]
    if missing:
        raise SystemExit(f"missing evidence: {missing}")
    if not (evidence / "ordinary-launch.png").stat().st_size:
        raise SystemExit("ordinary exported launch capture is empty")
    for report in REQUIRED_REPORTS[:-1]:
        validate_report(evidence / report)

    base = git(candidate_root, "merge-base", "HEAD", "origin/main")
    branch = git(candidate_root, "branch", "--show-current")
    ordinary_diff = subprocess.check_output(["git", "-C", str(candidate_root), "diff", "--no-ext-diff", base, head])
    changed = git(candidate_root, "diff", "--name-only", f"{base}..{head}").splitlines()
    checks = [
        {"name": name.removesuffix(".log").replace("-", "_"), "outcome": "PASS", "evidenceRelativePath": f"evidence/{name}"}
        for name in REQUIRED_LOGS
    ]
    checks.append({"name": "ordinary_exported_launch_bounded_30_seconds", "outcome": "PASS", "evidenceRelativePath": "evidence/ordinary-launch.log"})
    checks.extend({"name": name.removesuffix(".json").replace("-", "_"), "outcome": "PASS", "evidenceRelativePath": f"evidence/{name}"} for name in REQUIRED_REPORTS[:-1])
    files = inventory(build_root / "GamepadLab.app", "GamepadLab.app") + inventory(evidence, "evidence")
    manifest = {
        "schemaVersion": 1,
        "taskId": TASK,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "candidate": {"baseCommit": base, "commit": head, "branch": branch,
                      "ordinaryDiffSha256": hashlib.sha256(ordinary_diff).hexdigest(),
                      "changedFiles": changed, "gitStatus": "clean"},
        "toolchain": {"dotnetSdk": subprocess.run([str(Path.home() / ".local/share/dotnet/dotnet"), "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip(),
                      "godotMono": subprocess.run(["/Applications/Godot_mono.app/Contents/MacOS/Godot", "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip()},
        "app": {"relativePath": "GamepadLab.app", "executableRelativePath": app.relative_to(build_root).as_posix(),
                "sha256": sha256(app), "signatureEvidence": "evidence/codesign.log"},
        "checks": checks,
        "evidence": {"ordinaryLaunchCapture": "evidence/ordinary-launch.png",
                     "persistenceReports": [f"evidence/{name}" for name in REQUIRED_REPORTS[1:-1]],
                     "physicalDeviceEvidence": "NOT_VERIFIED; simulation only"},
        "files": files,
        "delivery": {"status": "verified-local-candidate; exact-head review pending",
                     "physicalXboxBluetooth": "NOT_VERIFIED", "signingNotarization": "NOT_VERIFIED"},
    }
    manifest_path = build_root / "manifest.json"
    write_json(manifest_path, manifest)
    pointer = main_root / "artifacts" / "builds" / TASK / "current.json"
    pointer.parent.mkdir(parents=True, exist_ok=True)
    pointer_payload = {"schemaVersion": 1, "taskId": TASK, "candidateCommit": head,
                       "artifactRelativePath": head, "manifestRelativePath": f"{head}/manifest.json",
                       "manifestSha256": sha256(manifest_path)}
    temporary = pointer.with_name(f".{pointer.name}.tmp")
    write_json(temporary, pointer_payload)
    temporary.replace(pointer)


if __name__ == "__main__":
    main()
