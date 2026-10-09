#!/usr/bin/env python3
"""Minimal owned audio-lab app extraction and exact-head integrity manifest."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[3]


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
        subprocess.run(["/usr/bin/codesign", "--force", "--deep", "--sign", "-", str(bundle)], check=True)
        subprocess.run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
        data = dict(schema=1, task="t_7dac6703", head=a.head, bundle="AudioLab.app",
                    provenance="Only independently authored audio lab, synthetic recipe compiled into own assembly; no original audio or external assets.",
                    entries=inventory(bundle), archive_sha256=hashlib.sha256((directory / "AudioLab.zip").read_bytes()).hexdigest())
        with manifest.open("x") as out:
            out.write(json.dumps(data, indent=2) + "\n")
    else:
        data = json.loads(manifest.read_text())
        if data["head"] != a.head or data["entries"] != inventory(bundle):
            raise SystemExit("Manifest/payload mismatch")
        if data["archive_sha256"] != hashlib.sha256((directory / "AudioLab.zip").read_bytes()).hexdigest():
            raise SystemExit("Archive mismatch")
        subprocess.run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
    print("AUDIO_ARTIFACT_INTEGRITY_PASS", a.mode, a.head, "entries", len(data["entries"]),
          "manifest_sha256", hashlib.sha256(manifest.read_bytes()).hexdigest())


if __name__ == "__main__":
    main()
