"""ARC-DEV-047: the packaged alpha build for a player (`./arcanum package mac`).

Builds the game (the terrain viewer project, which starts at the launcher and the original main menu without inputs) from
one commit of origin/main, never from the working tree: `git fetch origin main`, `git archive` of that commit into a fresh
temporary folder, a locked restore and build, a Godot import and a release export with the package preset (its own player
name and user folder, ad-hoc signature), then checks of the bundle and, when the game folder is found, a native smoke of
the launcher and the main menu on the exported app. Only a build that passed every step replaces
`artifacts/packages/<platform>/` in the main checkout; the build before it is kept in `previous/`. The developer build
registry (artifacts/world-builds) and the checkout's own files are not touched. Docs: docs/workflow/packaging.md.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
from typing import Callable, Sequence

ROOT = Path(__file__).resolve().parents[2]
PROJECT = Path("tools/terrain-viewer/Godot")
SOLUTION = PROJECT / "Arcanum.TerrainViewer.sln"
GODOT = Path("/Applications/Godot_mono.app/Contents/MacOS/Godot")
SDK_VERSION = "8.0.425"
FEATURE = "steamwoven_package"
PLAYER_NAME = "Arcanum Steamwoven"
USER_FOLDER = "Arcanum Steamwoven Game"
INSTRUCTION = Path("docs/player/alpha-build.md")
SMOKE_PASS = ("ARC-UI-014 native-smoke launcher PASS", "ARC-UI-011 native-smoke main menu PASS")
SMOKE_MINUTES = 10.0
DROPPED_PREFIXES = ("ARC_", "ARCANUM_", "DAT_ROOT", "LAUNCHER_SETTINGS_", "WORLD_DIAGNOSTIC_")
BOTTLES = 32  # GameFolderLocator.MaxBottles
KEPT_VARIABLES = frozenset({"ARCANUM_QUEUE_TICKET", "ARCANUM_QUEUE_KIND"})


@dataclass(frozen=True)
class Platform:
    key: str
    preset: str
    folder: str
    product: str
    executable: str  # inside the product


PLATFORMS = {
    "mac": Platform("mac", "macOS package", "macos", f"{PLAYER_NAME}.app", f"Contents/MacOS/{PLAYER_NAME}"),
}


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


def build_environment(base: dict[str, str], commit: str, date: str) -> dict[str, str]:
    """The canonical toolchain (docs/workflow/toolchain.md: the pinned SDK first, the Godot shell sentinel) and the
    commit stamp the viewer project reads as MSBuild properties (ViewerBuildInfo)."""
    sdk = dotnet_root(base)
    # The owner's opt-in data, smoke and settings variables (DAT_ROOT, ARC_*, ARCANUM_* HD and data selections,
    # LAUNCHER_SETTINGS_TEST_PATH, WORLD_DIAGNOSTIC_*) must not reach the build or its smoke; the queue's own stay.
    environment = {key: value for key, value in base.items()
                   if key in KEPT_VARIABLES or not key.startswith(DROPPED_PREFIXES)}
    environment.update({"DOTNET_ROOT": str(sdk), "PATH": os.pathsep.join([str(sdk), "/usr/bin", "/bin", "/usr/sbin", "/sbin"]),
                        "__GODOT_SHELL_ENV_SET": "1", "ArcanumPackageCommit": commit, "ArcanumPackageDate": date})
    return environment


def steps(platform: Platform, source: Path, product: Path, environment: dict[str, str]) -> list[tuple[str, list[str]]]:
    """The build steps: (log name, command), run in `source`."""
    dotnet = str(dotnet_root(environment) / "dotnet")
    project = str(source / PROJECT)
    return [
        ("restore", [dotnet, "restore", str(source / SOLUTION), "--locked-mode"]),
        ("build", [dotnet, "build", str(source / SOLUTION), "--no-restore"]),
        ("import", [str(GODOT), "--headless", "--path", project, "--import"]),
        ("export", [str(GODOT), "--verbose", "--headless", "--path", project, "--export-release", platform.preset, str(product)]),
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


def check_smoke_log(text: str, commit: str) -> None:
    """The exported app started as the packaged build of `commit`, in the player's own user folder, and passed the menu."""
    marker = f"ARC-DEV-047 build {PLAYER_NAME} alpha · main {commit[:10]}"
    if marker not in text:
        raise PackageError(f"the app did not report itself as the package of {commit[:10]}")
    line = next(line for line in text.splitlines() if marker in line)
    folder = line.split(" user=", 1)[1].rstrip("/\\") if " user=" in line else ""
    if Path(folder).name != USER_FOLDER:
        raise PackageError(f"the app's user folder is {folder!r}, not its own {USER_FOLDER!r}")
    for needed in SMOKE_PASS:
        if needed not in text:
            raise PackageError(f"the native smoke did not print {needed!r}")


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


def install(built: Path, destination: Path, commit: str, date: str, smoke: str, instruction: Path) -> Path:
    """Replace destination/<product> by `built`; the build it replaces moves to destination/previous/ with its BUILD.txt and
    README.md. The new build is first moved next to its place (the copy across volumes, the step that can fail on a full
    disk), so the current build is rotated only once the new one is complete on the same volume."""
    destination.mkdir(parents=True, exist_ok=True)
    incoming = destination / f".incoming-{built.name}"
    if incoming.exists():
        shutil.rmtree(incoming)
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
    (destination / "BUILD.txt").write_text(f"commit {commit}\ndate {date}\nproduct {built.name}\nsmoke {smoke}\n", encoding="utf-8")
    if instruction.is_file():
        shutil.copyfile(instruction, destination / "README.md")
    return current


def package(platform: Platform, *, root: Path, output: Path, ref: str, fetch: bool, smoke: bool, keep_work: bool,
            runner: Runner = run_logged, base: dict[str, str] | None = None) -> Path:
    if fetch:
        say("fetch origin main")
        git(root, "fetch", "origin", "main")
    commit, date = resolve(root, ref)
    say(f"commit {commit} ({date}) from {ref}")
    environment = build_environment(dict(os.environ if base is None else base), commit, date)
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
        product = work / "out" / platform.product
        product.parent.mkdir()
        for name, command in steps(platform, source, product, environment):
            text = step(name, command, source, environment, logs, runner)
            if name == "export":
                check_export_log(text, environment)
        executable = product / platform.executable
        if not os.access(executable, os.X_OK):
            raise PackageError(f"the export has no executable {platform.executable}")
        if platform.key == "mac":
            step("codesign-verify", ["/usr/bin/codesign", "--verify", "--deep", "--strict", "--verbose=2", str(product)],
                 source, environment, logs, runner)
            details = step("codesign-details", ["/usr/bin/codesign", "-d", "--verbose=2", str(product)], source, environment, logs, runner)
            if "Signature=adhoc" not in details:
                raise PackageError("the bundle is not ad-hoc signed")
        folder = game_folder() if smoke else None
        if folder is None:
            smoked = "SKIPPED (" + ("--no-smoke" if not smoke else "no game in /Applications/Arcanum, ~/Applications/Arcanum or a CrossOver bottle") + ")"
            say(f"smoke {smoked}")
        else:
            # An automated run keeps its settings and saves in an isolated folder (ViewerStoragePaths); the launcher file is
            # a fresh one, so the launcher detects the game folder as a player's first start would.
            smoke_environment = dict(environment, LAUNCHER_SETTINGS_TEST_PATH=str(work / "launcher_v1.json"), ARC_AREA_MAP_SMOKE_ONLY="menu")
            text = step("smoke-menu", [str(executable), "--", "--native-smoke"], source, smoke_environment, logs, runner, SMOKE_MINUTES)
            check_smoke_log(text, commit)
            smoked = f"PASS (launcher and main menu, own user folder {USER_FOLDER})"
            say(f"smoke {smoked}")
        try:
            installed = install(product, output / platform.folder, commit, date, smoked, source / INSTRUCTION)
        except OSError as error:
            raise PackageError(f"could not install into {output / platform.folder}: {error}") from error
        say(f"PASS {installed} (main {commit[:10]}, {date}, smoke {smoked.split(' ', 1)[0]})")
        return installed
    finally:
        if keep_work:
            say(f"work kept at {work}")
        else:
            shutil.rmtree(work, ignore_errors=True)


def main_checkout(root: Path = ROOT) -> Path:
    return Path(git(root, "rev-parse", "--path-format=absolute", "--git-common-dir")).parent


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(prog="package", description="Build the packaged alpha from a commit of origin/main.")
    result.add_argument("platform", choices=sorted(PLATFORMS))
    result.add_argument("--ref", default="origin/main", help="the commit to package (default origin/main, after a fetch)")
    result.add_argument("--no-fetch", action="store_true", help="do not fetch origin main first")
    result.add_argument("--no-smoke", action="store_true", help="skip the launcher and main menu smoke on the exported app")
    result.add_argument("--keep-work", action="store_true", help="keep the temporary source and export folder")
    result.add_argument("--output", type=Path, help="default: artifacts/packages in the main checkout")
    return result


def main(argv: Sequence[str] | None = None) -> int:
    args = parser().parse_args(argv)

    def stopped(number: int, _frame: object) -> None:
        raise PackageError(f"stopped by signal {number}")
    # A queue timeout or stop (SIGTERM, SIGHUP) unwinds like an error, so the temporary folder is removed.
    for number in (signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, stopped)
    try:
        root = main_checkout()
        output = args.output or root / "artifacts" / "packages"
        package(PLATFORMS[args.platform], root=root, output=output, ref=args.ref, fetch=not args.no_fetch,
                smoke=not args.no_smoke, keep_work=args.keep_work)
    except PackageError as error:
        print(f"package: FAIL {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
