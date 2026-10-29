"""ARC-DEV-061: the shared smoke runner, `./arcanum smoke`, so threads stop writing their own scratch smoke scripts.

The rule is docs/workflow/toolchain.md#smoke-runner-arc-dev-061 and the scope docs/workflow/tasks.md#smoke-scope. A run
builds the viewer from this checkout (dotnet, through the launch queue), imports its project once when it was never
imported, then runs each smoke as its own quiet `window` run of the queue with a start check on the viewer's
`ARC-DEV-059 viewer start` line for the intended mode, map and smoke. Each smoke gets its own settings folder (the
viewer's *_TEST_PATH files) and an environment without the owner's opt-in variables (DAT_ROOT, ARC_*, ARCANUM_*, the
settings paths), so neither the owner's files nor another smoke's leak into it. A failed smoke does not stop the
chain: every step runs and the summary names each one; a failed build or import, or a stopped run, stops it.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
from typing import Callable, Mapping, Sequence

from . import config, launch, run_queue, start_check

ROOT = Path(__file__).resolve().parents[2]
VIEWER = Path("tools/terrain-viewer/Godot")
SOLUTION = VIEWER / "Arcanum.TerrainViewer.sln"
STAGES_SOURCE = VIEWER / "TerrainViewerAreaMapStream.cs"
DEFAULT_GODOT = "/Applications/Godot_mono.app/Contents/MacOS/Godot"
MAIN_MAP = "Arcanum1-024-fixed"
# The Crash Site smoke: the viewer's pinned Crash Site exterior regression (crash-site-area without --area-map), with the
# ARC-WORLD-009 cave input so it also walks into the cave and back; the hero is the one the lanes' runs used.
CRASH_SITE_TASK = "ARC-WORLD-009"
CRASH_SITE_HERO = "dwarf-male"
# The full map smoke chain (before every ./arcanum package and once a day on main): the Crash Site smoke, then the whole
# map battery (every stage the viewer runs without ARC_AREA_MAP_SMOKE_ONLY) on the main map at the Crash Site, at the town
# and at the generated wilderness sector the map battery is documented at, and on a whole interior map.
FULL_CHAIN: tuple[tuple[str, str, str, str | None], ...] = (
    ("main", MAIN_MAP, "human-male", None),
    ("town", MAIN_MAP, "human-male", "1409,1314"),
    ("wild", MAIN_MAP, "human-male", "1384,239"),
    # ARC-RENDER-036: the elf-female, as the elf-male's wielded boomerang (1-3 normal damage) never gets past a container's
    # damage threshold, so the junk pile stage could not break one on the ruins.
    ("ruins", "elven ruins", "elf-female", None),
)
# The viewer's settings files an automated run may be pointed at (ViewerStoragePaths and the stores that read them).
SETTINGS = ("ALWAYS_RUN", "AUDIO_VOLUMES", "GAME_SETTINGS", "LAUNCHER_SETTINGS", "VISUAL_PROFILE",
            "WORLD_DIAGNOSTIC_SETTINGS", "WORLD_DIAGNOSTIC_ZOOM")
STAGES_ENV = "ARC_AREA_MAP_SMOKE_ONLY"
# The owner's opt-in data, smoke and settings variables never reach a smoke (as in the player package); the queue's stay.
DROPPED_PREFIXES = ("ARC_", "ARCANUM_", "DAT_ROOT", "LAUNCHER_SETTINGS_", "WORLD_DIAGNOSTIC_", "ALWAYS_RUN_",
                    "AUDIO_VOLUMES_", "GAME_SETTINGS_", "VISUAL_PROFILE_")
KEPT_VARIABLES = frozenset({run_queue.TICKET_ENV, run_queue.KIND_ENV})
STOPPED = 128  # an exit at or above it is a stopped run (128+N), which ends the chain
NAME = re.compile(r"^[A-Z][A-Z0-9_]*$")
RESERVED = frozenset({"PATH", "DOTNET_ROOT", "HOME", "GODOT", "__GODOT_SHELL_ENV_SET"})  # the canonical start's own
BATTERY_MINUTES = 60.0  # a whole map battery (no stage list) runs about 2 minutes; the queue's window default is 30


@dataclass(frozen=True)
class Step:
    """One queued run of a smoke: its name in the summary, queue kind, command and start line (None: the default)."""
    name: str
    kind: str
    command: tuple[str, ...]
    expect: str | None = None
    gate: bool = False  # a failure stops the chain (the build, the import)
    settings: str | None = None  # the step's own settings folder under the run's folder
    timeout: float | None = None  # minutes; None: the queue's default for the kind


def map_stages() -> tuple[str, ...]:
    """The stage names ARC_AREA_MAP_SMOKE_ONLY accepts, read from the viewer (MapSmokeStageNames), so a typo fails here
    before its run waits for the queue."""
    text = (ROOT / STAGES_SOURCE).read_text(encoding="utf-8")
    found = re.search(r"MapSmokeStageNames\s*=\s*\[([^\]]*)\]", text)
    if found is None:
        raise ValueError(f"cannot read the map smoke stages from {STAGES_SOURCE}")
    return tuple(re.findall(r'"([^"]+)"', found.group(1)))


def parse_stages(value: str | None, known: Sequence[str]) -> tuple[str, ...]:
    if value is None:
        return ()
    stages = tuple(dict.fromkeys(item.strip() for item in value.split(",") if item.strip()))
    unknown = [stage for stage in stages if stage not in known]
    if not stages or unknown:
        raise ValueError("unknown map smoke stage " + ", ".join(unknown or [repr(value)]) + " (known: " + ", ".join(known) + ")")
    return stages


def parse_env(items: Sequence[str]) -> tuple[tuple[str, str], ...]:
    pairs = []
    for item in items:
        name, separator, value = item.partition("=")
        if not separator or not NAME.match(name) or name in RESERVED or name == STAGES_ENV or name.endswith("_TEST_PATH"):
            raise ValueError(f"--env takes NAME=VALUE with an upper-case NAME other than {STAGES_ENV}, the settings "
                             f"paths and {', '.join(sorted(RESERVED))}, got {item!r}")
        pairs.append((name, value))
    return tuple(pairs)


def start_line(map_name: str | None, main_menu: bool = False) -> str:
    """The viewer's ARC-DEV-059 start line for this smoke: the intended mode, map (- for the pinned exterior) and smoke."""
    return (f"^ARC-DEV-059 viewer start mode=crash-site-area map={re.escape(map_name or '-')} "
            f"main-menu={main_menu} smoke=True$")


def godot_command(godot: str, prefs: Path, viewer: Sequence[str], environ: Mapping[str, str], *,
                  stages: Sequence[str] = (), extra: Sequence[tuple[str, str]] = (), headless: bool = False) -> tuple[str, ...]:
    """`env` that drops the owner's variables and sets the smoke's own, then Godot on the source project
    (godot_canonical: the sentinel, the pinned SDK and a plain PATH)."""
    sdk = run_queue.pinned_sdk(dict(environ))
    dropped = sorted(key for key in environ if key.startswith(DROPPED_PREFIXES) and key not in KEPT_VARIABLES)
    settings = [f"{name}_TEST_PATH={prefs / (name.lower() + '.json')}" for name in SETTINGS]
    return ("env", *(part for key in dropped for part in ("-u", key)), "__GODOT_SHELL_ENV_SET=1", f"DOTNET_ROOT={sdk}",
            f"PATH={sdk}:/usr/bin:/bin:/usr/sbin:/sbin", *settings,
            *([f"{STAGES_ENV}={','.join(stages)}"] if stages else []), *(f"{name}={value}" for name, value in extra),
            godot, *(["--headless"] if headless else []), "--path", str(VIEWER), "--", *viewer)


def build_steps(environ: Mapping[str, str], godot: str, *, build: bool, imported: bool) -> list[Step]:
    steps = []
    if build:
        steps.append(Step("build", "dotnet", ("sh", "-c", f"dotnet restore {SOLUTION} --locked-mode -v q && "
                                                          f"dotnet build {SOLUTION} --no-restore -v q --nologo"), gate=True))
    if not imported:
        sdk = run_queue.pinned_sdk(dict(environ))
        steps.append(Step("import", "headless", ("env", "__GODOT_SHELL_ENV_SET=1", f"DOTNET_ROOT={sdk}",
                                                 f"PATH={sdk}:/usr/bin:/bin:/usr/sbin:/sbin", godot, "--headless",
                                                 "--path", str(VIEWER), "--import"), gate=True))
    return steps


def crash_site_step(inputs: dict, conf: dict, prefs_root: Path, environ: Mapping[str, str], godot: str, hero: str,
                    extra: Sequence[tuple[str, str]] = ()) -> Step:
    viewer = ["--native-smoke", *launch.terrain_arguments(inputs, "crash-site-area", task=CRASH_SITE_TASK, hero=hero,
                                                            verified=launch.verified_roles(conf))]
    return Step("crash-site", "window", godot_command(godot, prefs_root / "crash-site", viewer, environ, extra=extra),
                start_line(None), settings="crash-site")


def map_step(name: str, inputs: dict, prefs_root: Path, environ: Mapping[str, str], godot: str, map_name: str, *,
             hero: str | None, sector: str | None, stages: Sequence[str] = (), main_menu: bool = False,
             extra: Sequence[tuple[str, str]] = ()) -> Step:
    viewer = ["--native-smoke", *launch.map_arguments(inputs, map_name, hero=hero, sector=sector, main_menu=main_menu)]
    return Step(name, "window", godot_command(godot, prefs_root / name, viewer, environ, stages=stages, extra=extra),
                start_line(map_name, main_menu), settings=name, timeout=None if stages else BATTERY_MINUTES)


def plan(args: argparse.Namespace, conf: dict, prefs_root: Path, environ: Mapping[str, str], imported: bool) -> list[Step]:
    """Every step of this smoke run, in order."""
    godot = environ.get("GODOT") or DEFAULT_GODOT
    inputs = conf.get("inputs", {})
    extra = parse_env(getattr(args, "env", None) or ())
    steps = build_steps(environ, godot, build=not args.no_build, imported=imported)
    if args.target == "self-test":
        steps.append(Step("self-test", "headless" if args.headless else "window",
                          godot_command(godot, prefs_root / "self-test", ["--self-test"], environ, extra=extra,
                                        headless=args.headless), settings="self-test"))
        return steps
    if args.target == "full":
        steps.append(crash_site_step(inputs, conf, prefs_root, environ, godot, CRASH_SITE_HERO))
        steps.extend(map_step(name, inputs, prefs_root, environ, godot, map_name, hero=hero, sector=sector)
                     for name, map_name, hero, sector in FULL_CHAIN)
        return steps
    if args.target == "crash-site":
        steps.append(crash_site_step(inputs, conf, prefs_root, environ, godot, args.hero or CRASH_SITE_HERO, extra))
        return steps
    stages = parse_stages(args.stages, map_stages())
    if args.target == "pr":
        prepared = len(steps)
        if args.crash_site:
            steps.append(crash_site_step(inputs, conf, prefs_root, environ, godot, CRASH_SITE_HERO))
        if stages:
            steps.append(map_step("stages", inputs, prefs_root, environ, godot, args.map, hero=args.hero,
                                  sector=args.sector, stages=stages, main_menu=args.main_menu, extra=extra))
        elif args.map != MAIN_MAP or args.hero or args.sector or args.main_menu or extra:
            raise ValueError("--map, --hero, --sector, --main-menu and --env go with --stages (the touched stages)")
        if len(steps) == prepared:
            raise ValueError("nothing to run: give --stages (the touched stages) or --crash-site")
        return steps
    steps.append(map_step("map", inputs, prefs_root, environ, godot, args.name, hero=args.hero, sector=args.sector,
                          stages=stages, main_menu=args.main_menu, extra=extra))
    return steps


def shown(command: Sequence[str]) -> str:
    return " ".join(part if part and not re.search(r"[\s'\"\\$`]", part) else "'" + part.replace("'", "'\\''") + "'"
                    for part in command)


def execute(steps: Sequence[Step], runner: Callable[..., int] = run_queue.run,
            say: Callable[[str], None] = lambda line: print(line, flush=True)) -> int:
    """Run the steps through the queue (each quiet, start checked); 0 when every one passed."""
    results: list[tuple[str, int]] = []
    for step in steps:
        say(f"SMOKE step {step.name} kind={step.kind}")
        check = start_check.for_run(step.kind, step.expect, None, step.command)
        code = runner(step.kind, list(step.command), label=f"smoke {step.name}", timeout=step.timeout, quiet=True, start=check)
        results.append((step.name, code))
        if code >= STOPPED or (code != 0 and step.gate):
            break
    width = max(len(name) for name, _ in results) if results else 0
    say("SMOKE summary")
    for name, code in results:
        say(f"  {name:<{width}}  {'PASS' if code == 0 else 'FAIL'} exit={code}")
    skipped = [step.name for step in steps[len(results):]]
    if skipped:
        say("  not run: " + ", ".join(skipped))
    failed = [name for name, code in results if code != 0]
    say(f"SMOKE {'PASS' if not failed and not skipped else 'FAIL'} {len(results) - len(failed)} of {len(steps)} steps passed")
    stopped = [code for _, code in results if code >= STOPPED]
    return stopped[0] if stopped else (1 if failed or skipped else 0)


def run(args: argparse.Namespace) -> int:
    environ = dict(os.environ)
    if environ.get(run_queue.TICKET_ENV):
        raise ValueError("run ./arcanum smoke on its own, not inside ./arcanum run: each smoke takes its own place in "
                         "the queue and checks its own start")
    prefs_root = Path(tempfile.mkdtemp(prefix="arcanum-smoke-"))
    keep = False
    try:
        steps = plan(args, config.load_config(), prefs_root, environ, imported=(ROOT / VIEWER / ".godot").is_dir())
        if args.dry_run:
            for step in steps:
                print(f"{step.name} [{step.kind}] {shown(step.command)}")
                if step.expect:
                    print(f"  start line: {step.expect}")
            return 0
        godot = environ.get("GODOT") or DEFAULT_GODOT
        if not os.access(godot, os.X_OK):
            raise ValueError(f"no Godot at {godot} (set GODOT)")
        os.chdir(ROOT)  # the steps name the viewer project and solution relative to the checkout
        for step in steps:
            if step.settings:
                (prefs_root / step.settings).mkdir()
        print(f"SMOKE settings folder {prefs_root}", flush=True)
        code = execute(steps)
        keep = args.keep_settings or code != 0
        print(f"SMOKE settings folder {'kept' if keep else 'removed'}: {prefs_root}", flush=True)
        return code
    finally:
        if not keep:
            shutil.rmtree(prefs_root, ignore_errors=True)


def configure(command: argparse.ArgumentParser) -> None:
    """The `./arcanum smoke` arguments."""
    targets = command.add_subparsers(dest="target", required=True)

    def common(parser: argparse.ArgumentParser) -> None:
        parser.add_argument("--no-build", action="store_true", help="skip the viewer build (dotnet restore and build of "
                            "the viewer solution); only when this checkout's viewer is already built from its sources")
        parser.add_argument("--keep-settings", action="store_true", help="keep the smokes' settings folders (kept "
                            "anyway when a step failed)")
        parser.add_argument("--dry-run", action="store_true", help="print each step's command and start line; run nothing")

    def map_options(parser: argparse.ArgumentParser) -> None:
        parser.add_argument("--hero", choices=launch.PREMADE_HEROES, help="start as this pre-made hero")
        parser.add_argument("--sector", help="X,Y: open the map at this sector")
        parser.add_argument("--main-menu", action="store_true", help="open the main menu first (not with --hero)")
        parser.add_argument("--env", action="append", metavar="NAME=VALUE",
                            help="one more variable for the viewer (a capture or probe switch); repeatable")

    stages = "comma list of map smoke stages (ARC_AREA_MAP_SMOKE_ONLY; the viewer's MapSmokeStageNames)"
    pr = targets.add_parser("pr", help="the per-PR scope: only the map smoke stages the change touches or is directly "
                           "related to (--stages), and the Crash Site smoke only when related (--crash-site)")
    pr.add_argument("--stages", help=stages)
    pr.add_argument("--map", default=MAIN_MAP, help=f"the map the stages run on (default {MAIN_MAP})")
    map_options(pr)
    pr.add_argument("--crash-site", action="store_true", help="also the Crash Site smoke first, when the change is related "
                    "to it (the pinned exterior, the cave, their inputs)")
    common(pr)
    full = targets.add_parser("full", help="the full map smoke chain: the Crash Site smoke, then the whole map battery "
                              "on the main map at the Crash Site, the town and the wilderness, and on elven ruins")
    common(full)
    one = targets.add_parser("map", help="one map's smoke: every default stage, or --stages")
    one.add_argument("name", help='the map folder name, e.g. "crash site cave" or Arcanum1-024-fixed')
    one.add_argument("--stages", help=stages)
    map_options(one)
    common(one)
    crash = targets.add_parser("crash-site", help="the Crash Site smoke alone: the pinned exterior regression with the "
                               "cave (crash-site-area without --area-map)")
    crash.add_argument("--hero", choices=launch.PREMADE_HEROES, help=f"the pre-made hero (default {CRASH_SITE_HERO})")
    crash.add_argument("--env", action="append", metavar="NAME=VALUE", help="one more variable for the viewer; repeatable")
    common(crash)
    test = targets.add_parser("self-test", help="the viewer's synthetic --self-test (no game data)")
    test.add_argument("--headless", action="store_true", help="Godot --headless (default: with a window)")
    test.add_argument("--env", action="append", metavar="NAME=VALUE", help="one more variable for the viewer; repeatable")
    common(test)
