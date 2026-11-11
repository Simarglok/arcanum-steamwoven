"""ARC-DEV-396: smoke zones (option A of docs/research/smoke-speed-diagnosis-2026-11.md): a core zone, heavy zones with
triggers, and a complete run.

A zone is a set of map smoke stages (and, for `crash-site`, the Crash Site smoke) that a change can break together.
`core` holds every cheap stage, so the runner (smoke.py, ARC-DEV-391) groups it into about ten viewer processes by
configuration. The heavy zones are the stages that cost minutes each (`walk`, `container-click`, `census`) and the Crash
Site smoke; they run only when a changed file names them. `complete` is the nightly run (`./arcanum smoke complete`, what
`./arcanum daily` runs): the whole map batteries and the Crash Site smoke of `full`, then every stage the viewer accepts,
so no stage goes unprotected.

`./arcanum smoke pr --auto` reads the diff against the merge base with `origin/main` and picks the zones through TRIGGERS.
The table is a safe miss when it lacks a row: a changed file that no rule names selects `complete`, and so does a file of
the shared data, save schema, render or shared UI code. A file that no smoke can cover (documents, unit tests, research and
packaging tools) selects nothing, and a diff of such files runs no smoke and says so.
"""
from __future__ import annotations

from dataclasses import dataclass
from fnmatch import fnmatchcase
import re
import subprocess
from typing import Callable, Iterable, Sequence

CORE, WALK, CONTAINER_CLICK, CRASH_SITE, CENSUS, COMPLETE = ("core", "walk", "container-click", "crash-site", "census",
                                                           "complete")


@dataclass(frozen=True)
class Zone:
    """One zone: its name, what it covers, the stages it runs (None for `core`: every stage no heavy zone holds) and
    whether it runs the Crash Site smoke."""
    name: str
    summary: str
    stages: tuple[str, ...] | None = None
    crash_site: bool = False


ZONES: dict[str, Zone] = {zone.name: zone for zone in (
    Zone(CORE, "every cheap stage, grouped into a few processes by configuration"),
    Zone(WALK, "the long walk over the main map's window moves (about 9 minutes)", stages=("walk",)),
    Zone(CONTAINER_CLICK, "every container of the town window clicked as the player does (about 11 minutes)",
         stages=("container-click",)),
    Zone(CRASH_SITE, "the Crash Site smoke: the pinned exterior and the cave", crash_site=True),
    Zone(CENSUS, "every map of the catalogue opened in turn", stages=("census",)),
)}
HEAVY = tuple(name for name in ZONES if name != CORE)
CHOICES = (*ZONES, COMPLETE)
# The batteries of the full chain already vouch for these two stages, so the complete run does not list them again.
IN_BATTERIES = frozenset({"map", "resources"})


def zone_stages(name: str, known: Sequence[str]) -> tuple[str, ...]:
    """The stages of one zone; `core` is every stage of `known` (the viewer's MapSmokeStageNames) no heavy zone holds, so a
    stage the viewer gains lands in `core` until a heavy zone claims it."""
    if name == CORE:
        heavy = {stage for zone in ZONES.values() for stage in zone.stages or ()}
        return tuple(stage for stage in known if stage not in heavy)
    return ZONES[name].stages or ()


def complete_stages(known: Sequence[str]) -> tuple[str, ...]:
    """Every stage the complete run lists after its batteries: all of them but the two the batteries vouch for, the core
    stages first and the heavy zones' last."""
    return tuple(stage for name in ZONES for stage in zone_stages(name, known) if stage not in IN_BATTERIES)


def stages_of(zones: Sequence[str], known: Sequence[str]) -> tuple[str, ...]:
    """The stages of the chosen zones (all of them for `complete`), the core stages first."""
    return tuple(dict.fromkeys(stage for name in ZONES if name in zones or COMPLETE in zones
                               for stage in zone_stages(name, known)))


# Path patterns (fnmatch on the repository-relative path with `/`; `*` also crosses `/`; a trailing `/` is a whole folder)
# and the zones a changed file selects. A file takes the union of every rule it matches; an empty zone list says that no
# smoke covers the file; a file no rule matches selects `complete`.
_NONE: tuple[str, ...] = ()
_ALL = (COMPLETE,)
_V = "tools/terrain-viewer/Godot/"
TRIGGERS: tuple[tuple[str, tuple[str, ...], str], ...] = (
    # no smoke covers these
    ("docs/", _NONE, "documents"), ("*.md", _NONE, "documents"), ("tests/", _NONE, "unit tests"),
    ("experiments/", _NONE, "labs"), (".gitattributes", _NONE, "repository settings"),
    (".gitignore", _NONE, "repository settings"), ("LICENSE", _NONE, "licence"), ("arcanum", _NONE, "launcher script"),
    ("tools/research/", _NONE, "research probes"), ("tools/hd_pack/", _NONE, "HD pack tool"),
    ("tools/hd-interface-import/", _NONE, "HD import tool"), ("tools/hd_font/", _NONE, "font tool"),
    ("tools/original-oracle/", _NONE, "oracle"), ("tools/terrain-inspect/", _NONE, "inspect tool"),
    ("tools/arcanum-local", _NONE, "launcher"), ("tools/local_dev/*", _NONE, "launcher tooling"),
    ("src/Arcanum.Executable/", _NONE, "the executable interpreter (no viewer project uses it)"),
    ("src/Arcanum.Godot/", _NONE, "the HD review viewer"), ("src/Arcanum.Input.Godot/", _NONE, "the lab input adapter"),
    # the runner itself: one zone proves it still runs
    ("tools/local_dev/smoke*", (CORE,), "smoke runner"), ("tools/local_dev/launch.py", (CORE,), "viewer arguments"),
    ("tools/local_dev/run_queue.py", (CORE,), "queue"), ("tools/local_dev/start_check.py", (CORE,), "start check"),
    ("tools/local_dev/config.py", (CORE,), "inputs"),
    # the cheap stages: ordinary game and viewer code
    ("src/Arcanum.Simulation/", (CORE,), "simulation"), ("src/Arcanum.Session/", (CORE,), "session"),
    ("src/Arcanum.World/", (CORE,), "world"), ("src/Arcanum.Scripting/", (CORE,), "scripting"),
    ("src/Arcanum.Input/", (CORE,), "input"), (_V + "*", (CORE,), "viewer"), ("tools/terrain-viewer/Core/", (CORE,), "viewer core"),
    # shared code every stage draws or loads through
    ("src/Arcanum.Data/", _ALL, "shared data readers"), ("src/Arcanum.Presentation/", _ALL, "shared presentation"),
    ("src/Arcanum.Session/GameSaveLibrary.cs", _ALL, "save schema"), ("src/Arcanum.Session/GameSessionSnapshot*", _ALL, "save schema"),
    ("src/Arcanum.Session/GameSessionSaveSlot.cs", _ALL, "save schema"),
    (_V + "TerrainCanvas*", _ALL, "shared render"), (_V + "TerrainCameraController.cs", _ALL, "shared render"),
    (_V + "TerrainTextureCache.cs", _ALL, "shared render"), (_V + "AreaChunkTextureCache.cs", _ALL, "shared render"),
    (_V + "AreaDrawItem.cs", _ALL, "shared render"),     ("tools/terrain-viewer/Core/AreaFrameDriver.cs", _ALL, "shared render"), ("tools/terrain-viewer/Core/CameraPolicy.cs", _ALL, "shared render"),
    ("tools/terrain-viewer/Core/FramePacingPolicy.cs", _ALL, "shared render"),
    ("tools/terrain-viewer/Core/AreaVisibilityIndex.cs", _ALL, "shared render"),
    ("tools/terrain-viewer/Core/AreaPanelArt.cs", _ALL, "shared UI"), ("tools/terrain-viewer/Core/AreaTextFonts.cs", _ALL, "shared UI"),
    (_V + "AreaTextFonts.cs", _ALL, "shared UI"), ("tools/terrain-viewer/Core/AreaInterfaceWindows.cs", _ALL, "shared UI"),
    ("tools/terrain-viewer/Core/ViewerStoragePaths.cs", _ALL, "viewer settings paths"),
    ("*.csproj", _ALL, "project"), ("*.sln", _ALL, "project"), ("*.props", _ALL, "build settings"), ("*.targets", _ALL, "build settings"),
    ("*.rsp", _ALL, "build settings"), ("global.json", _ALL, "SDK pin"), ("*packages*.lock.json", _ALL, "packages"),
    ("*.tscn", _ALL, "scene"), (_V + "project.godot", _ALL, "Godot project"), (_V + "export_presets.cfg", _ALL, "export"),
    # walk: window moves, walk and path code, the loader
    (_V + "TerrainViewerAreaMapWalkSmoke.cs", (WALK,), "walk stage"),     (_V + "TerrainViewerAreaMap.cs", (WALK,), "map window"), (_V + "TerrainViewerAreaMapVault.cs", (WALK,), "map window"),
    (_V + "TerrainViewerHeroRequests.cs", (WALK,), "hero walk requests"), (_V + "TerrainViewerHeroRuns.cs", (WALK,), "hero walk runs"),
    (_V + "TerrainViewerHeroLocation.cs", (WALK,), "hero location"), (_V + "TerrainViewerStickWalk.cs", (WALK,), "held walk"),
    (_V + "TerrainViewerWorld.cs", (WALK,), "world loop"),
    ("src/Arcanum.Simulation/Navigation/", (WALK,), "paths"), ("src/Arcanum.Simulation/Movement/", (WALK,), "movement"),
    ("src/Arcanum.Simulation/Residency/", (WALK,), "sector residency"),
    ("src/Arcanum.Simulation/Ai/ExecutorHeroWalkGoals.cs", (WALK,), "hero walk goals"),
    ("src/Arcanum.Simulation/Party/CompanionFollow.cs", (WALK,), "follow routes"),
    ("src/Arcanum.World/AreaRoute*", (WALK,), "routes"), ("src/Arcanum.World/AreaResiden*", (WALK,), "residency"),
    ("src/Arcanum.World/AreaMapLoad*", (WALK,), "window load"), ("src/Arcanum.World/AreaLoad*", (WALK,), "window load"),
    ("src/Arcanum.World/AreaMapTransfers.cs", (WALK,), "window transfers"), ("src/Arcanum.World/Terrain*", (WALK,), "terrain loader"),
    ("src/Arcanum.World/SectorTerrain.cs", (WALK,), "sector terrain"), ("src/Arcanum.World/LoadedSectorChart.cs", (WALK,), "sector chart"),
    ("src/Arcanum.World/AreaSectorChart.cs", (WALK,), "sector chart"), ("src/Arcanum.World/HeroRequests.cs", (WALK,), "hero requests"),
    ("src/Arcanum.World/GoalStepper.cs", (WALK,), "goal stepping"), ("src/Arcanum.World/MapResidentRows.cs", (WALK,), "resident rows"),
    ("src/Arcanum.Session/GameSessionResidency.cs", (WALK,), "residency"), ("src/Arcanum.Session/GameSessionSector*", (WALK,), "sectors"),
    ("src/Arcanum.Session/GameSessionCells.cs", (WALK,), "cells"), ("src/Arcanum.Session/GameSessionRealSteps.cs", (WALK,), "steps"),
    # container-click: picking, clicks on containers, roofs, locks
    (_V + "TerrainViewerContainerClickSmoke.cs", (CONTAINER_CLICK,), "container-click stage"),
    (_V + "TerrainViewerAreaContainerLoot.cs", (CONTAINER_CLICK,), "container loot"),
    (_V + "TerrainViewerContainerLootSmoke.cs", (CONTAINER_CLICK,), "container loot"),
    (_V + "TerrainViewerAreaRoofs.cs", (CONTAINER_CLICK,), "roofs"), (_V + "TerrainViewerAreaHover.cs", (CONTAINER_CLICK,), "hover and picking"),
    (_V + "TerrainViewerCursor.cs", (CONTAINER_CLICK,), "cursor"), (_V + "TerrainViewerHeroUse.cs", (CONTAINER_CLICK,), "use clicks"),
    ("src/Arcanum.World/AreaCellPicking.cs", (CONTAINER_CLICK,), "cell picking"), ("src/Arcanum.World/AreaHover*", (CONTAINER_CLICK,), "hover"),
    ("src/Arcanum.World/AreaContainerLoot.cs", (CONTAINER_CLICK,), "container loot"),
    ("src/Arcanum.World/AreaLockpicks.cs", (CONTAINER_CLICK,), "locks"), ("src/Arcanum.Simulation/Locks/", (CONTAINER_CLICK,), "locks"),
    ("src/Arcanum.Session/GameSessionLocks.cs", (CONTAINER_CLICK,), "locks"), ("src/Arcanum.Session/GameSessionPickLock.cs", (CONTAINER_CLICK,), "locks"),
    # crash-site: the pinned exterior, the cave, their inputs
    ("src/Arcanum.World/CrashSiteProfile.cs", (CRASH_SITE,), "Crash Site profile"),
    ("src/Arcanum.World/AreaTransition.cs", (CRASH_SITE,), "map transitions"), ("src/Arcanum.World/AreaPortals.cs", (CRASH_SITE,), "portals"),
    ("src/Arcanum.World/AreaStartMapAreas.cs", (CRASH_SITE,), "start areas"), ("src/Arcanum.World/AreaNewGameStart.cs", (CRASH_SITE,), "new game start"),
    ("src/Arcanum.World/Terrain*", (CRASH_SITE,), "terrain loader"), ("src/Arcanum.World/AreaMapLoad*", (CRASH_SITE,), "map load"),
    (_V + "TerrainViewerArea.cs", (CRASH_SITE,), "area mode"), (_V + "TerrainViewerAreaTransition.cs", (CRASH_SITE,), "transitions"),
    (_V + "TerrainViewerAreaPortals.cs", (CRASH_SITE,), "portals"), (_V + "TerrainViewerAreaPortalSmoke.cs", (CRASH_SITE,), "portals"),
    (_V + "TerrainViewerTeleport.cs", (CRASH_SITE,), "teleport"), (_V + "TerrainViewerChangeOfPlace.cs", (CRASH_SITE,), "change of place"),
    # census: the catalogue and the map readers
    (_V + "TerrainViewerAreaMapCensus.cs", (CENSUS,), "census stage"), ("src/Arcanum.World/AreaMapRecords.cs", (CENSUS,), "map records"),
    ("src/Arcanum.World/WholeMapMobileRows.cs", (CENSUS,), "whole-map rows"), ("src/Arcanum.World/MapResidentRows.cs", (CENSUS,), "map rows"),
    ("src/Arcanum.World/AreaStartMapAreas.cs", (CENSUS,), "map catalogue"),
)


# Two viewer files that every new smoke stage touches (its name in MapSmokeStageNames, its dispatch line) also hold shared
# logic: the viewer's start and stage dispatch, and the window moves. They select `core`; only a change to a line that is
# more than such a registration adds the zones here (always, when the diff's lines cannot be read).
REGISTRATIONS: dict[str, tuple[re.Pattern[str], tuple[str, ...]]] = {
    _V + "TerrainViewerApp.cs": (re.compile(r"only\??\.Contains\(\"|^\s*$"), (COMPLETE,)),
    _V + "TerrainViewerAreaMapStream.cs": (re.compile(r"MapSmokeStageNames|^\s*$"), (WALK,)),
}


def pattern_matches(pattern: str, path: str) -> bool:
    """A trailing `/` names a whole folder; otherwise fnmatch with `*` crossing `/`."""
    return path.startswith(pattern) if pattern.endswith("/") else fnmatchcase(path, pattern)


@dataclass(frozen=True)
class Selection:
    """What a diff selects: the zones (in ZONES order; `complete` stands for all of them), the files that selected each
    zone and the files no smoke covers."""
    zones: tuple[str, ...]
    because: dict[str, tuple[str, ...]]
    uncovered: tuple[str, ...]
    files: int

    @property
    def complete(self) -> bool:
        return COMPLETE in self.zones


def select_zones(paths: Iterable[str], lines: Callable[[str], Sequence[str]] | None = None) -> Selection:
    """The zones the changed files select: the union over files of the rules each matches; a file that matches no rule
    selects `complete` (a missing row is a safe miss); a file whose only rules say `none` is uncovered. `lines` gives the
    changed lines of a file (REGISTRATIONS); without it such a file selects its extra zones as well."""
    paths = sorted(set(path.strip().replace("\\", "/") for path in paths if path.strip()))
    because: dict[str, list[str]] = {}
    uncovered = []
    for path in paths:
        hit = [zones for pattern, zones, _ in TRIGGERS if pattern_matches(pattern, path)]
        chosen = {zone for zones in hit for zone in zones} if hit else {COMPLETE}
        if path in REGISTRATIONS:
            plain, extra = REGISTRATIONS[path]
            changed = lines(path) if lines is not None else None
            if changed is None or any(not plain.search(line) for line in changed):
                chosen |= set(extra)
        if not chosen:
            uncovered.append(path)
        for zone in chosen:
            because.setdefault(zone, []).append(path)
    if COMPLETE in because:
        return Selection((COMPLETE,), {zone: tuple(files) for zone, files in because.items()}, tuple(uncovered), len(paths))
    zones = tuple(name for name in ZONES if name in because)
    return Selection(zones, {name: tuple(because[name]) for name in zones}, tuple(uncovered), len(paths))


def git_output(*args: str) -> str:
    done = subprocess.run(("git", *args), capture_output=True, text=True, check=False)
    if done.returncode != 0:
        raise ValueError(f"git {' '.join(args)} failed: {done.stderr.strip() or done.stdout.strip()}")
    return done.stdout


def changed_files(base: str = "origin/main", git: Callable[..., str] = git_output) -> tuple[str, tuple[str, ...]]:
    """The merge base with `base` and the files changed since it: committed, staged, unstaged and untracked. `git` is the
    output of a git command (tests give a fake)."""
    mark = git("merge-base", base, "HEAD").strip()
    names = git("diff", "--name-only", "--no-renames", mark).splitlines() + \
        git("ls-files", "--others", "--exclude-standard").splitlines()
    return mark, tuple(sorted(set(name.strip() for name in names if name.strip())))


def changed_lines(mark: str, path: str, git: Callable[..., str] = git_output) -> list[str]:
    """The added and removed lines of one file since `mark`."""
    return [line[1:] for line in git("diff", "-U0", "--no-renames", mark, "--", path).splitlines()
            if line[:1] in "+-" and not line.startswith(("+++", "---"))]


def describe(selection: Selection, mark: str, base: str) -> list[str]:
    """The lines `--auto` prints: the base, each chosen zone with the files that chose it, and the files no smoke covers."""
    lines = [f"SMOKE auto: {selection.files} changed files since {base} ({mark[:10] or 'unknown'})"]
    if not selection.zones:
        lines.append("SMOKE auto: no smoke zone covers this diff (" + (", ".join(selection.uncovered[:3]) or "no changes")
                     + (", ..." if len(selection.uncovered) > 3 else "") + "): nothing to run")
    for name in selection.zones:
        files = selection.because.get(name, ())
        lines.append(f"SMOKE auto: zone {name}" + (f" because of {', '.join(files[:3])}"
                     + (f" and {len(files) - 3} more" if len(files) > 3 else "") if files else ""))
    if selection.uncovered and selection.zones:
        lines.append(f"SMOKE auto: not covered by any zone: {len(selection.uncovered)} files (documents, tests, tools)")
    return lines


def parse_zones(value: str | None) -> tuple[str, ...]:
    """`--zones core,walk`: known zone names, in the order given, `complete` standing alone for all of them."""
    if value is None:
        return ()
    zones = tuple(dict.fromkeys(item.strip() for item in value.split(",") if item.strip()))
    unknown = [zone for zone in zones if zone not in CHOICES]
    if not zones or unknown:
        raise ValueError("unknown smoke zone " + ", ".join(unknown or [repr(value)]) + " (known: " + ", ".join(CHOICES) + ")")
    return (COMPLETE,) if COMPLETE in zones else zones
