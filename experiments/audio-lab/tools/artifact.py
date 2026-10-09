#!/usr/bin/env python3
"""Minimal owned audio-lab app extraction and exact-head integrity manifest."""
import argparse
import hashlib
import json
import os
import plistlib
from pathlib import Path
import stat
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[3]
ENTITLEMENTS = ROOT / "experiments/audio-lab/Godot/audio-entitlements.plist"
EXPECTED_ENTITLEMENTS = {"com.apple.security.cs." + key: True for key in (
    "allow-jit", "allow-unsigned-executable-memory",
    "allow-dyld-environment-variables", "disable-library-validation")}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_signature(bundle):
    subprocess.run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
    result = subprocess.run(["/usr/bin/codesign", "--display", "--entitlements", ":-", str(bundle)],
                            check=True, capture_output=True)
    actual = plistlib.loads(result.stdout)
    if actual != EXPECTED_ENTITLEMENTS:
        raise ValueError("Executable entitlement mismatch")
    return actual


def export_binding(path, head, bundle):
    if not path.is_relative_to(ROOT / "artifacts/audio-lab"):
        raise ValueError("Export receipt must be owned evidence")
    receipt = json.loads(path.read_text())
    if (receipt["task"] != "t_7dac6703" or receipt["head"] != head or receipt["exit"] != 0
            or receipt["timed_out"] or not receipt["source_unchanged"] or receipt["status"]
            or receipt["workspace"] != str(ROOT) or str(bundle) not in receipt["command"]):
        raise ValueError("Export receipt does not bind clean direct-app candidate")
    log = Path(receipt["log"]).resolve()
    if not log.is_relative_to(ROOT / "artifacts/audio-lab") or digest(log) != receipt["log_sha256"]:
        raise ValueError("Export log mismatch")
    return dict(path=str(path.relative_to(ROOT)), sha256=digest(path))


def inventory(bundle):
    entries = []
    for p in sorted(bundle.rglob("*")):
        relative = p.relative_to(bundle).as_posix()
        mode = stat.S_IMODE(p.lstat().st_mode)
        if p.is_symlink():
            target = os.readlink(p)
            if not (p.parent / target).resolve().is_relative_to(bundle):
                raise ValueError("Escaping app symlink")
            entries.append(dict(path=relative, kind="symlink", target=target, mode=mode))
        elif p.is_file():
            entries.append(dict(path=relative, kind="file", bytes=p.stat().st_size,
                                sha256=hashlib.sha256(p.read_bytes()).hexdigest(), mode=mode))
        elif p.is_dir():
            entries.append(dict(path=relative, kind="directory", mode=mode))
        else:
            raise ValueError("Unsupported payload member")
    return entries


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["extract", "seal", "verify"])
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--head", required=True)
    parser.add_argument("--export-receipt", type=Path)
    a = parser.parse_args()
    directory = a.directory.resolve()
    if not directory.is_relative_to(ROOT / "artifacts/audio-lab"):
        raise SystemExit("Only own ignored audio-lab artifacts")
    if subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip() != a.head:
        raise SystemExit("Candidate head mismatch")
    bundle = directory / "AudioLab.app"
    manifest = directory / "manifest.json"
    if a.mode == "extract":
        if bundle.exists() or manifest.exists():
            raise SystemExit("Refuse to overwrite app or seal")
        with zipfile.ZipFile(directory / "AudioLab.zip") as archive:
            for info in archive.infolist():
                path = directory / info.filename
                if not path.resolve().is_relative_to(directory) or not info.filename.startswith("AudioLab.app/"):
                    raise SystemExit("Unexpected ZIP member")
                mode = info.external_attr >> 16
                if stat.S_ISLNK(mode):
                    raise SystemExit("ZIP symlinks require separate audited extraction")
                archive.extract(info, directory)
                if mode and not info.is_dir():
                    path.chmod(stat.S_IMODE(mode))
        print("Extracted owned synthetic app", bundle)
        return
    if a.mode == "seal":
        if manifest.exists():
            raise SystemExit("Seal is immutable")
        if subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip():
            raise SystemExit("Seal requires clean committed candidate")
        if not a.export_receipt or not bundle.is_dir() or bundle.is_symlink():
            raise SystemExit("Seal needs direct app and export receipt")
        binding = export_binding(a.export_receipt.resolve(), a.head, bundle)
        if plistlib.loads(ENTITLEMENTS.read_bytes()) != EXPECTED_ENTITLEMENTS:
            raise SystemExit("Unexpected entitlement recipe")
        subprocess.run(["/usr/bin/codesign", "--force", "--deep", "--sign", "-",
                        "--entitlements", str(ENTITLEMENTS), str(bundle)], check=True)
        actual = verify_signature(bundle)
        data = dict(schema=2, task="t_7dac6703", head=a.head, bundle="AudioLab.app",
                    provenance="Only independently authored audio lab, synthetic recipe compiled into own assembly; no original audio or external assets.",
                    bundle_mode=stat.S_IMODE(bundle.lstat().st_mode), entries=inventory(bundle),
                    entitlement_recipe_sha256=digest(ENTITLEMENTS), entitlements=actual, export_receipt=binding)
        with manifest.open("x") as out:
            out.write(json.dumps(data, indent=2) + "\n")
    else:
        data = json.loads(manifest.read_text())
        if (data["schema"] != 2 or data["task"] != "t_7dac6703" or data["head"] != a.head
                or data["bundle"] != "AudioLab.app" or bundle.is_symlink()
                or data["bundle_mode"] != stat.S_IMODE(bundle.lstat().st_mode)
                or data["entries"] != inventory(bundle)):
            raise SystemExit("Manifest/payload mismatch")
        binding = export_binding((ROOT / data["export_receipt"]["path"]).resolve(), a.head, bundle)
        if binding != data["export_receipt"] or data["entitlement_recipe_sha256"] != digest(ENTITLEMENTS):
            raise SystemExit("Recipe/export receipt mismatch")
        if verify_signature(bundle) != data["entitlements"]:
            raise SystemExit("Entitlement mismatch")
    print("AUDIO_ARTIFACT_INTEGRITY_PASS", a.mode, a.head, "entries", len(data["entries"]),
          "manifest_sha256", hashlib.sha256(manifest.read_bytes()).hexdigest())


if __name__ == "__main__":
    main()
