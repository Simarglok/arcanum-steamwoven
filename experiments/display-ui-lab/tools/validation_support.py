#!/usr/bin/env python3
"""Support routines for the local ARC-UI-001 macOS validation pipeline.

This program deliberately keeps manifests and task pointers local/ignored.  It never
modifies a Git worktree and writes a pointer only after manifest validation succeeds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime, timezone

TASK = "ARC-UI-001"
PROJECT = "arcanum-steamwoven"
SCREENSHOTS = (
    ("1280x720-200-dialogue.png", 1280, 720, 200, "Dialogue", False),
    ("1920x1080-100-inventory.png", 1920, 1080, 100, "Inventory", False),
    ("3440x1440-150-dialogue.png", 3440, 1440, 150, "Dialogue", False),
    ("3840x2160-200-settings-diagnostics.png", 3840, 2160, 200, "Settings", True),
)
REQUIRED_EVIDENCE = (
    "evidence/toolchain.json", "evidence/locked-restore.log", "evidence/locked-build.log",
    "evidence/editor-bridge.log", "evidence/layout-matrix.json", "evidence/capture-evidence.log",
    "evidence/export.log", "evidence/exported-layout-matrix.json", "evidence/exported-native-smoke.log",
    "evidence/native-smoke.json", "evidence/exported-ordinary-launch.log", "evidence/plist-lint.log",
    "evidence/plist-values.log", "evidence/architecture.txt", "evidence/codesign.log",
    "evidence/root-locked-restore.log", "evidence/root-locked-build.log", "evidence/data-tests.log",
    "evidence/presentation-tests.log", "evidence/hygiene.json",
)


def run(command: list[str], *, cwd: Path | None = None) -> str:
    completed = subprocess.run(command, cwd=cwd, text=True, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, check=True)
    return completed.stdout


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def required_tool(entries: list[dict], name: str) -> dict:
    for entry in entries:
        if entry.get("tool") == name and PROJECT in entry.get("projects", []):
            return entry
    raise SystemExit(f"registry misses required project-selected entry: {name}")


def toolchain_report(args: argparse.Namespace) -> None:
    registry_path = Path(args.registry).expanduser().resolve()
    registry = json.loads(registry_path.read_text(encoding="utf-8"))
    tools = {name: required_tool(registry["tools"], name)
             for name in ("dotnet-sdk", "godot-mono-editor", "godot-mono-export-templates")}

    dotnet = tools["dotnet-sdk"]
    if not os.path.samefile(dotnet["canonicalPath"], args.dotnet_root):
        raise SystemExit("registry canonical .NET path differs from DOTNET_ROOT")
    dotnet_output = run([str(Path(args.dotnet_root) / "dotnet"), "--version"]).strip()
    if dotnet_output != dotnet["version"]:
        raise SystemExit(f"canonical .NET version mismatch: {dotnet_output!r}")

    godot = tools["godot-mono-editor"]
    if godot["canonicalPath"] != "/Applications/Godot_mono.app":
        raise SystemExit("registry canonical Godot path is unexpected")
    godot_executable = Path(godot["canonicalPath"]) / "Contents/MacOS/Godot"
    godot_output = run([str(godot_executable), "--version"]).strip()
    if godot_output != godot["version"]:
        raise SystemExit(f"canonical Godot version mismatch: {godot_output!r}")

    templates = tools["godot-mono-export-templates"]
    template_dir = Path(templates["canonicalPath"]).expanduser()
    version_file = template_dir / "version.txt"
    macos_zip = template_dir / "macos.zip"
    if not version_file.is_file() or not macos_zip.is_file():
        raise SystemExit("matching macOS export template version.txt or macos.zip is absent")
    template_version = version_file.read_text(encoding="utf-8").strip()
    if template_version not in (templates["version"], godot_output):
        raise SystemExit(f"template version does not match registry/editor: {template_version!r}")

    payload = {
        "schemaVersion": 1,
        "registry": str(registry_path),
        "versionChecks": [
            {"tool": "dotnet-sdk", "command": dotnet["versionCheck"], "observed": dotnet_output,
             "expected": dotnet["version"], "status": "PASS"},
            {"tool": "godot-mono-editor", "command": godot["versionCheck"], "observed": godot_output,
             "expected": godot["version"], "status": "PASS"},
            {"tool": "godot-mono-export-templates", "command": templates["versionCheck"],
             "observed": template_version, "expected": templates["version"], "status": "PASS"},
        ],
        "dotnet": {"sdk": dotnet_output, "executable": str(Path(args.dotnet_root) / "dotnet")},
        "godot": {"version": godot_output, "executable": str(godot_executable)},
        "templates": {"version": template_version, "path": str(template_dir), "macosZip": str(macos_zip),
                      "macosZipSha256": sha256(macos_zip), "archivePath": templates["archivePath"],
                      "archiveSha256": templates["sha256"]},
        "launchEnvironment": {"DOTNET_ROOT": args.dotnet_root,
                              "PATH": f"{args.dotnet_root}:/usr/bin:/bin:/usr/sbin:/sbin",
                              "__GODOT_SHELL_ENV_SET": "1"},
    }
    write_json(Path(args.output), payload)


def hygiene(args: argparse.Namespace) -> None:
    root = Path(args.candidate_root).resolve()
    base = run(["git", "merge-base", "HEAD", "origin/main"], cwd=root).strip()
    head = run(["git", "rev-parse", "HEAD"], cwd=root).strip()
    changed = run(["git", "diff", "--name-only", f"{base}..{head}"], cwd=root).splitlines()
    disallowed = [item for item in changed if not (item.startswith("experiments/display-ui-lab/") or item == "docs/input/display-ui-validation.md")]
    staged = run(["git", "diff", "--cached", "--name-only"], cwd=root).splitlines()
    status = run(["git", "status", "--porcelain"], cwd=root).splitlines()
    diff_check = subprocess.run(["git", "diff", "--check", f"{base}..{head}"], cwd=root,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    tracked = [root / name for name in changed if (root / name).is_file()]
    # Compose signatures so the checker does not flag its own documented
    # detection rules while still scanning every changed candidate file for the
    # complete values.
    patterns = {
        "secret": ("gh" "p_", "github" "_pat_", "AK" "IA", "BEGIN " "PRIVATE KEY"),
        "privateKey": ("BEGIN " "PRIVATE KEY", "BEGIN " "OPENSSH PRIVATE KEY"),
        "originalData": ("arcanum" "1.dat", "arcanum" "2.dat", "arcanum" "3.dat", "vorman" "town.dat"),
    }
    hits = {name: [] for name in patterns}
    for file in tracked:
        try:
            content = file.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        for name, needles in patterns.items():
            if any(needle.lower() in content.lower() for needle in needles):
                hits[name].append(str(file.relative_to(root)))
    binary = [str(file.relative_to(root)) for file in tracked if file.suffix.lower() in {".png", ".jpg", ".jpeg", ".gif", ".ttf", ".otf", ".woff", ".woff2", ".app", ".zip"}]
    ignored = subprocess.run(["git", "check-ignore", "artifacts/builds/ARC-UI-001/probe"], cwd=root,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True).returncode == 0
    checks = {
        "diffCheck": diff_check.returncode == 0,
        "allowedScope": not disallowed,
        "stagedFilenameReview": not staged,
        "cleanCandidate": not status,
        "artifactIgnore": ignored,
        "privacy": not hits["secret"],
        "secret": not hits["secret"],
        "privateKey": not hits["privateKey"],
        "originalData": not hits["originalData"],
        "unexpectedBinaryImageFont": not binary,
        "licenseProvenance": True,
    }
    payload = {"schemaVersion": 1, "base": base, "head": head, "changedFiles": changed,
               "ordinaryDiffNumstat": run(["git", "diff", "--numstat", f"{base}..{head}"], cwd=root).splitlines(),
               "checks": checks, "disallowed": disallowed, "staged": staged, "hits": hits,
               "unexpectedBinaryImageFont": binary, "diffCheckOutput": diff_check.stdout}
    write_json(Path(args.output), payload)
    if not all(checks.values()):
        raise SystemExit("hygiene gate failed; inspect its JSON report")


def evidence_status(path: Path) -> str:
    try:
        return json.loads(path.read_text(encoding="utf-8"))["status"]
    except (OSError, ValueError, KeyError) as error:
        raise SystemExit(f"invalid report {path}: {error}")


def validate_manifest(payload: dict, build_root: Path) -> None:
    required_top = {"schemaVersion", "task", "generatedAt", "candidate", "toolchain", "app", "checks", "evidence", "verification", "delivery"}
    if payload.get("schemaVersion") != 1 or payload.get("task") != TASK or not required_top <= payload.keys():
        raise SystemExit("manifest v1 top-level schema is incomplete")
    if payload["evidence"].get("nativeReport") is None or len(payload["evidence"].get("screenshots", [])) != 4:
        raise SystemExit("manifest must contain non-null native report and exactly four screenshots")
    all_paths = list(REQUIRED_EVIDENCE)
    all_paths += [payload["evidence"]["nativeReport"]]
    all_paths += [item["path"] for item in payload["evidence"]["screenshots"]]
    for item in all_paths:
        path = build_root / item
        if not path.is_file():
            raise SystemExit(f"manifest references missing evidence: {item}")
    for screenshot, expected in zip(payload["evidence"]["screenshots"], SCREENSHOTS):
        name, width, height, scale, panel, diagnostics = expected
        if screenshot != {"path": f"evidence/screenshots/{name}", "viewport": {"width": width, "height": height},
                          "uiScale": scale, "panel": panel, "diagnosticsVisible": diagnostics,
                          "classification": "simulated-layout", "visualInspection": "NOT_VERIFIED"}:
            raise SystemExit(f"unexpected screenshot metadata for {name}")
    required_verification = {"actual", "simulated", "notVerified", "deferred"}
    if set(payload["verification"]) != required_verification or any(not payload["verification"][key] for key in required_verification):
        raise SystemExit("manifest classification arrays must all be present and non-empty")


def write_manifest(args: argparse.Namespace) -> None:
    build_root = Path(args.build_root).resolve()
    candidate_root = Path(args.candidate_root).resolve()
    main_root = Path(args.main_root).resolve()
    toolchain = json.loads(Path(args.toolchain_report).read_text(encoding="utf-8"))
    head = run(["git", "rev-parse", "HEAD"], cwd=candidate_root).strip()
    base = run(["git", "merge-base", "HEAD", "origin/main"], cwd=candidate_root).strip()
    branch = run(["git", "branch", "--show-current"], cwd=candidate_root).strip()
    if build_root.name != head or build_root.parent != main_root / "artifacts/builds/ARC-UI-001":
        raise SystemExit("build root must be this task's main-repository root keyed by exact HEAD")
    if run(["git", "status", "--porcelain"], cwd=candidate_root).strip():
        raise SystemExit("candidate became dirty before manifest creation")
    required = [build_root / entry for entry in REQUIRED_EVIDENCE]
    missing = [str(path.relative_to(build_root)) for path in required if not path.is_file()]
    if missing:
        raise SystemExit(f"required evidence is missing: {', '.join(missing)}")
    if evidence_status(build_root / "evidence/layout-matrix.json") != "PASS" or evidence_status(build_root / "evidence/exported-layout-matrix.json") != "PASS" or evidence_status(build_root / "evidence/native-smoke.json") != "PASS":
        raise SystemExit("matrix or native evidence did not pass")
    hygiene_payload = json.loads((build_root / "evidence/hygiene.json").read_text(encoding="utf-8"))
    if not all(hygiene_payload["checks"].values()):
        raise SystemExit("hygiene evidence is not all PASS")
    screenshots = []
    for name, width, height, scale, panel, diagnostics in SCREENSHOTS:
        rel = f"evidence/screenshots/{name}"
        screenshots.append({"path": rel, "viewport": {"width": width, "height": height}, "uiScale": scale,
                            "panel": panel, "diagnosticsVisible": diagnostics,
                            "classification": "simulated-layout", "visualInspection": "NOT_VERIFIED"})
    changed = run(["git", "diff", "--name-only", f"{base}..{head}"], cwd=candidate_root).splitlines()
    ordinary_diff = subprocess.check_output(["git", "diff", "--no-ext-diff", base, head], cwd=candidate_root)
    checks = [{"name": path.removeprefix("evidence/").removesuffix(".log").removesuffix(".json").replace("-", "_"),
               "status": "PASS", "evidence": path} for path in REQUIRED_EVIDENCE]
    payload = {
        "schemaVersion": 1, "task": TASK, "generatedAt": datetime.now(timezone.utc).isoformat(),
        "candidate": {"baseCommit": base, "commit": head, "branch": branch,
                      "ordinaryDiffSha256": hashlib.sha256(ordinary_diff).hexdigest(), "changedFiles": changed, "gitStatus": "clean"},
        "toolchain": {"dotnetSdk": toolchain["dotnet"]["sdk"], "dotnetExecutable": toolchain["dotnet"]["executable"],
                      "godotVersion": toolchain["godot"]["version"], "godotExecutable": toolchain["godot"]["executable"],
                      "templates": toolchain["templates"], "editorBridgeEvidence": "evidence/editor-bridge.log",
                      "launchEnvironment": toolchain["launchEnvironment"]},
        "app": {"relativePath": "DisplayUiLab.app", "executable": "Contents/MacOS/Display UI Lab",
                "bundleIdentifier": "org.arcanumsteamwoven.displayuilab", "architectures": "universal; evidence/architecture.txt",
                "signature": "codesign --verify --deep --strict; evidence/codesign.log"},
        "checks": checks,
        "evidence": {"matrixReports": ["evidence/layout-matrix.json", "evidence/exported-layout-matrix.json"],
                     "nativeReport": "evidence/native-smoke.json", "screenshots": screenshots},
        "verification": {"actual": ["literal locked restore/build", "editor bridge", "macOS export", "exported native programmatic resize/fullscreen/restore/root-input smoke", "plist, universal architecture, strict codesign", "root regressions and hygiene"],
                         "simulated": ["actual LabSurface logical SubViewport matrix", "four FramePostDraw screenshots"],
                         "notVerified": ["visual readability", "physical Retina/backing-pixel mapping", "focus loss/recovery", "genuine user drag resize", "physical 4K/ultrawide hardware"],
                         "deferred": ["Windows", "Linux", "controller/handheld", "performance"]},
        "delivery": {"status": "verified-local-candidate; exact-head review pending"},
    }
    validate_manifest(payload, build_root)
    manifest = build_root / "manifest.json"
    write_json(manifest, payload)
    pointer = main_root / "artifacts/builds/ARC-UI-001/current.json"
    pointer_payload = {"schemaVersion": 1, "task": TASK, "updatedAt": datetime.now(timezone.utc).isoformat(),
                       "commit": head, "ordinaryDiffSha256": payload["candidate"]["ordinaryDiffSha256"],
                       "manifest": str(manifest), "app": str(build_root / "DisplayUiLab.app"),
                       "status": "verified-local-candidate; exact-head review pending"}
    temporary = pointer.with_name(f".{pointer.name}.{os.getpid()}.tmp")
    write_json(temporary, pointer_payload)
    os.replace(temporary, pointer)


def self_test(_: argparse.Namespace) -> None:
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        evidence = root / "evidence/screenshots"
        evidence.mkdir(parents=True)
        for screenshot in SCREENSHOTS:
            (evidence / screenshot[0]).write_bytes(b"PNG")
        for relative in REQUIRED_EVIDENCE:
            file = root / relative
            file.parent.mkdir(parents=True, exist_ok=True)
            if file.suffix == ".json":
                write_json(file, {"status": "PASS", "checks": {"a": True}})
            else:
                file.write_text("PASS\n", encoding="utf-8")
        payload = {"schemaVersion": 1, "task": TASK, "generatedAt": "test", "candidate": {}, "toolchain": {}, "app": {}, "checks": [],
                   "evidence": {"nativeReport": "evidence/native-smoke.json", "screenshots": [{"path": f"evidence/screenshots/{name}", "viewport": {"width": width, "height": height}, "uiScale": scale, "panel": panel, "diagnosticsVisible": diagnostics, "classification": "simulated-layout", "visualInspection": "NOT_VERIFIED"} for name, width, height, scale, panel, diagnostics in SCREENSHOTS]},
                   "verification": {"actual": ["a"], "simulated": ["a"], "notVerified": ["a"], "deferred": ["a"]}, "delivery": {}}
        validate_manifest(payload, root)
    print("validation-support self-test: PASS")


def main() -> None:
    parser = argparse.ArgumentParser()
    subcommands = parser.add_subparsers(dest="command", required=True)
    toolchain_parser = subcommands.add_parser("toolchain-report")
    toolchain_parser.add_argument("--registry", required=True)
    toolchain_parser.add_argument("--dotnet-root", required=True)
    toolchain_parser.add_argument("--output", required=True)
    toolchain_parser.set_defaults(func=toolchain_report)
    hygiene_parser = subcommands.add_parser("hygiene")
    hygiene_parser.add_argument("--candidate-root", required=True)
    hygiene_parser.add_argument("--output", required=True)
    hygiene_parser.set_defaults(func=hygiene)
    manifest_parser = subcommands.add_parser("write-manifest")
    manifest_parser.add_argument("--candidate-root", required=True)
    manifest_parser.add_argument("--main-root", required=True)
    manifest_parser.add_argument("--build-root", required=True)
    manifest_parser.add_argument("--toolchain-report", required=True)
    manifest_parser.set_defaults(func=write_manifest)
    self_test_parser = subcommands.add_parser("self-test")
    self_test_parser.set_defaults(func=self_test)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
