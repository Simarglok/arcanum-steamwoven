#!/usr/bin/env python3
"""Verify bundled macOS hostfxr architectures, not SDK directory conventions."""
from __future__ import annotations

from pathlib import Path
import subprocess
import sys


def verify_hostfxr(app: Path) -> None:
    resources = app / "Contents" / "Resources"
    for directory, architecture in (("arm64", "arm64"), ("x86_64", "x86_64")):
        library = resources / f"data_Arcanum.TerrainViewer_macos_{directory}" / "libhostfxr.dylib"
        if not library.is_file() or library.is_symlink():
            raise ValueError(f"missing bundled {architecture} hostfxr")
        architectures = subprocess.check_output(
            ["/usr/bin/lipo", "-archs", str(library)], text=True
        ).split()
        if architecture not in architectures:
            raise ValueError(f"bundled hostfxr does not contain {architecture}")
    print("hostfxr both-architectures PASS arm64/x86_64 Mach-O verified")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: hostfxr_validate.py APP")
    verify_hostfxr(Path(sys.argv[1]))
