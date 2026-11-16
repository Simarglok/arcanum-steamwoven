"""ARC-DEV-061: the shared smoke runner, `./arcanum smoke`, so threads stop writing their own scratch smoke scripts.

The rule is docs/workflow/toolchain.md#smoke-runner-arc-dev-061 and the scope docs/workflow/tasks.md#smoke-scope. A run
builds the viewer from this checkout (dotnet, through the launch queue), imports its project once when it was never
imported, then runs each smoke as its own quiet `window` run of the queue with a start check on the viewer's
`ARC-DEV-059 viewer start` line for the intended mode, map and smoke. Each smoke gets its own settings folder (the
viewer's *_TEST_PATH files) and an environment without the owner's opt-in variables (DAT_ROOT, ARC_*, ARCANUM_*, the
settings paths), so neither the owner's files nor another smoke's leak into it. A failed smoke does not stop the
chain: every step runs and the summary names each one; a failed build or import, or a stopped run, stops it.

ARC-DEV-063: PASS means every requested stage ran. A smoke passes only when its log holds each requested stage's own
PASS line (a stage the viewer skips, silently or with a NONE/SKIP line, fails it); a stage that needs a hero gets
AUTO_HERO when no --hero is given; a stage the viewer runs only without `map` gets its own run when both are asked for.
"""
from __future__ import annotations

import argparse
import dataclasses
from dataclasses import dataclass
import functools
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
from typing import Callable, Mapping, Sequence

from . import config, focus_guard, launch, red_flag, run_queue, smoke_history, smoke_zones, start_check

ROOT = Path(__file__).resolve().parents[2]
VIEWER = Path("tools/terrain-viewer/Godot")
SOLUTION = VIEWER / "Arcanum.TerrainViewer.sln"
STAGES_SOURCE = VIEWER / "TerrainViewerAreaMapStream.cs"
DEFAULT_GODOT = "/Applications/Godot_mono.app/Contents/MacOS/Godot"
MAIN_MAP = "Arcanum1-024-fixed"
# The Crash Site smoke: the viewer's pinned Crash Site exterior regression (area mode without --area-map), with the
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


def settings_file(name: str) -> str:
    """The file a smoke's settings variable names: a settings file (arcanum.cfg format, ARC-UI-1130) for the player's settings, the
    JSON of the WORLD diagnostic's own preferences for the diagnostic's."""
    return name.lower() + (".json" if name.startswith("WORLD_DIAGNOSTIC") else ".cfg")


# ARC-DEV-395: the stages that check a sample unless --all (the viewer reads ALL_ENV: docs/workflow/toolchain.md#smoke-runner-arc-dev-061).
SAMPLED_STAGES = frozenset({"container-click", "walk"})
ALL_ENV = "ARC_SMOKE_ALL"
# The owner's opt-in data, smoke and settings variables never reach a smoke (as in the player package); the queue's stay.
DROPPED_PREFIXES = ("ARC_", "ARCANUM_", "DAT_ROOT", "LAUNCHER_SETTINGS_", "WORLD_DIAGNOSTIC_", "ALWAYS_RUN_",
                    "AUDIO_VOLUMES_", "GAME_SETTINGS_", "VISUAL_PROFILE_")
KEPT_VARIABLES = frozenset({run_queue.TICKET_ENV, run_queue.KIND_ENV})
STOPPED = 128  # an exit at or above it is a stopped run (128+N), which ends the chain
NAME = re.compile(r"^[A-Z][A-Z0-9_]*$")
RESERVED = frozenset({"PATH", "DOTNET_ROOT", "HOME", "GODOT", "__GODOT_SHELL_ENV_SET"})  # the canonical start's own
BATTERY_MINUTES = 60.0  # a whole map battery (no stage list) runs about 2 minutes; the queue's window default is 30
# ARC-DEV-390: the seconds each step declares to the launch queue, which puts a short run ahead of a long one that arrived a
# little before it (run_queue.ORDER_WEIGHT). Measured for the smoke speed diagnosis (ARC-DEV-384, deleted; see docs/workflow/toolchain.md): a run with no
# stage worth naming is its map load (about 20 s with start and exit) plus 0-2 s per cheap stage; the whole map battery
# 25 s (declared with room for a loaded Mac); the self-test 24 s with a window and 12 s headless; the Crash Site smoke 98 s;
# a project import 3 s warm; a viewer build 12 s warm, 45 s cold. The stages that stand out add their own time: `walk`
# 525 s (72 window moves) and `container-click` on the town 675 s in full, and about a fifth of that as a sample (ARC-DEV-395:
# the default; the sampled figures are estimates, not measured); `census` is every map of the catalogue and was not
# measured: an assumption.
RUN_SECONDS = 20.0  # a viewer run with no stage on top of it
STAGE_SECONDS = 3.0  # each cheap stage, with room for the dearer ones (opening 13 s, perf 16 s)
BATTERY_SECONDS = 60.0  # the whole map battery with its load
CRASH_SITE_SECONDS = 110.0
SELF_TEST_SECONDS = {"window": 30.0, "quick": 15.0}
BUILD_SECONDS, IMPORT_SECONDS = 30.0, 5.0
IMPORT_MINUTES = 30.0  # a first import of a new checkout is not the warm 3 s (the limit the headless run had)
SLOW_STAGES = {"walk": 540.0, "container-click": 700.0, "census": 900.0}  # in full (--all)
SAMPLED_SLOW_SECONDS = {"walk": 100.0, "container-click": 120.0}  # as the sample they run by default


def estimate(stages: Sequence[str], full: bool = False) -> float:
    """ARC-DEV-390: the seconds a viewer run of these stages is expected to take (() is the whole map battery), the
    estimate the queue orders it by; `full` is a run with --all, whose sampled stages run in full. Declared, not measured
    per run: a wrong one only costs its run a different place in the queue."""
    if not stages:
        return BATTERY_SECONDS
    slow = SLOW_STAGES if full else {**SLOW_STAGES, **SAMPLED_SLOW_SECONDS}
    return RUN_SECONDS + sum(slow.get(stage, STAGE_SECONDS) for stage in stages)


STAGE_NOT_RUN = 3  # a smoke that exited 0 without the PASS line of a requested stage (ARC-DEV-063)
# ARC-DEV-063: the line each map smoke stage prints when it ran and passed (the viewer's own markers, checked against its
# sources by the tests). A whole map battery (no stage list) must show the map and resources stages; its other stages
# depend on the map (a whole map has no window to stream, only the start map opens the world map).
STAGE_PASS = {
    "map": "ARC-RENDER-017 native-smoke map PASS", "resources": "ARC-RENDER-007 resources reload PASS",
    "transfer": "ARC-WORLD-031 native-smoke transfer PASS", "roundtrip": "ARC-RENDER-024 native-smoke round-trip PASS",
    "stream": "ARC-RENDER-024 native-smoke stream PASS", "worldmap": "ARC-UI-048 native-smoke worldmap PASS",
    "walk": "ARC-WORLD-042 native-smoke walk PASS", "census": "ARC-RENDER-027 native-smoke census PASS",
    "cast": "ARC-MAGIC-015 native-smoke cast PASS", "turn-based": "ARC-COMBAT-020 native-smoke turn-based PASS",
    "knockdown": "ARC-COMBAT-021 native-smoke knockdown PASS", "ranged": "ARC-COMBAT-015 native-smoke ranged PASS",
    "container": "ARC-WORLD-101 native-smoke container PASS", "junk": "ARC-WORLD-063 native-smoke junk-pile PASS",
    "barter": "ARC-UI-050 native-smoke barter PASS", "menu": "ARC-UI-011 native-smoke main menu PASS",
    "scene": "ARC-UI-012 native-smoke scene flow PASS", "options": "ARC-UI-013 native-smoke options PASS",
    "plants": "ARC-WORLD-081 native-smoke plants PASS", "bar": "ARC-UI-018 native-smoke bar PASS",
    "ai": "ARC-WORLD-092 native-smoke ai PASS", "ai-turn-based": "ARC-WORLD-095 native-smoke ai-turn-based PASS",
    "ai-cast": "ARC-WORLD-102 native-smoke ai-cast PASS", "ai-follower": "ARC-WORLD-115 native-smoke ai-follower PASS",
    "premade": "ARC-WORLD-114 native-smoke premade PASS", "opening": "ARC-SCRIPT-047 native-smoke opening PASS",
    "life": "ARC-UI-020 native-smoke life PASS", "floaters": "ARC-UI-021 native-smoke floaters PASS",
    "death": "ARC-UI-026 native-smoke death PASS", "slides": "ARC-RENDER-1879 native-smoke slides PASS", "pickpocket": "ARC-SKILLS-010 native-smoke pickpocket PASS", "loot-worn": "ARC-DEV-202 native-smoke loot-worn PASS", "inv-arrange": "ARC-DEV-339 native-smoke inv-arrange PASS", "loot-room": "ARC-DEV-333 native-smoke loot-room PASS",
    "wreck": "ARC-SCRIPT-046 wreck smoke PASS", "altar": "ARC-SCRIPT-052 altar smoke PASS", "debug-travel": "ARC-DEV-050 native-smoke debug-travel PASS",
    "report": "ARC-DEV-051 native-smoke bug report PASS", "hover": "ARC-UI-057 native-smoke hover-window PASS",
    "dead-window": "ARC-COMBAT-024 native-smoke dead-window PASS", "inv-ui": "ARC-DEV-111 native-smoke inv-ui PASS",
    "follower-home": "ARC-DEV-351 native-smoke follower-return PASS", "follower-far": "ARC-DEV-170 native-smoke follower-far PASS", "teleport-follow": "ARC-MAGIC-1147 native-smoke teleport-follow PASS", "talk-teleport": "ARC-WORLD-2261 native-smoke talk-teleport PASS", "npc-resident": "ARC-DEV-170 native-smoke npc-resident PASS", "saved-runs": "ARC-DEV-208 native-smoke saved-runs PASS", "npc-door": "ARC-DEV-209 native-smoke npc-door PASS", "npc-tile": "ARC-DEV-206 native-smoke npc-tile PASS", "npc-wander": "ARC-AI-1313 native-smoke npc-wander PASS", "door-close": "ARC-DEV-138 native-smoke door-close PASS",
    "tile-ghost": "ARC-DEV-298 native-smoke tile-ghost PASS", "container-click": "ARC-DEV-299 native-smoke container-click PASS", "fire-order": "ARC-DEV-203 native-smoke fire-order PASS", "indoor-fade": "ARC-DEV-203 native-smoke indoor-fade PASS",
    "route-barrel": "ARC-DEV-247 native-smoke route-barrel PASS", "critter-fall": "ARC-DEV-248 native-smoke critter-fall PASS", "save-resume": "ARC-DEV-325 native-smoke save-resume PASS", "cast-multi": "ARC-DEV-250 native-smoke cast-multi PASS", "companion-click": "ARC-DEV-263 native-smoke companion-click PASS", "grenade": "ARC-DEV-249 native-smoke grenade PASS", "please-move": "ARC-AI-1315 native-smoke please-move PASS", "ai-react": "ARC-DEV-264 native-smoke ai-react PASS", "npc-shot": "ARC-DEV-282 native-smoke npc-shot PASS", "npc-object": "ARC-AI-1757 native-smoke npc-object PASS", "npc-throw": "ARC-DEV-1762 native-smoke npc-throw PASS", "placement-census": "ARC-DEV-308 native-smoke placement-census PASS", "inv-carry": "ARC-DEV-305 native-smoke inv-carry PASS", "spell-end": "ARC-DEV-301 native-smoke spell-end PASS", "hold-hero": "ARC-DEV-304 native-smoke hold-hero PASS", "spirit-talk": "ARC-DEV-304 native-smoke spirit-talk PASS", "healer-talk": "ARC-SCRIPT-1790 native-smoke healer-talk PASS", "sleep": "ARC-WORLD-1160 native-smoke sleep PASS", "bed": "ARC-WORLD-1162 native-smoke bed PASS", "spell-effects": "ARC-DEV-1148 native-smoke spell-effects PASS", "created-scenery": "ARC-DEV-347 native-smoke created-scenery PASS", "object-art": "ARC-SCRIPT-1735 native-smoke object-art PASS", "created-critter": "ARC-DEV-370 native-smoke created-critter PASS", "script-critters": "ARC-DEV-371 native-smoke script-critters PASS", "script-eye-candy": "ARC-SCRIPT-1592 native-smoke script-eye-candy PASS", "weapon-hit": "ARC-RENDER-1990 native-smoke weapon-hit PASS", "encounters": "ARC-WORLD-1159 native-smoke encounters PASS", "worn-light": "ARC-DEV-335 native-smoke worn-light PASS", "critter-light": "ARC-DEV-353 native-smoke critter-light PASS", "eye-light": "ARC-MAGIC-1229 native-smoke eye-light PASS", "summon-flash": "ARC-DEV-352 native-smoke summon-flash PASS", "summon-wall": "ARC-DEV-352 native-smoke summon-wall PASS", "summon-undead": "ARC-MAGIC-053 native-smoke summon-undead PASS", "tech-craft": "ARC-DEV-362 native-smoke tech-craft PASS", "tech-products": "ARC-MAGIC-051 native-smoke tech-products PASS", "item-aim": "ARC-DEV-365 native-smoke item-aim PASS", "disarm": "ARC-SKILLS-1156 native-smoke disarm PASS",
    "perf": "ARC-DEV-367 native-smoke perf PASS", "relight": "ARC-RES-1978 native-smoke relight PASS", "movies": "ARC-RENDER-1170 native-smoke movies PASS", "movement": "ARC-MAGIC-036 native-smoke movement PASS", "creature-item": "ARC-MAGIC-042 native-smoke creature-item PASS", "trait-body": "ARC-MAGIC-046 native-smoke trait-body PASS", "weapon-art": "ARC-RENDER-1544 native-smoke weapon-art PASS", "body-styles": "ARC-MAGIC-045 native-smoke body-styles PASS", "perf-menu": "ARC-DEV-367 native-smoke perf-menu PASS",
}
# ARC-DEV-1556: a stage whose own PASS line is not the whole check: the lines that must also be in the log. `transfer` runs the
# visitor-fight sub-check after its own PASS line; with a hero (HERO_STAGES) the Crash Site start and the town both print its PASS
# (a NONE or NO-TARGET line there means the fight did not run), so the stage counts as run only with both.
# ARC-AI-1695: `transfer` also carries a force-joined NPC without a dialog (its led-carry and led-home lines), and the two stages that move the main map's window
# (`follower-far`, `teleport-follow`) check it there too (led-far).
STAGE_ALSO_PASS = {"transfer": ("ARC-DEV-065 native-smoke visitor-fight PASS", "ARC-AI-1695 native-smoke led-carry PASS", "ARC-AI-1695 native-smoke led-home PASS"),
                   "follower-far": ("ARC-AI-1695 native-smoke led-far PASS",), "teleport-follow": ("ARC-AI-1695 native-smoke led-far PASS",),
                   "report": ("ARC-DEV-2190 native-smoke owner-qa PASS",)}
BATTERY_STAGES = ("map", "resources")
# The stages a hero must play: without a session hero the viewer skips them (the AI stages silently). The map battery
# plays the hero's stages too (cast, turn-based, ranged, container, junk ...). `transfer` is one (ARC-DEV-1547): its visitor-fight
# sub-check prints NONE without a hero while the stage's own PASS line still comes, so only a hero runs it whole; STAGE_ALSO_PASS makes the runner require its PASS line (ARC-DEV-1556).
HERO_STAGES = frozenset({"relight", "map", "cast", "turn-based", "knockdown", "ranged", "container", "junk", "hover", "barter", "life",
                         "floaters", "ai", "ai-turn-based", "ai-cast", "ai-follower", "premade", "opening", "pickpocket", "loot-worn", "inv-arrange", "loot-room", "dead-window", "inv-ui", "transfer", "follower-home", "follower-far", "teleport-follow", "talk-teleport", "npc-resident", "saved-runs", "npc-door", "npc-tile", "please-move", "npc-wander", "tile-ghost", "container-click", "door-close", "fire-order", "indoor-fade", "route-barrel", "critter-fall", "save-resume", "cast-multi", "grenade", "companion-click", "npc-shot", "npc-object", "npc-throw", "ai-react", "placement-census", "inv-carry", "worldmap", "spell-end", "hold-hero", "spell-effects", "movement", "creature-item", "movies", "spirit-talk", "healer-talk", "sleep", "bed", "created-scenery", "object-art", "created-critter", "script-critters", "script-eye-candy", "weapon-hit", "encounters", "worn-light", "critter-light", "eye-light", "summon-flash", "summon-wall", "summon-undead", "tech-craft", "tech-products", "item-aim", "disarm", "trait-body", "weapon-art", "body-styles", "perf"})
AUTO_HERO = "human-male"  # the full chain's hero on the main map; a map of the chain gets its own (elven ruins: elf-female)
# The stages the viewer runs only when `map` is not in the list (its else branch), and those of them the map battery runs
# as well (the tests read both from TerrainViewerApp.cs).
ALONE_STAGES = frozenset({"barter", "menu", "scene", "options", "plants", "bar", "life", "floaters", "death", "slides", "ai",
                          "ai-turn-based", "ai-cast", "ai-follower", "premade", "opening", "pickpocket", "loot-worn", "inv-arrange", "loot-room", "wreck", "altar", "inv-ui", "npc-resident", "saved-runs", "npc-door", "npc-tile", "please-move", "npc-wander", "tile-ghost", "container-click", "fire-order", "indoor-fade", "critter-fall", "save-resume", "cast-multi", "companion-click", "ai-react", "placement-census", "spell-end", "hold-hero", "spell-effects", "movement", "spirit-talk", "healer-talk", "sleep", "bed", "created-scenery", "object-art", "created-critter", "script-critters", "script-eye-candy", "weapon-hit", "encounters", "relight", "worn-light", "critter-light", "eye-light", "summon-flash", "summon-wall", "summon-undead", "tech-craft", "tech-products", "item-aim", "disarm", "trait-body", "weapon-art", "body-styles", "perf", "perf-menu"})
BATTERY_ALSO = frozenset({"cast", "turn-based", "knockdown", "ranged", "container", "junk", "hover", "report", "door-close"})
SKIP_LINE = re.compile(r"native-smoke .* (NONE|SKIP|NO-EXIT|NO-TARGET|SAME-MAP-ONLY)\b")

# ARC-DEV-391: the configuration each stage runs in. A viewer process fixes its map, start sector, hero, main menu and debug
# mode at launch, so only stages with the same configuration can share a process. The table (read by `--stages` to group the
# stages into the fewest processes, and to refuse a stage given the wrong configuration before anything waits for the
# queue) is the one place that says "this stage runs on this configuration"; the evidence is the stage's own NONE/SKIP
# lines and the smoke speed diagnosis (ARC-DEV-384, deleted; the result is in docs/workflow/toolchain.md). The hero is HERO_STAGES.
TOWN = "1409,1314"  # the town sector
WILD = "1384,239"  # the generated wilderness sector (`plants`)
SHRINE = "1452,1291"  # Virgil's altar sector (`altar`, `npc-tile`)
FIRE = "1410,1315"  # the town's brick fireplaces (`fire-order`)
CAVE = "crash site cave"
RUINS = "elven ruins"
LIGHT_MAP = "tulla interior - battle - conveyance"  # an NPC there carries a light (`critter-light`; NONE on the main map, town, ruins, cave)
SCENE_MAP = "crematorium-lev 1"  # a map that is not the start map: `scene` offers a new game only there


@dataclass(frozen=True)
class StageNeeds:
    """What one stage needs. `map` and `sector` are the default configuration a stage run alone gets (`sector` None: the
    Crash Site start, no --sector; a sector only matters on the main map, a window around it); the `only_*` fields say
    that anything else fails the stage (a NONE or SKIP line), so a request that names another configuration is refused;
    without them the configuration was only observed, and another one is not refused."""
    map: str = MAIN_MAP
    sector: str | None = None
    only_map: bool = False
    only_sector: bool = False
    also_sectors: tuple[str | None, ...] = ()  # other start sectors the stage passes in, besides `sector`
    not_maps: tuple[str, ...] = ()  # maps it fails on
    menu: bool = False  # runs from the main menu (--main-menu, no hero)
    debug: bool = False  # needs the viewer's debug mode (ARC-DEV-050), --debug-mode (ARC-WORLD-120)
    own: bool = False  # its own process: it ends the session, moves the window, leaves state behind or runs long
    portable: bool = False  # passes in any configuration, so it rides in a process that exists anyway

    def takes_map(self, name: str) -> bool:
        return name.casefold() not in (other.casefold() for other in self.not_maps) and \
            (not self.only_map or name.casefold() == self.map.casefold())

    def takes_sector(self, map_name: str, sector: str | None) -> bool:
        return map_name.casefold() != MAIN_MAP.casefold() or not self.only_sector \
            or sector in (self.sector, *self.also_sectors)

    def wants(self) -> str:
        sector = f"sector {self.sector}" if self.sector else "the start sector (no --sector)"
        where = (f"map {self.map}" if self.only_map else f"a map other than {', '.join(self.not_maps)} (such as {self.map})"
                 if self.not_maps else "a map where it finds its target")
        return where + (f" at {sector}" if self.only_sector else "") + (" from the main menu" if self.menu else "")


def normal_sector(value: str | None) -> str | None:
    """`X,Y` as the launcher reads it (integers), so 01409,1314 is the town sector."""
    try:
        return ",".join(str(int(part)) for part in value.split(",")) if value else value
    except ValueError:
        return value


def _needs(**fields) -> StageNeeds:
    return StageNeeds(**fields)


_MAIN_ONLY = dict(only_map=True)
_MENU = dict(menu=True, only_map=True, only_sector=True, own=True)
_BOTH = dict(only_sector=True, also_sectors=(None, TOWN))  # the transfer block: the Crash Site start or the town
STAGE_NEEDS: dict[str, StageNeeds] = {
    "map": _needs(portable=True), "resources": _needs(portable=True),
    "transfer": _needs(**_MAIN_ONLY, **_BOTH), "roundtrip": _needs(**_MAIN_ONLY, **_BOTH),
    "follower-home": _needs(**_MAIN_ONLY, **_BOTH), "stream": _needs(**_MAIN_ONLY), "worldmap": _needs(**_MAIN_ONLY),
    "walk": _needs(**_MAIN_ONLY, only_sector=True, own=True),  # ~525 s; fails off the main map
    "census": _needs(own=True),  # every map of the catalogue in turn
    "cast": _needs(), "turn-based": _needs(), "knockdown": _needs(portable=True), "ranged": _needs(), "container": _needs(),
    "junk": _needs(map=RUINS),  # the main map has no container with two items (NONE); the ruins have (ARC-DEV-396)
    "barter": _needs(),
    "menu": _needs(**_MENU), "options": _needs(**_MENU), "bar": _needs(**_MENU), "death": _needs(**_MENU), "slides": _needs(**_MENU),
    "perf-menu": _needs(**_MENU),
    "scene": _needs(map=SCENE_MAP, not_maps=(MAIN_MAP,), menu=True, own=True),
    "plants": _needs(**_MAIN_ONLY, sector=WILD, only_sector=True),
    "ai": _needs(), "ai-turn-based": _needs(), "ai-cast": _needs(own=True),  # fails after the other AI stages of a process, passes alone (2026-11-11)
    "ai-follower": _needs(**_MAIN_ONLY),
    "premade": _needs(**_MAIN_ONLY), "opening": _needs(**_MAIN_ONLY),
    "life": _needs(), "floaters": _needs(), "pickpocket": _needs(), "loot-worn": _needs(), "loot-room": _needs(),
    "inv-arrange": _needs(portable=True), "inv-ui": _needs(),
    "wreck": _needs(**_MAIN_ONLY, only_sector=True), "altar": _needs(**_MAIN_ONLY, sector=SHRINE, only_sector=True),
    "debug-travel": _needs(debug=True, own=True), "report": _needs(portable=True), "hover": _needs(),
    "door-close": _needs(sector=TOWN, only_sector=True),
    "dead-window": _needs(**_MAIN_ONLY, only_sector=True, own=True),  # leaves the hero dead; NONE after `stream`
    "follower-far": _needs(**_MAIN_ONLY, debug=True, own=True),
    "teleport-follow": _needs(**_MAIN_ONLY, own=True),
    "talk-teleport": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True, own=True),  # moves the hero to K'na Tha and back (ARC-WORLD-2261)
    "npc-resident": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True),
    "saved-runs": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True),
    "npc-door": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True, own=True),  # NONE after the other town stages, passes alone
    "npc-tile": _needs(**_MAIN_ONLY, sector=SHRINE, only_sector=True),
    "npc-wander": _needs(**_MAIN_ONLY, sector=SHRINE, only_sector=True),
    "tile-ghost": _needs(map=CAVE, only_map=True), "route-barrel": _needs(map=CAVE, only_map=True),
    "container-click": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True, own=True),  # ~675 s
    "fire-order": _needs(**_MAIN_ONLY, sector=FIRE, only_sector=True),
    "indoor-fade": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True),
    "critter-fall": _needs(own=True),  # fails in a longer list ("ran over at least two cells"), passes alone
    "save-resume": _needs(), "cast-multi": _needs(map=RUINS, only_map=True), "grenade": _needs(map=CAVE),
    # ai-react: a bystander the witness rule sends fleeing (a human near the hit, ARC-DEV-1577) exists in the town, not at the Crash Site (the animals protect one another: NONE); elven ruins and the cave: the spell lands (ARC-DEV-1660: the victim must have a clear line of fire, the cast refuses without one, spell.mes 604) but no calm bystander the rule sends fleeing stands near the hit: NONE (2026-11-13)
    "companion-click": _needs(), "npc-shot": _needs(sector=TOWN), "npc-object": _needs(sector=TOWN, own=True), "npc-throw": _needs(sector=TOWN, own=True), "ai-react": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True, own=True), "please-move": _needs(),
    "placement-census": _needs(),  # passes after the other AI stages (ARC-DEV-1187: baseline loads at the stage boundaries, a companion is no NPC row)
    "inv-carry": _needs(debug=True, own=True),  # a dead body with an item; NONE at the Crash Site was seen
    "spell-end": _needs(), "hold-hero": _needs(), "spell-effects": _needs(), "movement": _needs(), "spirit-talk": _needs(), "healer-talk": _needs(), "sleep": _needs(sector=WILD, own=True),  # moves the clock by days; the wilderness sector has no town map (the sleep window, not the wait window)
    "bed": _needs(map=CAVE, only_map=True, own=True),  # the cave's one scripted bed: the sleep window through it, kind-2 heal; moves the clock
    "created-scenery": _needs(portable=True), "object-art": _needs(portable=True), "created-critter": _needs(portable=True),
    "script-critters": _needs(**_MAIN_ONLY), "script-eye-candy": _needs(**_MAIN_ONLY), "weapon-hit": _needs(**_MAIN_ONLY), "encounters": _needs(sector=WILD, own=True),  # moves the clock by days and ends with the hero landed elsewhere; the wilderness sector has no named area or town map
    "worn-light": _needs(), "relight": _needs(map=CAVE, own=True), "critter-light": _needs(map=LIGHT_MAP), "eye-light": _needs(),
    "summon-flash": _needs(portable=True), "summon-wall": _needs(portable=True), "summon-undead": _needs(portable=True),
    "tech-craft": _needs(map=CAVE, only_map=True, own=True),  # needs the cave's bullets; F8 replaces the session
    "tech-products": _needs(map=CAVE, only_map=True, own=True),  # a living NPC of the cave for the poison; F8 replaces the session
    "item-aim": _needs(sector=TOWN), "disarm": _needs(), "creature-item": _needs(), "movies": _needs(), "trait-body": _needs(), "weapon-art": _needs(), "body-styles": _needs(),
    "perf": _needs(**_MAIN_ONLY, sector=TOWN, only_sector=True, own=True),  # needs a map exit in the window
}
# The stages that run from the main menu (--main-menu, which starts without a hero).
MENU_STAGES = frozenset(stage for stage, needs in STAGE_NEEDS.items() if needs.menu)
# The stages that need the viewer's debug mode: their run gets --debug-mode.
DEBUG_STAGES = frozenset(stage for stage, needs in STAGE_NEEDS.items() if needs.debug)
KNOWN_FAILURES = Path(__file__).with_name("smoke_known_failures.json")
KNOWN_STAGE = re.compile(r"^[a-z][a-z0-9-]*$")
KNOWN_TASK = re.compile(r"^(ARC-[A-Z]+-\d{3,}|unassigned)$")


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
    requires: tuple[str, ...] = ()  # the map smoke stages whose PASS line the log must hold (ARC-DEV-063)
    again: Callable[[str], "Step"] | None = None  # ARC-DEV-396: one stage of this step as a step of its own (--retry)
    estimate: float | None = None  # ARC-DEV-390: the seconds the step is expected to take (None: its kind's default)
    custom: bool = False  # ARC-DEV-2223: the run was pinned (--map, --hero, --sector, --main-menu, --env, --no-config-check): it does not count for the PR floor


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
    return (f"^ARC-DEV-059 viewer start mode=area map={re.escape(map_name or '-')} "
            f"main-menu={main_menu} smoke=True$")


def godot_command(godot: str, prefs: Path, viewer: Sequence[str], environ: Mapping[str, str], *,
                  stages: Sequence[str] = (), extra: Sequence[tuple[str, str]] = (), headless: bool = False) -> tuple[str, ...]:
    """`env` that drops the owner's variables and sets the smoke's own, then Godot on the source project
    (godot_canonical: the sentinel, the pinned SDK and a plain PATH). ARC-DEV-403: a window run also loads the focus
    guard when the plan carries it (focus_guard.carry), so its window never takes the focus."""
    sdk = run_queue.pinned_sdk(dict(environ))
    dropped = sorted(key for key in environ if key.startswith(DROPPED_PREFIXES) and key not in KEPT_VARIABLES
                     and key != focus_guard.CARRIER)
    settings = [f"{name}_TEST_PATH={prefs / settings_file(name)}" for name in SETTINGS]
    return ("env", *(part for key in dropped for part in ("-u", key)), "__GODOT_SHELL_ENV_SET=1", f"DOTNET_ROOT={sdk}",
            f"PATH={sdk}:/usr/bin:/bin:/usr/sbin:/sbin", *(() if headless else focus_guard.insert_arguments(focus_guard.reads(environ))),
            *settings, *([f"{STAGES_ENV}={','.join(stages)}"] if stages else []), *(f"{name}={value}" for name, value in extra),
            godot, *(["--headless"] if headless else []), "--path", str(VIEWER), "--", *viewer)


def build_steps(environ: Mapping[str, str], godot: str, *, build: bool, imported: bool) -> list[Step]:
    steps = []
    if build:
        steps.append(Step("build", "dotnet", ("sh", "-c", f"dotnet restore {SOLUTION} --locked-mode -v q && "
                                                          f"dotnet build {SOLUTION} --no-restore -v q --nologo"), gate=True,
                          estimate=BUILD_SECONDS))
    if not imported:
        sdk = run_queue.pinned_sdk(dict(environ))
        # ARC-DEV-390: a quick run (beside the Godot slot, not in it): importing this checkout's own project folder
        steps.append(Step("import", "quick", ("env", "__GODOT_SHELL_ENV_SET=1", f"DOTNET_ROOT={sdk}",
                                                 f"PATH={sdk}:/usr/bin:/bin:/usr/sbin:/sbin", godot, "--headless",
                                                 "--path", str(VIEWER), "--import"), gate=True, estimate=IMPORT_SECONDS,
                          timeout=IMPORT_MINUTES))
    return steps


def crash_site_step(inputs: dict, conf: dict, prefs_root: Path, environ: Mapping[str, str], godot: str, hero: str,
                    extra: Sequence[tuple[str, str]] = ()) -> Step:
    viewer = ["--native-smoke", *launch.terrain_arguments(inputs, "area", task=CRASH_SITE_TASK, hero=hero,
                                                            verified=launch.verified_roles(conf))]
    return Step("crash-site", "window", godot_command(godot, prefs_root / "crash-site", viewer, environ, extra=extra),
                start_line(None), settings="crash-site", estimate=CRASH_SITE_SECONDS)


def map_step(name: str, inputs: dict, prefs_root: Path, environ: Mapping[str, str], godot: str, map_name: str, *,
             hero: str | None, sector: str | None, stages: Sequence[str] = (), main_menu: bool = False,
             extra: Sequence[tuple[str, str]] = ()) -> Step:
    viewer = ["--native-smoke", *launch.map_arguments(inputs, map_name, hero=hero, sector=sector, main_menu=main_menu,
                                                      debug_mode=any(stage in DEBUG_STAGES for stage in stages))]
    return Step(name, "window", godot_command(godot, prefs_root / name, viewer, environ, stages=stages, extra=extra),
                start_line(map_name, main_menu), settings=name, timeout=None if stages else BATTERY_MINUTES,
                requires=tuple(stages) or BATTERY_STAGES,
                estimate=estimate(stages, full=any(name == ALL_ENV for name, _ in extra)))


@dataclass(frozen=True)
class Config:
    """What one viewer process fixes at launch: its map, start sector (main map only), main menu and debug mode."""
    map: str
    sector: str | None
    menu: bool = False
    debug: bool = False

    def label(self) -> str:
        labels = {None: "main", TOWN: "town", WILD: "wild", SHRINE: "shrine", FIRE: "fire"}
        if self.map == MAIN_MAP:
            base = labels.get(self.sector) or "s" + re.sub(r"[^0-9a-z]+", "x", self.sector)
        else:
            base = re.sub(r"[^a-z0-9]+", "-", self.map.lower()).strip("-") or "map"
        return base + ("-menu" if self.menu else "") + ("-debug" if self.debug else "")

    def describe(self) -> str:
        return ", ".join([f"map {self.map}", *([f"sector {self.sector}"] if self.sector else []),
                          *(["main menu"] if self.menu else []), *(["debug mode"] if self.debug else [])])


@dataclass(frozen=True)
class Pins:
    """What the request itself fixes (--map or the map of `map NAME`, --sector, --main-menu); the rest is the table's."""
    map: str | None = None
    sector: str | None = None
    menu: bool = False


@dataclass(frozen=True)
class Run:
    """One viewer process: its step name, configuration, stage list (() the whole map battery) and hero."""
    name: str
    config: Config
    stages: tuple[str, ...]
    hero: str | None


def refused(wanted: Sequence[str], pins: Pins, *, battery: bool = False, checked: bool = True) -> list[str]:
    """Why the request cannot run, before anything waits for the queue: --main-menu starts without a hero or a session,
    and (when `checked`) a stage that only passes in another map or start sector than the one the request fixes."""
    problems = []
    if pins.menu:
        heroes = [stage for stage in wanted if stage in HERO_STAGES and stage not in MENU_STAGES]
        others = [stage for stage in wanted if stage not in HERO_STAGES and stage not in MENU_STAGES]
        if heroes:
            problems.append(f"{'the whole map battery' if battery else 'stage ' + ', '.join(heroes)} needs a hero, "
                            "and --main-menu starts without one: run it in a smoke of its own")
        if others and checked:
            problems.append(f"stage {', '.join(others)} needs a session, and --main-menu starts without one: run it in a "
                            "smoke of its own")
    if not checked:
        return problems
    for stage in wanted:
        needs = STAGE_NEEDS[stage]
        map_name = pins.map or needs.map
        if not needs.takes_map(map_name):
            problems.append(f"stage {stage} does not run on map {map_name}: it needs {needs.wants()}")
        elif pins.sector is not None and not needs.takes_sector(map_name, pins.sector):
            problems.append(f"stage {stage} does not run at --sector {pins.sector}: it needs {needs.wants()}")
    return problems


def stage_config(needs: StageNeeds, pins: Pins) -> Config:
    """The configuration a stage runs in: what the request fixes, else the stage's own default."""
    map_name = pins.map or needs.map
    sector = pins.sector if pins.sector is not None else (needs.sector if map_name.casefold() == MAIN_MAP.casefold() else None)
    return Config(map_name, sector, needs.menu or pins.menu, needs.debug)


def grouped(wanted: Sequence[str], pins: Pins) -> list[tuple[Config, list[str], bool]]:
    """The stages in the fewest processes: one per configuration, in order of first appearance, with a process of its own
    for each stage that must have one; a portable stage rides in a process that exists on its own map (and a sector it
    passes in), as long as that one is no menu or debug process; the portable stages come after the others."""
    groups: list[tuple[Config, list[str], bool]] = []
    portable = []
    for stage in wanted:
        needs = STAGE_NEEDS[stage]
        config = stage_config(needs, pins)
        if needs.own:
            groups.append((config, [stage], True))
        elif needs.portable:
            portable.append((stage, needs, config))
        else:
            found = next((members for other, members, own in groups if not own and other == config), None)
            if found is None:
                groups.append((config, [stage], False))
            else:
                found.append(stage)
    for stage, needs, config in portable:
        found = next((members for other, members, own in groups if not own and not other.menu and not other.debug
                      and other.map == config.map and needs.takes_sector(other.map, other.sector)), None)
        if found is None:
            groups.append((config, [stage], False))
        else:
            found.append(stage)
    for _, members, _ in groups:
        members.sort(key=wanted.index)  # the request's order inside a process
    return groups


def map_runs(base: str, stages: Sequence[str], pins: Pins, hero: str | None, say: Callable[[str], None], *,
             checked: bool = True) -> list[Run]:
    """ARC-DEV-391 and ARC-DEV-063: the viewer runs in which every requested stage runs (the whole map battery for no
    stage list). The stages are grouped by the configuration each one needs (STAGE_NEEDS) into the fewest processes; a
    request that fixes another configuration than a stage needs is refused here. A stage that needs a hero gets the
    full chain's hero of its map, else AUTO_HERO, when none is given; the stages the viewer runs only without `map` get a
    run of their own when `map` is asked for as well."""
    wanted = tuple(stages) or ("map",)
    problems = refused(wanted, pins, battery=not stages, checked=checked)
    if problems:
        raise ValueError("; ".join(problems) + ("" if not checked else " (the stage table is STAGE_NEEDS in "
                         "tools/local_dev/smoke.py; --no-config-check runs the request as given)"))
    parts: list[tuple[Config, tuple[str, ...], bool, bool]] = []
    for config, members, own in grouped(wanted, pins):
        alone = tuple(stage for stage in members if stage in ALONE_STAGES) if "map" in members and not own else ()
        if alone:
            parts.append((config, tuple(stage for stage in members if stage not in ALONE_STAGES), own, False))
            parts.append((config, alone, own, True))
        else:
            parts.append((config, tuple(members), own, False))
    configs = {config for config, _, _, _ in parts}
    runs = []
    for config, members, own, alone in parts:
        needs = [stage for stage in members if stage in HERO_STAGES and not config.menu]
        run_hero = hero if not config.menu or pins.menu else None
        if needs and run_hero is None:
            run_hero = next((chain_hero for _, chain_map, chain_hero, _ in FULL_CHAIN if chain_map == config.map), AUTO_HERO)
            say(f"SMOKE hero {run_hero} (auto): {', '.join(needs)} need a hero; --hero picks another")
        label = "-".join([*([config.label()] if len(configs) > 1 else []), *(members if own else ()),
                          *(["alone"] if alone else [])])
        runs.append(Run(f"{base}-{label}" if label else base, config, tuple(members) if stages else (), run_hero))
        if alone:
            say(f"SMOKE run {runs[-1].name} for {', '.join(members)}: the viewer runs them only without map")
        elif len(parts) > 1:
            say(f"SMOKE run {runs[-1].name} on {config.describe()}: {', '.join(members)}")
    return runs


def pinned_run(args: argparse.Namespace) -> bool:
    """ARC-DEV-2223: whether the request fixes what the default run chooses (a map, hero, sector, main menu, extra variables,
    no configuration check, or a `map NAME` / crash-site run): such a run is not the run of the stage the floor asks for."""
    return bool(getattr(args, "map", None) or getattr(args, "hero", None) or getattr(args, "sector", None)
                or getattr(args, "main_menu", False) or getattr(args, "env", None) or getattr(args, "no_config_check", False)
                or getattr(args, "name", None))


def plan(args: argparse.Namespace, conf: dict, prefs_root: Path, environ: Mapping[str, str], imported: bool,
         say: Callable[[str], None] = lambda line: print(line, flush=True), focus_lib: Path | None = None) -> list[Step]:
    """Every step of this smoke run, in order (each marked `custom` when the request pinned it, see pinned_run)."""
    steps = plan_steps(args, conf, prefs_root, environ, imported, say, focus_lib)
    return [dataclasses.replace(step, custom=True) for step in steps] if pinned_run(args) else steps


def plan_steps(args: argparse.Namespace, conf: dict, prefs_root: Path, environ: Mapping[str, str], imported: bool,
               say: Callable[[str], None], focus_lib: Path | None) -> list[Step]:
    """Every step of this smoke run, in order. `focus_lib` is the built focus guard the window steps load (ARC-DEV-403)."""
    environ = focus_guard.carry(dict(environ), focus_lib)
    godot = environ.get("GODOT") or DEFAULT_GODOT
    inputs = conf.get("inputs", {})
    extra = parse_env(getattr(args, "env", None) or ())
    checked = not getattr(args, "no_config_check", False)
    wants_all = getattr(args, "all", False)
    steps = build_steps(environ, godot, build=not args.no_build, imported=imported)

    asked = getattr(args, "retry", None)

    def retry() -> bool:  # the nightly run (and the complete zone) retries, a PR run does not unless asked
        return (args.target == "complete" or smoke_zones.COMPLETE in getattr(args, "zones_chosen", ())) if asked is None else asked

    def stage_steps(base: str, stages: Sequence[str], pins: Pins, env: Sequence[tuple[str, str]] | None = None) -> None:
        def one(run: Run, name: str, members: Sequence[str]) -> Step:
            return map_step(name, inputs, prefs_root, environ, godot, run.config.map, hero=run.hero,
                            sector=run.config.sector, stages=members, main_menu=run.config.menu,
                            extra=extra if env is None else env)

        def alone(run: Run, stage: str) -> Step:
            step = one(run, f"{run.name}-retry-{stage}", (stage,))
            if step.settings:
                (prefs_root / step.settings).mkdir(exist_ok=True)  # made now: the run's folders were made before it started
            return step

        for run in map_runs(base, stages, pins, getattr(args, "hero", None), say, checked=checked):
            step = one(run, run.name, run.stages)
            steps.append(dataclasses.replace(step, again=lambda stage, run=run: alone(run, stage))
                         if retry() and len(run.stages) > 1 else step)

    if args.target == "self-test":
        # ARC-DEV-390: the headless self-test has no window and ends in seconds: a quick run beside the Godot slot
        kind = "quick" if args.headless else "window"
        steps.append(Step("self-test", kind,
                          godot_command(godot, prefs_root / "self-test", ["--self-test"], environ, extra=extra,
                                        headless=args.headless), settings="self-test", estimate=SELF_TEST_SECONDS[kind]))
        return steps
    if args.target == "full":
        steps.append(crash_site_step(inputs, conf, prefs_root, environ, godot, CRASH_SITE_HERO))
        steps.extend(map_step(name, inputs, prefs_root, environ, godot, map_name, hero=hero, sector=sector)
                     for name, map_name, hero, sector in FULL_CHAIN)
        return steps
    if args.target == "crash-site":
        steps.append(crash_site_step(inputs, conf, prefs_root, environ, godot, args.hero or CRASH_SITE_HERO, extra))
        return steps
    known = map_stages()
    stages = parse_stages(getattr(args, "stages", None), known)

    def complete_chain() -> None:
        steps.append(crash_site_step(inputs, conf, prefs_root, environ, godot, CRASH_SITE_HERO))
        steps.extend(map_step(name, inputs, prefs_root, environ, godot, map_name, hero=hero, sector=sector)
                     for name, map_name, hero, sector in FULL_CHAIN)
        # ARC-DEV-395: the complete run is the one that vouches for everything, so its sampled stages run in full.
        stage_steps("stages", smoke_zones.complete_stages(known), Pins(), (*extra, (ALL_ENV, "1")))

    if args.target == "complete":
        complete_chain()
        return steps
    if args.target == "pr":
        prepared = len(steps)
        zones = smoke_zones.parse_zones(getattr(args, "zones", None))
        if getattr(args, "auto", False):
            base = getattr(args, "base", None) or "origin/main"
            mark, files = smoke_zones.changed_files(base)
            selection = smoke_zones.select_zones(files, lambda path: smoke_zones.changed_lines(mark, path))
            for line in smoke_zones.describe(selection, mark, base):
                say(line)
            # ARC-DEV-2223: a PR takes `complete` as core + Crash Site (the floor smoke_verify checks); `--zones complete` by hand
            # still runs it all
            zones = tuple(dict.fromkeys((*zones, *selection.pr_zones)))
            if selection.click_walk:  # the mandatory click and walk rule: the `map` stage here, `self-test` its own command
                stages = tuple(dict.fromkeys((*stages, "map")))
            if not zones and not stages and not args.crash_site:
                return []  # no smoke covers the diff: nothing to run, not even the build
        pinned = args.map is not None or args.hero or args.sector or args.main_menu or extra
        if zones and pinned and (not stages or smoke_zones.COMPLETE in zones):
            raise ValueError("--map, --hero, --sector, --main-menu and --env go with --stages, not with the zones (the "
                             "zones run on the configuration each stage needs; add --stages for the pinned ones)")
        args.zones_chosen = zones
        if smoke_zones.COMPLETE in zones:
            complete_chain()
            return steps
        if args.crash_site or smoke_zones.CRASH_SITE in zones:
            steps.append(crash_site_step(inputs, conf, prefs_root, environ, godot, CRASH_SITE_HERO))
        zoned = smoke_zones.stages_of(zones, known)
        if wants_all:
            # ARC-DEV-395: the sampled stages of this run (named or of a zone) run in full.
            if SAMPLED_STAGES & {*zoned, *stages}:
                extra = (*extra, (ALL_ENV, "1"))
            elif getattr(args, "auto", False):
                say(f"SMOKE --all ignored: no {' or '.join(sorted(SAMPLED_STAGES))} run is selected")
            else:
                raise ValueError(f"--all goes with a run of {' or '.join(sorted(SAMPLED_STAGES))} (--stages or the zone of the same name)")
        if pinned and zoned:  # the pins hold for the named stages only, the zones' stages keep their own configurations
            stage_steps("zones", tuple(stage for stage in zoned if stage not in stages), Pins())
        else:
            stages = tuple(dict.fromkeys((*zoned, *stages)))
        if stages:
            stage_steps("stages", stages, Pins(args.map, normal_sector(args.sector), args.main_menu))
        elif pinned:
            raise ValueError("--map, --hero, --sector, --main-menu and --env go with --stages (the touched stages)")
        if len(steps) == prepared:
            raise ValueError("nothing to run: give --stages (the touched stages) or --crash-site")
        return steps
    if wants_all:
        if not SAMPLED_STAGES & set(stages):
            raise ValueError(f"--all goes with --stages naming {' or '.join(sorted(SAMPLED_STAGES))} (the sampled stages)")
        extra = (*extra, (ALL_ENV, "1"))
    stage_steps("map", stages, Pins(args.name, normal_sector(args.sector), args.main_menu))
    return steps


def shown(command: Sequence[str]) -> str:
    return " ".join(part if part and not re.search(r"[\s'\"\\$`]", part) else "'" + part.replace("'", "'\\''") + "'"
                    for part in command)


def stages_not_run(log: Path | None, stages: Sequence[str]) -> tuple[list[str], list[str]]:
    """The requested stages whose PASS line (or an also-required line, STAGE_ALSO_PASS) the log lacks, and up to three NONE/SKIP lines of the log (the viewer's
    reasons), those with a missing stage's task code first."""
    try:
        text = log.read_text(encoding="utf-8", errors="replace") if log is not None else ""
    except OSError:
        text = ""
    missing = [stage for stage in stages if any(line not in text for line in (STAGE_PASS[stage], *STAGE_ALSO_PASS.get(stage, ())))]
    codes = tuple(line.split()[0] + " " for stage in missing for line in (STAGE_PASS[stage], *STAGE_ALSO_PASS.get(stage, ())))
    skips = [line.strip()[:200] for line in text.splitlines() if missing and SKIP_LINE.search(line)]
    return missing, sorted(skips, key=lambda line: not line.startswith(codes))[:3]


@dataclass(frozen=True)
class KnownFailure:
    """ARC-DEV-391: a failure seen on clean `origin/main`: the stage ("crash-site" for the Crash Site smoke), the line its
    log holds when it fails this way (a regex), the task that owns it and the day it was recorded."""
    stage: str
    symptom: re.Pattern[str]
    task: str
    since: str


def parse_known_failures(raw: object) -> tuple[KnownFailure, ...]:
    """The entries of smoke_known_failures.json; a malformed one raises ValueError (the tests read the file with this)."""
    if not isinstance(raw, list):
        raise ValueError("the known failures file holds a list of entries")
    entries = []
    for item in raw:
        if not isinstance(item, dict) or set(item) != {"stage", "symptom", "task", "since"} \
                or not all(isinstance(value, str) and value for value in item.values()):
            raise ValueError(f"a known failure has exactly the text fields stage, symptom, task, since: {item!r}")
        if item["stage"] != "crash-site" and item["stage"] not in STAGE_PASS:
            raise ValueError(f"known failure of an unknown stage {item['stage']!r}")
        if not KNOWN_TASK.match(item["task"]) or not re.match(r"^\d{4}-\d{2}-\d{2}$", item["since"]):
            raise ValueError(f"a known failure names its task (ARC-TYPE-NNN or unassigned) and its day: {item!r}")
        try:
            symptom = re.compile(item["symptom"])
        except re.error as error:
            raise ValueError(f"a known failure's symptom is not a regular expression: {item['symptom']!r} ({error})") from error
        entries.append(KnownFailure(item["stage"], symptom, item["task"], item["since"]))
    return tuple(entries)


def load_known_failures(path: Path = KNOWN_FAILURES) -> tuple[KnownFailure, ...]:
    """The known failures on main; none when the file is missing or malformed (a smoke must never fail on it)."""
    try:
        return parse_known_failures(json.loads(path.read_text(encoding="utf-8")))
    except (OSError, ValueError):
        return ()


def known_on_main(known: Sequence[KnownFailure], step: Step, log: Path | None) -> tuple[list[KnownFailure], list[str]]:
    """For a failed step: the known failures its log shows (the stage did not pass and its symptom line is there), and
    the requested stages that did not pass for another reason (or none shown)."""
    try:
        text = log.read_text(encoding="utf-8", errors="replace") if log is not None else ""
    except OSError:
        text = ""
    failing = stages_not_run(log, step.requires)[0] if step.requires else ["crash-site"] if step.name == "crash-site" else []
    # the whole battery stops at its first failing stage, so the stages after the known one cannot have run: the rest follows it
    battery = step.requires == BATTERY_STAGES
    hits = [entry for entry in known if (entry.stage in failing or battery and entry.stage in BATTERY_ALSO)
            and entry.symptom.search(text)]
    return hits, [] if battery and hits else [stage for stage in failing if stage not in {entry.stage for entry in hits}]


def execute(steps: Sequence[Step], runner: Callable[..., int] = run_queue.run,
            say: Callable[[str], None] = lambda line: print(line, flush=True),
            known: Sequence[KnownFailure] | None = None) -> int:
    """Run the steps through the queue (each quiet, start checked); 0 when every one passed and printed the PASS line
    of each stage it was asked for. A failed step whose log shows a failure listed in smoke_known_failures.json says so
    (known on main, not this PR); it still fails, and the exit code is the same."""
    known = load_known_failures() if known is None else known
    results: list[tuple[str, int]] = []
    only_known: list[str] = []
    timed: list[dict] = []  # ARC-DEV-399: the stage and queue times of each step that ran, for the summary

    def run_step(step: Step) -> tuple[int, Path | None]:
        say(f"SMOKE step {step.name} kind={step.kind}")
        if step.kind == "window":  # ARC-DEV-2272: the queue holds a window run while the owner plays; say so where the smoke is read
            playing = run_queue.playing_state()
            if playing is not None:
                say(f"SMOKE waiting: owner is playing ({run_queue.playing_text(playing)}); the window starts when it ends: "
                    "./arcanum queue --not-playing")
        before = smoke_history.work_tree()  # ARC-DEV-2223: the files the step starts on (an edit while it waits or runs is not covered)
        check = start_check.for_run(step.kind, step.expect, None, step.command)
        logs: list[Path] = []
        code = runner(step.kind, list(step.command), label=f"smoke {step.name}", timeout=step.timeout, quiet=True,
                      start=check, log_sink=logs.append, estimate=step.estimate)
        if code == 0 and step.requires:
            missing, reasons = stages_not_run(logs[-1] if logs else None, step.requires)
            if missing:
                say(f"  FAIL exit={STAGE_NOT_RUN} stages not run: {', '.join(missing)} "
                    f"(no PASS line in {logs[-1] if logs else 'the log'})")
                for reason in reasons:
                    say(f"  viewer: {reason}")
                code = STAGE_NOT_RUN
        known_only = False
        if known and 0 != code < STOPPED and not step.gate:
            hits, others = known_on_main(known, step, logs[-1] if logs else None)
            if hits:
                say("  known on main, not this PR: " + ", ".join(f"{entry.stage} ({entry.task}, since {entry.since})"
                                                                 for entry in hits))
                if others:
                    say(f"  not run for another reason: {', '.join(others)}")
                else:
                    only_known.append(step.name)
                    known_only = True
        try:  # ARC-DEV-399: what the step ran and each stage's time, into the queue folder; never changes the result
            record = smoke_history.entry_of(step.name, step.kind, step.requires, code, logs[-1] if logs else None,
                                            tree=lambda: before)
            if record is not None:
                record["custom"] = step.custom  # ARC-DEV-2223: what smoke_verify needs to tell the default run from a pinned one
                record["knownOnly"] = known_only
                if code != 0 and step.requires:  # the stages a failed process still passed before it stopped (smoke_verify credits them)
                    missing = stages_not_run(logs[-1] if logs else None, step.requires)[0]
                    record["passed"] = [stage for stage in step.requires if stage not in missing]

                smoke_history.append(record)
                timed.append(record)
        except Exception as error:  # noqa: BLE001 - a record must never fail the smoke, whatever a history line held
            say(f"  note: the stage times of {step.name} were not recorded: {error}")
        results.append((step.name, code))
        return code, logs[-1] if logs else None

    order_dependent: dict[str, list[str]] = {}  # ARC-DEV-396: a failed group whose every unfinished stage passed alone
    done = 0
    for step in steps:
        code, log = run_step(step)
        done += 1
        if code >= STOPPED or (code != 0 and step.gate):
            break
        # ARC-DEV-396: a smoke process ends at its first failing stage, so the stages after it never ran, and a stage that
        # passes alone may fail only after the others (state it leaves behind). The unfinished stages run one to a process.
        if code == start_check.FAILED_START_EXIT and step.again is not None:
            say("  not retried one to a process: the viewer did not start, which says nothing about its stages")
        elif code != 0 and step.again is not None:
            unfinished = stages_not_run(log, step.requires)[0]
            if unfinished:
                say(f"  retrying {len(unfinished)} unfinished stage(s) one to a process: {', '.join(unfinished)}")
            alone_failed = False
            for stage in unfinished:
                retry_code, _ = run_step(step.again(stage))
                if retry_code >= STOPPED:
                    code = retry_code
                    break
                alone_failed = alone_failed or retry_code != 0
                say(f"  {stage}: " + (f"passes alone, so it fails only after the other stages of {step.name}"
                                if retry_code == 0 else f"fails alone too (exit {retry_code})"))
            if code >= STOPPED:
                break
            # a timeout is not a sign of order: the hung stage would pass alone only by luck (and the retry is not counted then)
            if unfinished and not alone_failed and code != run_queue.TIMEOUT_EXIT:
                order_dependent[step.name] = list(unfinished)
    width = max(len(name) for name, _ in results) if results else 0
    say("SMOKE summary")
    for name, code in results:
        say(f"  {name:<{width}}  {'ORDER' if name in order_dependent else 'PASS' if code == 0 else 'FAIL'} exit={code}")
    for name, stages in order_dependent.items():  # a warning, not a failure: every stage passed alone (owner decision 2026-11-12)
        say(f"  order-dependent: {name} (fails only after other stages of its process; each of {', '.join(stages)} passes alone)")
    skipped = [step.name for step in steps[done:]]
    if skipped:
        say("  not run: " + ", ".join(skipped))
    if timed:
        say("SMOKE times (queue wait, run time, each stage; the history is smoke-stages.jsonl in the queue folder)")
        for record in timed:
            for line in smoke_history.table(record):
                say(line)
    failed = [name for name, code in results if code != 0 and name not in order_dependent]
    only_known = [name for name in only_known if name not in order_dependent]
    if only_known:
        say("  only known failures on main (not this PR): " + ", ".join(only_known))
    say(f"SMOKE {'PASS' if not failed and not skipped else 'FAIL'} {len(results) - len(failed)} of {len(results) + len(skipped)} steps passed"
        + (f" ({len(order_dependent)} order-dependent, each stage passes alone)" if order_dependent else "")
        + (f" ({len(only_known)} of the {len(failed)} failed with only known failures on main)" if only_known else ""))
    stopped = [code for _, code in results if code >= STOPPED]
    return stopped[0] if stopped else (1 if failed or skipped else 0)


def focus_guard_target(args: argparse.Namespace, prefs_root: Path) -> Path | None:
    """ARC-DEV-403: where the focus guard library of this run goes (in the run's settings folder), None when this run does
    not use one (--take-focus, off macOS)."""
    if getattr(args, "take_focus", False) or not focus_guard.supported():
        return None
    return prefs_root / "focus-guard" / focus_guard.LIBRARY


def focus_guard_library(steps: Sequence[Step], target: Path | None,
                        build: Callable[[Path], tuple[Path | None, str]] | None = None,
                        say: Callable[[str], None] = lambda line: print(line, flush=True)) -> Path | None:
    """The focus guard library the window smokes load: built at `target` when some step is a window run, None (with a
    note: the smoke windows then take the focus as before) when it cannot be built. Nothing is built for a run with no
    window step."""
    if target is None or not any(step.kind == "window" for step in steps):
        return None
    library, why = (build or focus_guard.build)(target)
    if library is None:
        say(f"SMOKE note: {why}; the smoke windows may take the focus")
    return library


def red_on_main_note(args: argparse.Namespace) -> str | None:
    """ARC-DEV-2226: the flag of ./arcanum main-smoke, printed by a PR run (`smoke pr`) while main's rolling smoke is red."""
    if getattr(args, "target", None) != "pr":
        return None
    flag = red_flag.read()
    return red_flag.text(flag) if flag else None


def run(args: argparse.Namespace) -> int:
    if args.target == "verify-pr":  # ARC-DEV-2223: reads git and the journal; no smoke, no queue place
        from . import smoke_verify
        return smoke_verify.run(args)
    note = red_on_main_note(args)
    if note:
        print(note, flush=True)
    code = run_smoke(args)
    if note and code != 0:
        print(note, flush=True)  # again after a failure, where the summary is read
    return code


def run_smoke(args: argparse.Namespace) -> int:
    environ = dict(os.environ)
    if environ.get(run_queue.TICKET_ENV):
        raise ValueError("run ./arcanum smoke on its own, not inside ./arcanum run: each smoke takes its own place in "
                         "the queue and checks its own start")
    prefs_root = Path(tempfile.mkdtemp(prefix="arcanum-smoke-"))
    keep = False
    try:
        target = focus_guard_target(args, prefs_root)
        conf, imported = config.load_config(), (ROOT / VIEWER / ".godot").is_dir()
        steps = plan(args, conf, prefs_root, environ, imported=imported, focus_lib=target)
        if not steps:
            print("SMOKE PASS nothing to run: no smoke zone covers this diff", flush=True)
            return 0
        zones = getattr(args, "zones_chosen", ())
        if args.dry_run:
            for step in steps:
                print(f"{step.name} [{step.kind}] {shown(step.command)}")
                if step.expect:
                    print(f"  start line: {step.expect}")
            return 0
        godot = environ.get("GODOT") or DEFAULT_GODOT
        if not os.access(godot, os.X_OK):
            raise ValueError(f"no Godot at {godot} (set GODOT)")
        if target is not None and focus_guard_library(steps, target) is None:
            # not built (no window step, or it failed with a note): plan again without the guard
            steps = plan(args, conf, prefs_root, environ, imported=imported, say=lambda line: None, focus_lib=None)
        os.chdir(ROOT)  # the steps name the viewer project and solution relative to the checkout
        for step in steps:
            if step.settings:
                (prefs_root / step.settings).mkdir()
        print(f"SMOKE settings folder {prefs_root}", flush=True)
        code = (execute(steps, runner=functools.partial(run_queue.run, priority=run_queue.LOW))
                if getattr(args, "low_priority", False) else execute(steps))
        if zones:
            print(f"SMOKE zones: {', '.join(zones)}", flush=True)  # the PR report names them
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
        parser.add_argument("--no-config-check", action="store_true", help="do not refuse a stage given a map or sector it "
                            "does not pass in (STAGE_NEEDS); the stages are still grouped by configuration")
        parser.add_argument("--take-focus", action="store_true", help="let the smoke windows take the focus as before "
                            "(by default they are background-only on macOS, never active; ARC-DEV-403)")
        parser.add_argument("--low-priority", action="store_true", help="queue every step behind the ordinary runs "
                            "(./arcanum main-smoke uses it, ARC-DEV-2226; a PR run never needs it)")

    def map_options(parser: argparse.ArgumentParser) -> None:
        parser.add_argument("--hero", choices=launch.PREMADE_HEROES, help="start as this pre-made hero")
        parser.add_argument("--sector", help="X,Y: open the map at this sector")
        parser.add_argument("--main-menu", action="store_true", help="open the main menu first (not with --hero)")
        parser.add_argument("--env", action="append", metavar="NAME=VALUE",
                            help="one more variable for the viewer (a capture or probe switch); repeatable")
        parser.add_argument("--all", action="store_true", help="the sampled stages in full: container-click clicks every "
                            "container, walk goes the whole route (default: a sample, a fifth to a third of the time)")

    stages = "comma list of map smoke stages (ARC_AREA_MAP_SMOKE_ONLY; the viewer's MapSmokeStageNames)"
    pr = targets.add_parser("pr", help="the per-PR scope: only the map smoke stages the change touches or is directly "
                           "related to (--stages), and the Crash Site smoke only when related (--crash-site)")
    pr.add_argument("--stages", help=stages)
    pr.add_argument("--zones", help="comma list of smoke zones to run (" + ", ".join(smoke_zones.CHOICES) + "; "
                    "docs/workflow/toolchain.md#smoke-zones-arc-dev-396), with or without --stages")
    pr.add_argument("--auto", action="store_true", help="pick the zones from the diff against the merge base with --base: "
                    "prints each zone and the files that chose it; a diff no zone covers runs nothing")
    pr.add_argument("--base", help="the ref --auto diffs against (default origin/main; fetch it first)")
    retry = argparse.BooleanOptionalAction
    pr.add_argument("--retry", action=retry, default=None, help="after a failed stage process, run its unfinished stages one to "
                    "a process, so no stage after the failing one goes unchecked and a stage that fails only after the "
                    "others is named (default: off for pr, on for complete)")
    pr.add_argument("--map", help=f"the map the stages run on (default: the map each stage needs, {MAIN_MAP} for most)")
    map_options(pr)
    pr.add_argument("--crash-site", action="store_true", help="also the Crash Site smoke first, when the change is related "
                    "to it (the pinned exterior, the cave, their inputs)")
    common(pr)
    full = targets.add_parser("full", help="the full map smoke chain: the Crash Site smoke, then the whole map battery "
                              "on the main map at the Crash Site, the town and the wilderness, and on elven ruins")
    common(full)
    complete = targets.add_parser("complete", help="the nightly run (./arcanum daily): the full chain, then every stage the "
                                  "viewer accepts, grouped by configuration; core plus all heavy zones")
    complete.add_argument("--retry", action=retry, default=None, help="retry the unfinished stages of a failed process one to a "
                          "process (default on)")
    common(complete)
    one = targets.add_parser("map", help="one map's smoke: every default stage, or --stages")
    one.add_argument("name", help='the map folder name, e.g. "crash site cave" or Arcanum1-024-fixed')
    one.add_argument("--stages", help=stages)
    map_options(one)
    common(one)
    crash = targets.add_parser("crash-site", help="the Crash Site smoke alone: the pinned exterior regression with the "
                               "cave (area mode without --area-map)")
    crash.add_argument("--hero", choices=launch.PREMADE_HEROES, help=f"the pre-made hero (default {CRASH_SITE_HERO})")
    crash.add_argument("--env", action="append", metavar="NAME=VALUE", help="one more variable for the viewer; repeatable")
    common(crash)
    verify = targets.add_parser("verify-pr", help="the pre-merge check (ARC-DEV-2223): every smoke stage `pr --auto` selects for "
                                "the diff against --base (and the self-test and the map stage for click or walk changes) has a "
                                "passing run of this checkout on the files as they are; runs no smoke, exit 1 when one is missing")
    verify.add_argument("--base", help="the ref the diff is taken against (default origin/main; fetch it first)")
    test = targets.add_parser("self-test", help="the viewer's synthetic --self-test (no game data)")
    test.add_argument("--headless", action="store_true", help="Godot --headless (default: with a window)")
    test.add_argument("--env", action="append", metavar="NAME=VALUE", help="one more variable for the viewer; repeatable")
    common(test)
