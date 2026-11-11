"""ARC-DEV-403: the smoke windows stay out of the owner's way.

A viewer smoke opens a real Godot window, and Godot makes itself the foreground application when it starts, which took the
keyboard focus from whatever the owner was doing (and with several smoke lanes running at once, again and again). The
rule is docs/workflow/toolchain.md#smoke-windows-and-focus-arc-dev-403. On macOS `./arcanum smoke` builds one small native
library from `focus_guard_native/arcanum_nofocus.m` (clang, a second) into its settings folder and loads it into the Godot
process of each window smoke through DYLD_INSERT_LIBRARIES (the Godot app's entitlements allow it); the process then
becomes a background-only application that is never active and shows no window, while the window keeps drawing, so the
viewer's own frame readbacks still work. Only smoke runs load it. Anything that cannot build it (another system, no clang,
a compile error) runs as before and says so; `--take-focus` asks for the old behaviour (to watch a smoke run).
"""
from __future__ import annotations

from pathlib import Path
import shutil
import subprocess
import sys
from typing import Callable, Mapping

SOURCE = Path(__file__).resolve().with_name("focus_guard_native") / "arcanum_nofocus.m"
LIBRARY = "arcanum_nofocus.dylib"
INSERT = "DYLD_INSERT_LIBRARIES"
CARRIER = "ARCANUM_SMOKE_FOCUS_GUARD"  # the planned smoke environment's key for the library path (dropped from the child)
BUILD_SECONDS = 120.0
FRAMEWORKS = ("AppKit", "ApplicationServices")


def supported(platform: str = sys.platform) -> bool:
    """The guard is macOS only: Godot's activation calls it turns off are AppKit's."""
    return platform == "darwin"


def compile_command(clang: str, target: Path, source: Path = SOURCE) -> tuple[str, ...]:
    """The clang call: a universal library (Godot's app is one), ARC, the AppKit frameworks, warnings are errors."""
    return (clang, "-dynamiclib", "-fobjc-arc", "-O1", "-Wall", "-Werror", "-Wno-deprecated-declarations",
            "-arch", "arm64", "-arch", "x86_64", "-mmacosx-version-min=11.0",
            *(part for name in FRAMEWORKS for part in ("-framework", name)),
            "-install_name", f"@rpath/{LIBRARY}", "-o", str(target), str(source))


def build(target: Path, *, run: Callable[..., subprocess.CompletedProcess] = subprocess.run,
          which: Callable[[str], str | None] = shutil.which, platform: str = sys.platform,
          source: Path = SOURCE) -> tuple[Path | None, str]:
    """Build the library at `target` (its folder is made): (the path, "") or (None, why it was not built). Never raises:
    a smoke without the guard is still a smoke."""
    if not supported(platform):
        return None, f"the focus guard is macOS only (this is {platform})"
    if not source.is_file():
        return None, f"no guard source at {source}"
    clang = which("clang")
    if clang is None:
        return None, "no clang (install the Xcode command line tools)"
    try:
        target.parent.mkdir(parents=True, exist_ok=True)
        done = run(compile_command(clang, target, source), capture_output=True, text=True, timeout=BUILD_SECONDS)
        if done.returncode == 0:
            signed = run((which("codesign") or "codesign", "-s", "-", "-f", str(target)), capture_output=True, text=True,
                         timeout=BUILD_SECONDS)
            if signed.returncode == 0:
                return target, ""
            done = signed
    except (OSError, subprocess.SubprocessError) as error:
        return None, f"the focus guard build failed: {error}"
    detail = (done.stderr or done.stdout or "").strip().splitlines()
    return None, "the focus guard build failed: " + (detail[0] if detail else f"exit {done.returncode}")


def insert_arguments(library: str | None) -> tuple[str, ...]:
    """The `env` argument that loads the library into the Godot process (nothing when there is none)."""
    return (f"{INSERT}={library}",) if library else ()


def carry(environ: dict[str, str], library: Path | None) -> dict[str, str]:
    """The planned environment with the library path under CARRIER, for godot_command to read."""
    values = {key: value for key, value in environ.items() if key != CARRIER}  # an owner's value never counts
    return values if library is None else {**values, CARRIER: str(library)}


def reads(environ: Mapping[str, str]) -> str | None:
    return environ.get(CARRIER) or None
