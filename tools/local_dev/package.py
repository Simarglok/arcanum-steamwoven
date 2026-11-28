"""ARC-DEV-047/ARC-DEV-048: the packaged alpha build for a player (`./arcanum package mac|win`).

Builds the game (the terrain viewer project, which starts at the launcher and the original main menu without inputs) from
one commit of origin/main, never from the working tree: `git fetch origin main`, `git archive` of that commit into a fresh
temporary folder, a locked restore and build, a Godot import and a release export with the package preset (its own player
name and user folder, ad-hoc signature), then checks of the bundle and, when the game folder is found, a native smoke of
the launcher and the main menu on the exported app (macOS; the Windows build cannot start on the Mac, so it gets file checks
and is zipped). Only a build that passed every step replaces
`artifacts/packages/<platform>/` in the main checkout; the build before it is kept in `previous/`. The developer build
registry (artifacts/world-builds) and the checkout's own files are not touched. Docs: docs/workflow/packaging.md.

ARC-DEV-3379: `package mac --profiling` builds the profiling package for the owner's own measurements, never distributed:
the same commit and the same precompiled game code, exported with the debug template (so the Godot editor's remote
debugger can attach) and the get-task-allow entitlement (so Instruments needs no password), under its own app name,
bundle id and user folder, into `artifacts/packages/macos-profiling/`. The player package is not touched.
ARC-DEV-3478: no other package run rebuilds it, so a player package run (and `./arcanum daily`) ends with a WARNING and the
rebuild command when the installed profiling package is behind the commit it packaged.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import os
from pathlib import Path
import re
import shutil
import signal
import struct
import subprocess
import sys
import tempfile
from typing import Callable, Sequence
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PROJECT = Path("tools/terrain-viewer/Godot")
SOLUTION = PROJECT / "Arcanum.TerrainViewer.sln"
GODOT = Path("/Applications/Godot_mono.app/Contents/MacOS/Godot")
SDK_VERSION = "8.0.425"
FEATURE = "steamwoven_package"
PLAYER_NAME = "Arcanum Steamwoven"
USER_FOLDER = "Arcanum Steamwoven Game"
INSTRUCTION = Path("docs/player/alpha-build.md")
# ARC-DEV-3457: the one committed app icon, and the name it takes inside the extracted viewer project, where the
# package feature's config/icon names it (res:// cannot reach assets/); Godot converts it to the bundle's and the .exe's icon.
APP_ICON = Path("assets/app-icon/steamwoven-icon-1024.png")
PROJECT_ICON = "app-icon.png"
ICON_SETTING = f'config/icon.{FEATURE}="res://{PROJECT_ICON}"'
# ARC-DEV-3379: the profiling package's own app name and user folder (the same words) and the page installed beside it.
PROFILING_NAME = "Arcanum Steamwoven Profiling"
PROFILING_INSTRUCTION = Path("docs/workflow/mac-performance-monitoring.md")
DEBUG_ENTITLEMENT = "com.apple.security.get-task-allow"
SMOKE_PASS = ("ARC-UI-014 native-smoke launcher PASS", "ARC-UI-011 native-smoke main menu PASS")
SMOKE_MINUTES = 10.0
DROPPED_PREFIXES = ("ARC_", "ARCANUM_", "DAT_ROOT", "LAUNCHER_SETTINGS_", "WORLD_DIAGNOSTIC_")
BOTTLES = 32  # GameFolderLocator.MaxBottles
KEPT_VARIABLES = frozenset({"ARCANUM_QUEUE_TICKET", "ARCANUM_QUEUE_KIND"})
# ARC-DEV-062: BUILD.txt's map-smoke line when the full map smoke chain did not run before this package (`./arcanum daily`
# runs it and passes its result with --map-smoke).
MAP_SMOKE_NOT_RUN = "NOT RUN (./arcanum package alone; ./arcanum daily runs the full map smoke chain first)"


@dataclass(frozen=True)
class Platform:
    key: str
    preset: str
    folder: str
    product: str  # what the export writes: the .app bundle, or the folder of the .exe
    executable: str  # inside the product
    installed: str  # what lands in artifacts/packages/<folder>: the bundle itself, or the zip of the folder
    export: str = "--export-release"
    user_folder: str = USER_FOLDER
    instruction: Path = INSTRUCTION
    profiling: bool = False

    def export_target(self, product: Path) -> Path:
        """The path given to the export: the bundle on macOS, the .exe (beside it the .pck and data folder) on Windows."""
        return product if self.key == "mac" else product / self.executable


PLATFORMS = {
    "mac": Platform("mac", "macOS package", "macos", f"{PLAYER_NAME}.app", f"Contents/MacOS/{PLAYER_NAME}", f"{PLAYER_NAME}.app"),
    "win": Platform("win", "Windows package", "windows", PLAYER_NAME, f"{PLAYER_NAME}.exe", f"{PLAYER_NAME} Windows x64.zip"),
}
# Not in PLATFORMS: it is not a platform of the command (`mac --profiling` selects it), and `daily` never builds it.
PROFILING = Platform("mac", "macOS profiling", "macos-profiling", f"{PROFILING_NAME}.app", f"Contents/MacOS/{PROFILING_NAME}",
                     f"{PROFILING_NAME}.app", export="--export-debug", user_folder=PROFILING_NAME,
                     instruction=PROFILING_INSTRUCTION, profiling=True)
WINDOWS_ASSEMBLIES = ("Arcanum.TerrainViewer.dll", "GodotSharp.dll", "coreclr.dll", "hostfxr.dll")


class PackageError(Exception):
    pass


def say(message: str) -> None:
    print(f"package: {message}", flush=True)


def git(root: Path, *arguments: str) -> str:
    try:
        return subprocess.run(["git", "-C", str(root), *arguments], check=True, capture_output=True, text=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError) as error:
        raise PackageError(f"git {' '.join(arguments)} failed: {getattr(error, 'stderr', '') or error}".strip()) from error


def resolve(root: Path, ref: str) -> tuple[str, str]:
    """The full commit of `ref` and its commit date (YYYY-MM-DD)."""
    commit = git(root, "rev-parse", "--verify", f"{ref}^{{commit}}")
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise PackageError(f"{ref} does not name a commit")
    return commit, git(root, "show", "-s", "--format=%cs", commit)


def dotnet_root(environment: dict[str, str]) -> Path:
    return Path(environment.get("DOTNET_ROOT") or Path.home() / ".local" / "share" / "dotnet")


def build_environment(base: dict[str, str], commit: str, date: str, profiling: bool = False) -> dict[str, str]:
    """The canonical toolchain (docs/workflow/toolchain.md: the pinned SDK first, the Godot shell sentinel) and the
    commit stamp the viewer project reads as MSBuild properties (ViewerBuildInfo)."""
    sdk = dotnet_root(base)
    # The owner's opt-in data, smoke and settings variables (DAT_ROOT, ARC_*, ARCANUM_* HD and data selections,
    # LAUNCHER_SETTINGS_TEST_PATH, WORLD_DIAGNOSTIC_*) must not reach the build or its smoke; the queue's own stay.
    environment = {key: value for key, value in base.items()
                   if key in KEPT_VARIABLES or not key.startswith(DROPPED_PREFIXES)}
    environment.update({"DOTNET_ROOT": str(sdk), "PATH": os.pathsep.join([str(sdk), "/usr/bin", "/bin", "/usr/sbin", "/sbin"]),
                        "__GODOT_SHELL_ENV_SET": "1", "ArcanumPackageCommit": commit, "ArcanumPackageDate": date})
    # MSBuild reads it as a property: the debug export of the profiling package compiles the game as the release export does
    # (Directory.Build.props, the viewer project). Never taken from the caller's shell.
    environment.pop("ArcanumProfiling", None)
    if profiling:
        environment["ArcanumProfiling"] = "true"
    return environment


def steps(platform: Platform, source: Path, product: Path, environment: dict[str, str]) -> list[tuple[str, list[str]]]:
    """The build steps: (log name, command), run in `source`."""
    dotnet = str(dotnet_root(environment) / "dotnet")
    project = str(source / PROJECT)
    target = platform.export_target(product)
    return [
        ("restore", [dotnet, "restore", str(source / SOLUTION), "--locked-mode"]),
        ("build", [dotnet, "build", str(source / SOLUTION), "--no-restore"]),
        ("import", [str(GODOT), "--headless", "--path", project, "--import"]),
        ("export", [str(GODOT), "--verbose", "--headless", "--path", project, platform.export, platform.preset, str(target)]),
    ]


def check_tools(environment: dict[str, str]) -> None:
    """The canonical SDK and Godot exist before anything runs (as validate-macos.sh checks)."""
    for tool in (dotnet_root(environment) / "dotnet", GODOT):
        if not os.access(tool, os.X_OK):
            raise PackageError(f"{tool} is missing: the canonical SDK {SDK_VERSION} and Godot 4.7.2 .NET are needed (toolchain.md)")


def check_export_log(text: str, environment: dict[str, str]) -> None:
    """The editor's build bridge used the pinned SDK (toolchain.md: a direct build alone does not prove it)."""
    if str(dotnet_root(environment) / "dotnet") not in text or SDK_VERSION not in text:
        raise PackageError(f"the export did not use the pinned SDK {SDK_VERSION} at {dotnet_root(environment)}")


def rename_for_profiling(source: Path) -> None:
    """The profiling package's own app name and user folder, written into the extracted project.godot (not the checkout's):
    both presets share the package feature, so the game behaves as the player's; only these two settings differ."""
    project = source / PROJECT / "project.godot"
    try:
        text = project.read_text(encoding="utf-8")
    except OSError as error:
        raise PackageError(f"the commit has no readable {PROJECT / 'project.godot'}") from error
    for setting, name in (("config/name", PLAYER_NAME), ("config/custom_user_dir_name", USER_FOLDER)):
        line = f'{setting}.{FEATURE}="{name}"\n'
        if text.count(line) != 1:
            raise PackageError(f"project.godot does not set {line.strip()} exactly once: the profiling package cannot take its own name")
        text = text.replace(line, f'{setting}.{FEATURE}="{PROFILING_NAME}"\n')
    project.write_text(text, encoding="utf-8")


def place_app_icon(source: Path) -> None:
    """The commit's app icon, copied into the extracted viewer project (never into the checkout). A commit whose
    project.godot names no package icon is older than it and keeps Godot's."""
    project = source / PROJECT / "project.godot"
    if not project.is_file() or ICON_SETTING not in project.read_text(encoding="utf-8").splitlines():
        return
    icon = source / APP_ICON
    if not icon.is_file():
        raise PackageError(f"project.godot names the app icon, but the commit has no {APP_ICON}")
    shutil.copyfile(icon, source / PROJECT / PROJECT_ICON)


def check_smoke_log(text: str, commit: str, user_folder: str = USER_FOLDER) -> None:
    """The exported app started as the packaged build of `commit`, in its own user folder, and passed the menu."""
    marker = f"ARC-DEV-047 build {PLAYER_NAME} alpha · main {commit[:10]}"
    if marker not in text:
        raise PackageError(f"the app did not report itself as the package of {commit[:10]}")
    line = next(line for line in text.splitlines() if marker in line)
    folder = line.split(" user=", 1)[1].rstrip("/\\") if " user=" in line else ""
    if Path(folder).name != user_folder:
        raise PackageError(f"the app's user folder is {folder!r}, not its own {user_folder!r}")
    for needed in SMOKE_PASS:
        if needed not in text:
            raise PackageError(f"the native smoke did not print {needed!r}")


def check_windows(product: Path) -> None:
    """The Windows export, which cannot start on the Mac, by its files: an x86-64 GUI executable carrying the player name in
    its version resource, the .pck of the package feature beside it and the .NET data folder with the game's assemblies."""
    executable = product / f"{PLAYER_NAME}.exe"
    try:
        head = executable.read_bytes()
    except OSError as error:
        raise PackageError(f"the export has no {executable.name}") from error
    offset = struct.unpack_from("<I", head, 0x3C)[0] if len(head) >= 0x40 and head[:2] == b"MZ" else -1
    if offset < 0 or len(head) < offset + 24 + 70 or head[offset:offset + 4] != b"PE\0\0":
        raise PackageError(f"{executable.name} is not a Windows executable")
    machine = struct.unpack_from("<H", head, offset + 4)[0]
    magic, subsystem = struct.unpack_from("<H", head, offset + 24)[0], struct.unpack_from("<H", head, offset + 24 + 68)[0]
    if machine != 0x8664 or magic != 0x20B or subsystem != 2:
        raise PackageError(f"{executable.name} is not an x86-64 GUI executable (machine {machine:#x}, subsystem {subsystem})")
    # The ProductName entry of the version resource: the key, its terminator and up to one padding word, then the value.
    version = head.find("VS_VERSION_INFO".encode("utf-16-le"))
    entry = re.escape("ProductName".encode("utf-16-le")) + b"\0\0(?:\0\0)?" + re.escape(PLAYER_NAME.encode("utf-16-le")) + b"\0\0"
    if version < 0 or not re.search(entry, head[version:version + 4096]):
        raise PackageError(f"{executable.name} does not carry the product name {PLAYER_NAME!r} (modify_resources)")
    pck = product / f"{PLAYER_NAME}.pck"
    pack = pck.read_bytes() if pck.is_file() else b""
    if pack[:4] != b"GDPC":
        raise PackageError(f"the export has no Godot pack {pck.name}")
    if FEATURE.encode() not in pack:
        raise PackageError(f"{pck.name} was not exported with the feature {FEATURE}")
    data = [path for path in product.glob("data_*_windows_x86_64") if path.is_dir()]
    if len(data) != 1:
        raise PackageError(f"the export has {len(data)} .NET data folders, not one data_*_windows_x86_64")
    missing = [name for name in WINDOWS_ASSEMBLIES if not any(data[0].rglob(name))]
    if missing:
        raise PackageError(f"{data[0].name} lacks {', '.join(missing)}")


def zip_folder(folder: Path, archive: Path) -> None:
    """The folder as one zip (its name the zip's top folder), the file a player copies to the Windows PC."""
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as handle:
        for path in sorted(folder.rglob("*")):
            handle.write(path, Path(folder.name) / path.relative_to(folder))


Runner = Callable[[Sequence[str], Path, dict[str, str], Path, float | None], int]


def run_logged(command: Sequence[str], cwd: Path, environment: dict[str, str], log: Path, minutes: float | None) -> int:
    with log.open("wb") as handle:
        try:
            return subprocess.run(list(command), cwd=cwd, env=environment, stdout=handle, stderr=subprocess.STDOUT,
                                  timeout=None if minutes is None else minutes * 60).returncode
        except subprocess.TimeoutExpired:
            handle.write(f"\npackage: stopped after {minutes:g} minutes\n".encode())
            return 124
        except OSError as error:
            handle.write(f"package: could not start {command[0]}: {error}\n".encode())
            return 127


def step(name: str, command: Sequence[str], cwd: Path, environment: dict[str, str], logs: Path, runner: Runner,
         minutes: float | None = None) -> str:
    say(f"step {name}")
    log = logs / f"{name}.log"
    code = runner(command, cwd, environment, log, minutes)
    if code != 0:
        tail = log.read_text(encoding="utf-8", errors="replace").splitlines()[-20:] if log.is_file() else []
        raise PackageError("\n".join([f"step {name} FAILED exit={code} log={log}", *tail]))
    return log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""


def extract(root: Path, commit: str, source: Path) -> None:
    """The commit's tracked files only (git archive), so nothing untracked or uncommitted can enter a package."""
    source.mkdir(parents=True)
    try:
        archive = subprocess.Popen(["git", "-C", str(root), "archive", "--format=tar", commit], stdout=subprocess.PIPE)
        unpack = subprocess.run(["tar", "-x", "-f", "-", "-C", str(source)], stdin=archive.stdout)
    except OSError as error:
        raise PackageError(f"could not extract {commit[:10]}: {error}") from error
    assert archive.stdout is not None
    archive.stdout.close()
    if archive.wait() != 0 or unpack.returncode != 0:
        raise PackageError(f"could not extract {commit[:10]}")


def game_folder() -> Path | None:
    """A folder the smoke's first start detects (GameFolderLocator's macOS candidates: the fixed folders, then the GOG
    folders in CrossOver bottles), or None to skip the smoke."""
    bottles = Path.home() / "Library" / "Application Support" / "CrossOver" / "Bottles"
    crossover = [bottle / "drive_c" / Path(*folder) for bottle in (sorted(bottles.iterdir())[:BOTTLES] if bottles.is_dir() else [])
                 for folder in (("GOG Games", "Arcanum"), ("Program Files (x86)", "GOG Galaxy", "Games", "Arcanum"))]
    for candidate in (Path("/Applications/Arcanum"), Path.home() / "Applications" / "Arcanum", *crossover):
        if (candidate / "modules" / "Arcanum.dat").is_file():
            return candidate
    return None


def remove(path: Path) -> None:
    if path.is_dir() and not path.is_symlink():
        shutil.rmtree(path)
    else:
        path.unlink()


def install(built: Path, destination: Path, commit: str, date: str, smoke: str, instruction: Path,
            map_smoke: str = MAP_SMOKE_NOT_RUN, notes: Sequence[str] = ()) -> Path:
    """Replace destination/<product> (a bundle or a zip) by `built`; the build it replaces moves to destination/previous/ with its BUILD.txt and
    README.md. The new build is first moved next to its place (the copy across volumes, the step that can fail on a full
    disk), so the current build is rotated only once the new one is complete on the same volume."""
    destination.mkdir(parents=True, exist_ok=True)
    incoming = destination / f".incoming-{built.name}"
    if incoming.exists():
        remove(incoming)
    shutil.move(str(built), str(incoming))
    current = destination / built.name
    if current.exists():
        previous = destination / "previous"
        if previous.exists():
            shutil.rmtree(previous)
        previous.mkdir()
        current.rename(previous / built.name)
        for name in ("BUILD.txt", "README.md"):
            if (destination / name).is_file():
                (destination / name).rename(previous / name)
    incoming.rename(current)
    (destination / "BUILD.txt").write_text(f"commit {commit}\ndate {date}\nproduct {built.name}\nsmoke {smoke}\n"
                                           f"map-smoke {map_smoke}\n" + "".join(f"{note}\n" for note in notes), encoding="utf-8")
    if instruction.is_file():
        shutil.copyfile(instruction, destination / "README.md")
    return current


def package(platform: Platform, *, root: Path, output: Path, ref: str, fetch: bool, smoke: bool, keep_work: bool,
            runner: Runner = run_logged, base: dict[str, str] | None = None, map_smoke: str = MAP_SMOKE_NOT_RUN) -> Path:
    if not map_smoke.strip() or any(character in map_smoke for character in "\r\n"):
        raise PackageError("--map-smoke takes one line of text")
    if fetch:
        say("fetch origin main")
        git(root, "fetch", "origin", "main")
    commit, date = resolve(root, ref)
    say(f"commit {commit} ({date}) from {ref}")
    environment = build_environment(dict(os.environ if base is None else base), commit, date, platform.profiling)
    if runner is run_logged:
        check_tools(environment)
    output.mkdir(parents=True, exist_ok=True)
    logs = output / "logs" / platform.folder
    if logs.exists():
        shutil.rmtree(logs)
    logs.mkdir(parents=True)
    # Resolved: on macOS the temporary folder is under the /var -> /private/var link, and a locked restore given the link path
    # compares the referenced projects' lock entries by their resolved paths and refuses (NU1004 "Current dependencies: None").
    work = Path(tempfile.mkdtemp(prefix="arcanum-package-")).resolve()
    say(f"work {work} logs {logs}")
    try:
        source = work / "source"
        extract(root, commit, source)
        if platform.profiling:
            # A commit from before the profiling package has no preset for it, and its debug export would be unoptimized.
            presets = source / PROJECT / "export_presets.cfg"
            if not presets.is_file() or f'name="{platform.preset}"' not in presets.read_text(encoding="utf-8"):
                raise PackageError(f"{commit[:10]} has no export preset {platform.preset!r}: it is older than the profiling package")
            rename_for_profiling(source)
        place_app_icon(source)
        product = work / "out" / platform.product
        platform.export_target(product).parent.mkdir(parents=True)  # Godot refuses a Windows export path whose folder is missing
        for name, command in steps(platform, source, product, environment):
            text = step(name, command, source, environment, logs, runner)
            if name == "export":
                check_export_log(text, environment)
        executable = product / platform.executable
        if platform.key == "win":
            check_windows(product)
        elif not os.access(executable, os.X_OK):
            raise PackageError(f"the export has no executable {platform.executable}")
        if platform.key == "mac":
            step("codesign-verify", ["/usr/bin/codesign", "--verify", "--deep", "--strict", "--verbose=2", str(product)],
                 source, environment, logs, runner)
            details = step("codesign-details", ["/usr/bin/codesign", "-d", "--verbose=2", str(product)], source, environment, logs, runner)
            if "Signature=adhoc" not in details:
                raise PackageError("the bundle is not ad-hoc signed")
            if platform.profiling:
                entitlements = step("codesign-entitlements", ["/usr/bin/codesign", "-d", "--entitlements", "-", "--xml", str(product)],
                                    source, environment, logs, runner)
                if not re.search(f"<key>{re.escape(DEBUG_ENTITLEMENT)}</key>\\s*<true/>", entitlements):
                    raise PackageError(f"the bundle lacks the entitlement {DEBUG_ENTITLEMENT}")
        folder = game_folder() if smoke and platform.key == "mac" else None
        if platform.key == "win":
            smoked = "NOT RUN (a Windows build cannot start on the Mac: file checks only)"
            say(f"smoke {smoked}")
        elif folder is None:
            smoked = "SKIPPED (" + ("--no-smoke" if not smoke else "no game in /Applications/Arcanum, ~/Applications/Arcanum or a CrossOver bottle") + ")"
            say(f"smoke {smoked}")
        else:
            # An automated run keeps its settings and saves in an isolated folder (ViewerStoragePaths); the launcher file is
            # a fresh one, so the launcher detects the game folder as a player's first start would.
            smoke_environment = dict(environment, LAUNCHER_SETTINGS_TEST_PATH=str(work / "launcher.cfg"), ARC_AREA_MAP_SMOKE_ONLY="menu")
            text = step("smoke-menu", [str(executable), "--", "--native-smoke"], source, smoke_environment, logs, runner, SMOKE_MINUTES)
            check_smoke_log(text, commit, platform.user_folder)
            smoked = f"PASS (launcher and main menu, own user folder {platform.user_folder})"
            say(f"smoke {smoked}")
        if platform.installed != platform.product:
            say(f"zip {platform.installed}")
            built = product.parent / platform.installed
            zip_folder(product, built)
            product = built
        try:
            notes = (["profiling debug export with get-task-allow: for this Mac's own measurements, never distributed"]
                     if platform.profiling else [])
            installed = install(product, output / platform.folder, commit, date, smoked, source / platform.instruction, map_smoke, notes)
        except OSError as error:
            raise PackageError(f"could not install into {output / platform.folder}: {error}") from error
        say(f"PASS {installed} (main {commit[:10]}, {date}, smoke {smoked.split(' (', 1)[0]})")
        return installed
    finally:
        if keep_work:
            say(f"work kept at {work}")
        else:
            shutil.rmtree(work, ignore_errors=True)


def main_checkout(root: Path = ROOT) -> Path:
    return Path(git(root, "rev-parse", "--path-format=absolute", "--git-common-dir")).parent


def built_commit(folder: Path) -> str | None:
    """The commit in a package folder's BUILD.txt, or None when there is no readable one."""
    try:
        text = (folder / "BUILD.txt").read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None
    found = re.search(r"^commit ([0-9a-f]{40})$", text, re.MULTILINE)
    return found.group(1) if found else None


def stale_profiling(root: Path, output: Path, commit: str) -> str | None:
    """ARC-DEV-3478: what a package run of `commit` says when the installed profiling package holds neither that commit
    nor a later one that contains it; None when it does, or when none is installed."""
    folder = output / PROFILING.folder
    if not (folder / PROFILING.installed).is_dir():
        return None
    held = built_commit(folder)
    if held == commit:
        return None
    if held is not None:
        try:
            git(root, "merge-base", "--is-ancestor", commit, held)
            return None  # it is newer than the packaged commit
        except PackageError:
            pass  # older, another branch's, or a commit this repository no longer has
    return (f"WARNING the profiling package is out of date: it holds {f'main {held[:10]}' if held else 'an unknown commit'}, "
            f"not the packaged {commit[:10]}; before measuring rebuild it: ./arcanum package mac --profiling")


def stale_profiling_after(checkout: Path, platform: Platform) -> str | None:
    """`stale_profiling` after a player package run from `checkout`, for the commit that run installed (its BUILD.txt)."""
    try:
        root = main_checkout(checkout)
        output = root / "artifacts" / "packages"
        commit = built_commit(output / platform.folder)
        return stale_profiling(root, output, commit) if commit else None
    except (OSError, PackageError):  # a note only: it never turns an installed package into a failed run
        return None


PROFILING_HELP = ("mac only (ARC-DEV-3379): the profiling package for Instruments and the Godot remote debugger, a debug export "
                  "with get-task-allow under its own name and user folder, into packages/macos-profiling; never distribute it")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(prog="package", description="Build the packaged alpha from a commit of origin/main.")
    result.add_argument("platform", choices=sorted(PLATFORMS))
    result.add_argument("--profiling", action="store_true", help=PROFILING_HELP)
    result.add_argument("--ref", default="origin/main", help="the commit to package (default origin/main, after a fetch)")
    result.add_argument("--no-fetch", action="store_true", help="do not fetch origin main first")
    result.add_argument("--no-smoke", action="store_true", help="skip the launcher and main menu smoke on the exported app")
    result.add_argument("--keep-work", action="store_true", help="keep the temporary source and export folder")
    result.add_argument("--output", type=Path, help="default: artifacts/packages in the main checkout")
    result.add_argument("--map-smoke", default=MAP_SMOKE_NOT_RUN, metavar="TEXT",
                        help="the full map smoke chain's result for BUILD.txt's map-smoke line (ARC-DEV-062: ./arcanum daily)")
    return result


def main(argv: Sequence[str] | None = None) -> int:
    command = parser()
    args = command.parse_args(argv)
    if args.profiling and args.platform != "mac":
        command.error("--profiling is for mac only")

    def stopped(number: int, _frame: object) -> None:
        raise PackageError(f"stopped by signal {number}")
    # A queue timeout or stop (SIGTERM, SIGHUP) unwinds like an error, so the temporary folder is removed.
    for number in (signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, stopped)
    try:
        root = main_checkout()
        output = args.output or root / "artifacts" / "packages"
        package(PROFILING if args.profiling else PLATFORMS[args.platform], root=root, output=output, ref=args.ref, fetch=not args.no_fetch,
                smoke=not args.no_smoke, keep_work=args.keep_work, map_smoke=args.map_smoke)
    except PackageError as error:
        print(f"package: FAIL {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
