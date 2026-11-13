"""ARC-DEV-061: ./arcanum smoke plans the per-PR scope and the full map smoke chain and runs each smoke through the queue;
ARC-DEV-063: it passes only when every requested stage printed its PASS line."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import io
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from tools.local_dev import launch, run_queue, smoke, start_check

REPO = Path(__file__).resolve().parents[2]
ENVIRON = {"HOME": "/Users/x", "PATH": "/usr/bin", "DOTNET_ROOT": "/sdk", "DAT_ROOT": "/game", "ARC_HD_PACK": "/hd",
           "ARC_AREA_MAP_SMOKE_ONLY": "walk", "ARCANUM_EXE": "/exe", "VISUAL_PROFILE_TEST_PATH": "/owner/v.json",
           "LAUNCHER_SETTINGS_TEST_PATH": "/owner/l.json", run_queue.TICKET_ENV: "7", run_queue.KIND_ENV: "window"}


def parse(*argv: str) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="arcanum smoke")
    smoke.configure(parser)
    return parser.parse_args(argv)


class PlanTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        inputs = {"sourceId": "public-fixture"}
        for role, (_, size, _) in launch.GAME_ROLES.items():
            path = self.root / "roles" / role
            if size is None:
                path.mkdir(parents=True)
            else:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"public fixture")
            inputs[role] = str(path)
        self.conf = {"inputs": inputs}
        self.prefs = self.root / "prefs"
        self.said: list[str] = []

    def tearDown(self) -> None:
        self.temp.cleanup()

    def plan(self, *argv: str, imported: bool = True) -> list[smoke.Step]:
        return smoke.plan(parse(*argv), self.conf, self.prefs, ENVIRON, imported, say=self.said.append)

    def test_the_stage_names_come_from_the_viewer(self) -> None:
        stages = smoke.map_stages()
        for name in ("map", "stream", "cast", "life", "menu", "ai-turn-based"):
            self.assertIn(name, stages)
        self.assertEqual(smoke.parse_stages(" cast,life,cast ", stages), ("cast", "life"))
        with self.assertRaisesRegex(ValueError, "unknown map smoke stage lfie"):
            smoke.parse_stages("cast,lfie", stages)
        with self.assertRaisesRegex(ValueError, "unknown map smoke stage"):
            smoke.parse_stages(",", stages)

    def test_pr_with_crash_site_is_the_build_the_crash_site_smoke_and_the_touched_stages(self) -> None:
        steps = self.plan("pr", "--stages", "cast,life", "--hero", "human-male", "--env", "ARC_AREA_LIFE_CAPTURE=/x",
                          "--crash-site")
        self.assertEqual([(step.name, step.kind, step.gate) for step in steps],
                         [("build", "dotnet", True), ("crash-site", "window", False), ("stages", "window", False)])
        self.assertIn("dotnet restore tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln --locked-mode", steps[0].command[2])
        crash, stages = steps[1].command, steps[2].command
        self.assertIn("--native-smoke", crash)
        self.assertNotIn("--area-map", crash)
        self.assertIn("--area-cave-mobile", crash)
        self.assertEqual(crash[crash.index("--hero") + 1], smoke.CRASH_SITE_HERO)
        self.assertEqual(stages[stages.index("--area-map") + 1], smoke.MAIN_MAP)
        self.assertEqual(stages[stages.index("--hero") + 1], "human-male")
        self.assertIn("ARC_AREA_MAP_SMOKE_ONLY=cast,life", stages)
        self.assertIn("ARC_AREA_LIFE_CAPTURE=/x", stages)
        self.assertNotIn("ARC_AREA_LIFE_CAPTURE=/x", crash)  # --env goes with the stages
        self.assertFalse(any(part.startswith("ARC_AREA_MAP_SMOKE_ONLY=") for part in crash))

    def test_each_smoke_has_its_own_settings_and_none_of_the_owners_variables(self) -> None:
        steps = self.plan("pr", "--stages", "cast", "--crash-site")
        for step in steps[1:]:
            command = list(step.command)
            self.assertEqual(command[0], "env")
            dropped = {command[index + 1] for index, part in enumerate(command) if part == "-u"}
            self.assertEqual(dropped, {"DAT_ROOT", "ARC_HD_PACK", "ARC_AREA_MAP_SMOKE_ONLY", "ARCANUM_EXE",
                                       "VISUAL_PROFILE_TEST_PATH", "LAUNCHER_SETTINGS_TEST_PATH"})
            for name in smoke.SETTINGS:
                self.assertIn(f"{name}_TEST_PATH={self.prefs / step.settings / smoke.settings_file(name)}", command)
            self.assertIn("__GODOT_SHELL_ENV_SET=1", command)
            self.assertIn("PATH=/sdk:/usr/bin:/bin:/usr/sbin:/sbin", command)
            self.assertEqual(command[command.index("--path") + 1], "tools/terrain-viewer/Godot")
        self.assertEqual([step.settings for step in steps], [None, "crash-site", "stages"])

    def test_the_start_line_names_the_intended_mode_map_and_smoke(self) -> None:
        steps = self.plan("pr", "--stages", "map", "--map", "elven ruins", "--crash-site")
        crash, stages = (re.compile(step.expect or "") for step in steps[1:])
        line = "ARC-DEV-059 viewer start mode=area map={} main-menu=False smoke=True"
        self.assertTrue(crash.search(line.format("-")))
        self.assertFalse(crash.search(line.format("elven ruins")))
        self.assertTrue(stages.search(line.format("elven ruins")))
        for wrong in (line.format("-"), line.format("elven ruins 2"), line.format("elven ruins").replace("True", "False"),
                      line.format("elven ruins").replace("mode=area", "mode=world-diagnostic")):
            self.assertFalse(stages.search(wrong), wrong)
        menu = self.plan("map", "Arcanum1-024-fixed", "--main-menu", "--stages", "menu")[-1]
        self.assertTrue(re.search(menu.expect or "", line.format("Arcanum1-024-fixed").replace("main-menu=False", "main-menu=True")))
        # The expected start line goes to the queue's start check in place of the default lines.
        check = start_check.for_run("window", stages.pattern, None)
        assert check is not None
        self.assertEqual(start_check.scan(check, ["Godot Engine v4.7.2", line.format("elven ruins")])[0], "started")
        self.assertEqual(start_check.scan(check, ["ARC-DEV-059 viewer failure x | failure stage=Arguments code=Usage"])[0],
                         "failed")

    def test_pr_runs_only_the_touched_stages_and_the_crash_site_smoke_only_when_asked(self) -> None:
        self.assertEqual([step.name for step in self.plan("pr", "--stages", "cast", "--no-build")], ["stages"])
        self.assertEqual([step.name for step in self.plan("pr", "--crash-site", "--no-build")], ["crash-site"])
        with self.assertRaisesRegex(ValueError, "nothing to run"):
            self.plan("pr")
        with self.assertRaisesRegex(ValueError, "go with --stages"):
            self.plan("pr", "--hero", "elf-male")
        with self.assertRaisesRegex(ValueError, "--env takes NAME=VALUE"):
            self.plan("pr", "--stages", "cast", "--env", "ARC_AREA_MAP_SMOKE_ONLY=walk")
        for bad in ("lower=1", "PATH=/x", "DOTNET_ROOT=/x", "NOVALUE"):
            with self.assertRaisesRegex(ValueError, "--env takes NAME=VALUE"):
                self.plan("pr", "--stages", "cast", "--env", bad)

    def test_all_runs_the_sampled_stages_in_full_and_only_goes_with_them(self) -> None:
        # ARC-DEV-395: container-click and walk check a sample unless --all, which sets the viewer's ARC_SMOKE_ALL.
        sampled = self.plan("pr", "--stages", "walk", "--no-build")[-1].command
        self.assertFalse(any(part.startswith(smoke.ALL_ENV + "=") for part in sampled))
        for argv in (("pr", "--stages", "walk,cast", "--all", "--no-build"),
                     ("map", "Arcanum1-024-fixed", "--stages", "container-click", "--all", "--no-build")):
            self.assertIn(smoke.ALL_ENV + "=1", self.plan(*argv)[-1].command)
        for argv in (("pr", "--all", "--crash-site"), ("pr", "--stages", "cast", "--all"), ("pr", "--zones", "core", "--all"),
                     ("map", "Arcanum1-024-fixed", "--all")):
            with self.assertRaisesRegex(ValueError, "--all goes with"):
                self.plan(*argv)
        # a zone of a sampled stage takes --all too; the complete run always runs them in full
        zone = self.plan("pr", "--zones", "walk", "--all", "--no-build")
        self.assertTrue(any(smoke.ALL_ENV + "=1" in step.command for step in zone))
        self.assertFalse(any(smoke.ALL_ENV + "=1" in step.command for step in self.plan("pr", "--zones", "walk", "--no-build")))
        self.assertTrue(all(smoke.ALL_ENV + "=1" in step.command for step in self.plan("complete", "--no-build") if step.name.startswith("stages")))
        # with --auto the diff picks the zones, so --all is ignored (with a line) when none of them is sampled
        with mock.patch.object(smoke.smoke_zones, "changed_files", return_value=("abc", ("src/Arcanum.Session/GameSessionHeal.cs",))), \
                mock.patch.object(smoke.smoke_zones, "changed_lines", return_value=[]):
            self.said.clear()
            self.assertFalse(any(smoke.ALL_ENV + "=1" in step.command for step in self.plan("pr", "--auto", "--all", "--no-build")))
        self.assertTrue(any("--all ignored" in line for line in self.said))
        self.assertEqual(smoke.SAMPLED_STAGES, {"container-click", "walk"})
        # the viewer reads the same variable the runner sets, in the stage the walk shares it with
        viewer = REPO / "tools/terrain-viewer/Godot"
        self.assertIn(f'"{smoke.ALL_ENV}"', (viewer / "TerrainViewerContainerClickSmoke.cs").read_text(encoding="utf-8"))
        self.assertIn("SmokeAll", (viewer / "TerrainViewerAreaMapWalkSmoke.cs").read_text(encoding="utf-8"))

    def test_a_debug_stage_runs_in_the_viewers_debug_mode(self) -> None:
        debug = self.plan("pr", "--stages", "debug-travel", "--no-build")[-1]
        self.assertIn("--debug-mode", debug.command)
        self.assertNotIn("--debug-mode", self.plan("pr", "--stages", "cast", "--no-build")[-1].command)

    def test_the_full_chain(self) -> None:
        steps = self.plan("full", imported=False)
        self.assertEqual([step.name for step in steps], ["build", "import", "crash-site", "main", "town", "wild", "ruins"])
        self.assertIn("--import", steps[1].command)
        self.assertTrue(steps[1].gate)
        for step, (_, map_name, hero, sector) in zip(steps[3:], smoke.FULL_CHAIN):
            command = list(step.command)
            self.assertEqual(command[command.index("--area-map") + 1], map_name)
            self.assertEqual(command[command.index("--hero") + 1], hero)
            self.assertEqual(command[command.index("--area-map-sector") + 1] if sector else None, sector)
            self.assertFalse(any(part.startswith("ARC_AREA_MAP_SMOKE_ONLY=") for part in command))  # the whole battery
            self.assertEqual(step.timeout, smoke.BATTERY_MINUTES)
        self.assertIsNone(self.plan("pr", "--stages", "cast")[-1].timeout)

    def test_every_stage_has_its_pass_line_from_the_viewer(self) -> None:
        self.assertEqual(set(smoke.STAGE_PASS), set(smoke.map_stages()))
        sources = "\n".join(path.read_text(encoding="utf-8") for path in (REPO / smoke.VIEWER).glob("*.cs"))
        for stage, marker in smoke.STAGE_PASS.items():
            self.assertIn('"' + marker, sources.replace('$"', '"'), stage)
        known = set(smoke.map_stages())
        for group in (smoke.HERO_STAGES, smoke.MENU_STAGES, smoke.ALONE_STAGES, smoke.BATTERY_ALSO, smoke.BATTERY_STAGES):
            self.assertLessEqual(set(group), known)

    def test_the_stages_that_run_only_without_map_come_from_the_viewer(self) -> None:
        text = (REPO / smoke.VIEWER / "TerrainViewerApp.cs").read_text(encoding="utf-8")
        start, end = text.index('if (Stage("map"))'), text.index("else await RunAreaSmokeAsync(result);")
        battery, *branch = text[start:end].splitlines()
        battery_calls = set(re.findall(r"Run\w+", battery))
        alone, also = set(), set()
        for line in branch:
            named = re.search(r'(?:Stage|only\?\.Contains)\("([a-z-]+)"\)', line)
            if named is None:
                continue
            calls = re.findall(r"Run\w+", line)
            self.assertTrue(calls, line)
            (also if set(calls) <= battery_calls else alone).add(named.group(1))
        self.assertEqual((alone, also), (set(smoke.ALONE_STAGES), set(smoke.BATTERY_ALSO)))

    def test_a_stage_that_needs_a_hero_gets_one(self) -> None:
        step = self.plan("pr", "--stages", "ai", "--no-build")[0]
        self.assertEqual(step.command[step.command.index("--hero") + 1], smoke.AUTO_HERO)
        self.assertEqual(step.requires, ("ai",))
        self.assertEqual(self.said, [f"SMOKE hero {smoke.AUTO_HERO} (auto): ai need a hero; --hero picks another"])
        step = self.plan("pr", "--stages", "ai,container", "--hero", "elf-male", "--no-build")[0]
        self.assertEqual(step.command[step.command.index("--hero") + 1], "elf-male")
        self.assertNotIn("--hero", self.plan("pr", "--stages", "report", "--no-build")[0].command)
        # ARC-DEV-404: the hover stage's item window draws the lines after the name, which the original draws only for a valid local PC.
        hover = self.plan("pr", "--stages", "hover", "--no-build")[0]
        self.assertEqual(hover.command[hover.command.index("--hero") + 1], smoke.AUTO_HERO)
        # ARC-DEV-1547: transfer runs the visitor-fight sub-check, which prints NONE (no hero) while the stage's own PASS line
        # still comes, so the stage gets a hero (and is refused with --main-menu) instead of passing without the check.
        run = self.plan("pr", "--stages", "transfer", "--no-build")[0]
        self.assertEqual(run.command[run.command.index("--hero") + 1], smoke.AUTO_HERO)
        with self.assertRaisesRegex(ValueError, "stage transfer needs a hero, and --main-menu starts without one"):
            self.plan("pr", "--stages", "transfer", "--main-menu")
        battery = self.plan("map", "elven ruins", "--no-build")[0]  # the whole battery plays the hero's stages
        self.assertEqual(battery.command[battery.command.index("--hero") + 1], "elf-female")  # the chain's hero there
        cave = self.plan("map", "crash site cave", "--stages", "junk", "--no-build")[0]
        self.assertEqual(cave.command[cave.command.index("--hero") + 1], smoke.AUTO_HERO)
        self.assertEqual(battery.requires, smoke.BATTERY_STAGES)
        for full in self.plan("full")[1:]:
            self.assertIn("--hero", full.command)
        with self.assertRaisesRegex(ValueError, "stage ai, life needs a hero, and --main-menu starts without one"):
            self.plan("pr", "--stages", "ai,menu,life", "--main-menu")
        with self.assertRaisesRegex(ValueError, "the whole map battery needs a hero"):
            self.plan("map", "Arcanum1-024-fixed", "--main-menu")
        # ARC-DEV-391: a menu stage needs no --main-menu (it gets its own menu process), and with it a stage that needs a
        # hero or a session is refused
        menus = self.plan("pr", "--stages", "menu,death", "--no-build")
        self.assertEqual([(step.name, step.requires) for step in menus], [("stages-menu", ("menu",)), ("stages-death", ("death",))])
        self.assertEqual([(step.name, step.requires) for step in self.plan("pr", "--stages", "menu,death", "--main-menu", "--no-build")],
                         [("stages-menu", ("menu",)), ("stages-death", ("death",))])
        for step in menus:
            self.assertIn("--main-menu", step.command)
            self.assertNotIn("--hero", step.command)
        with self.assertRaisesRegex(ValueError, "stage hover needs a hero, and --main-menu starts without one"):
            self.plan("pr", "--stages", "menu,hover", "--main-menu")

    def test_the_perf_stages_follow_their_kind(self) -> None:
        # ARC-DEV-367: `perf` plays a hero in a map window; `perf-menu` runs from the main menu, with no hero.
        step = self.plan("pr", "--stages", "perf", "--no-build")[0]
        self.assertEqual(step.requires, ("perf",))
        self.assertEqual(step.command[step.command.index("--hero") + 1], smoke.AUTO_HERO)
        self.assertEqual(step.command[step.command.index("--area-map-sector") + 1], smoke.TOWN)  # needs a map exit in the window
        for argv in (("pr", "--stages", "perf-menu", "--main-menu", "--no-build"), ("pr", "--stages", "perf-menu", "--no-build")):
            menu = self.plan(*argv)[0]
            self.assertEqual(menu.requires, ("perf-menu",))
            self.assertIn("--main-menu", menu.command)
            self.assertNotIn("--hero", menu.command)

    def test_the_stages_that_run_only_without_map_get_their_own_run(self) -> None:
        steps = self.plan("pr", "--stages", "map,ai,cast,stream,barter", "--no-build")
        self.assertEqual([(step.name, step.requires) for step in steps],
                         [("stages", ("map", "cast", "stream")), ("stages-alone", ("ai", "barter"))])
        self.assertIn("ARC_AREA_MAP_SMOKE_ONLY=map,cast,stream", steps[0].command)
        self.assertIn("ARC_AREA_MAP_SMOKE_ONLY=ai,barter", steps[1].command)
        self.assertEqual([step.settings for step in steps], ["stages", "stages-alone"])
        self.assertIn("SMOKE run stages-alone for ai, barter: the viewer runs them only without map", self.said)
        self.assertEqual([step.name for step in self.plan("map", "x", "--stages", "map,cast", "--no-build")], ["map"])

    def test_one_map_crash_site_and_self_test(self) -> None:
        one = self.plan("map", "crash site cave", "--stages", "junk", "--no-build")
        self.assertEqual([step.name for step in one], ["map"])
        self.assertIn("ARC_AREA_MAP_SMOKE_ONLY=junk", one[0].command)
        crash = self.plan("crash-site", "--hero", "elf-female", "--no-build")[0].command
        self.assertEqual(crash[crash.index("--hero") + 1], "elf-female")
        test = self.plan("self-test", "--headless", "--no-build")[0]
        # ARC-DEV-390: the headless self-test is a quick run beside the Godot slot
        self.assertEqual((test.kind, test.expect, test.estimate), ("quick", None, smoke.SELF_TEST_SECONDS["quick"]))
        self.assertIn("--headless", test.command)
        self.assertEqual(test.command[-1], "--self-test")
        self.assertEqual(self.plan("self-test", "--no-build")[0].kind, "window")

    def test_the_import_is_a_quick_run_and_every_step_declares_its_seconds_to_the_queue(self) -> None:
        # ARC-DEV-390: an import of a new checkout does not wait for the Godot slot; the queue orders runs by their estimates.
        steps = self.plan("pr", "--stages", "cast,walk", "--crash-site", imported=False)
        kinds = {step.name: (step.kind, step.estimate) for step in steps}
        self.assertEqual(kinds["import"], ("quick", smoke.IMPORT_SECONDS))
        self.assertEqual(kinds["build"], ("dotnet", smoke.BUILD_SECONDS))
        self.assertEqual(kinds["crash-site"], ("window", smoke.CRASH_SITE_SECONDS))
        self.assertTrue(all(step.estimate for step in steps))

    def test_the_sampled_stages_are_declared_shorter_than_in_full_and_the_import_keeps_the_headless_limit(self) -> None:
        for stage in smoke.SAMPLED_STAGES:
            self.assertLess(smoke.estimate((stage,)), smoke.estimate((stage,), full=True))
        self.assertEqual(smoke.estimate(("census",)), smoke.estimate(("census",), full=True))  # not sampled
        steps = self.plan("pr", "--stages", "walk", "--all", "--no-build")
        self.assertEqual(steps[-1].estimate, smoke.estimate(("walk",), full=True))
        self.assertEqual(self.plan("pr", "--stages", "walk", "--no-build")[-1].estimate, smoke.estimate(("walk",)))
        imported = self.plan("pr", "--stages", "cast", imported=False)
        self.assertEqual(next(step for step in imported if step.name == "import").timeout, smoke.IMPORT_MINUTES)
        self.assertEqual(smoke.IMPORT_MINUTES, run_queue.DEFAULT_TIMEOUT_MINUTES["headless"])

    def test_the_estimate_of_a_stage_run_is_its_load_plus_its_stages_and_the_slow_ones_stand_out(self) -> None:
        self.assertEqual(smoke.estimate(()), smoke.BATTERY_SECONDS)
        self.assertEqual(smoke.estimate(("cast", "life")), smoke.RUN_SECONDS + 2 * smoke.STAGE_SECONDS)
        for slow in smoke.SLOW_STAGES:
            self.assertGreater(smoke.estimate((slow,)), 5 * smoke.estimate(("cast",)))
        self.assertTrue(set(smoke.SLOW_STAGES) <= set(smoke.map_stages()))

    # ARC-DEV-391: the stage-to-configuration table, grouping and the early error

    def arg(self, step: smoke.Step, name: str) -> str | None:
        return step.command[step.command.index(name) + 1] if name in step.command else None

    def test_every_stage_has_its_configuration_and_the_derived_sets_agree(self) -> None:
        self.assertEqual(set(smoke.STAGE_NEEDS), set(smoke.map_stages()))
        self.assertTrue(smoke.MENU_STAGES.isdisjoint(smoke.HERO_STAGES))  # the menu starts without a hero
        for stage, needs in smoke.STAGE_NEEDS.items():
            self.assertTrue(needs.map and (needs.sector is None or re.fullmatch(r"\d+,\d+", needs.sector)), stage)
            self.assertTrue(needs.takes_map(needs.map) and needs.takes_sector(needs.map, needs.sector), stage)  # its default runs
            self.assertFalse(needs.portable and (needs.own or needs.menu or needs.debug), stage)
            if needs.menu:
                self.assertTrue(needs.own, stage)  # each menu stage starts and ends a game of its own

    def test_stages_are_grouped_by_configuration_into_the_fewest_processes(self) -> None:
        said: list[str] = []
        steps = smoke.plan(parse("pr", "--stages", "cast,plants,knockdown,altar,ai,report,npc-tile,saved-runs,resources",
                                 "--no-build"), self.conf, self.prefs, ENVIRON, True, say=said.append)
        self.assertEqual([step.name for step in steps], ["stages-main", "stages-wild", "stages-shrine", "stages-town"])
        self.assertEqual([step.requires for step in steps],
                         [("cast", "knockdown", "ai", "report", "resources"), ("plants",), ("altar", "npc-tile"), ("saved-runs",)])
        self.assertEqual([self.arg(step, "--area-map-sector") for step in steps], [None, smoke.WILD, smoke.SHRINE, smoke.TOWN])
        self.assertEqual([self.arg(step, "--hero") for step in steps], [smoke.AUTO_HERO, None, smoke.AUTO_HERO, smoke.AUTO_HERO])
        self.assertEqual([step.settings for step in steps], [step.name for step in steps])
        self.assertIn("SMOKE run stages-wild on map Arcanum1-024-fixed, sector 1384,239: plants", said)
        # one configuration: the old names and one process
        self.assertEqual([step.name for step in self.plan("pr", "--stages", "cast,ai,report", "--no-build")], ["stages"])
        # a portable stage rides in the process that exists, else it makes one on the default configuration
        self.assertEqual([step.requires for step in self.plan("pr", "--stages", "plants,report", "--no-build")], [("plants", "report")])
        self.assertEqual([step.name for step in self.plan("pr", "--stages", "report,resources", "--no-build")], ["stages"])

    def test_a_stage_that_must_have_its_own_process_gets_one(self) -> None:
        steps = self.plan("pr", "--stages", "cast,walk,ai,perf,critter-fall,tech-craft", "--no-build")
        self.assertEqual([(step.name, step.requires) for step in steps],
                         [("stages-main", ("cast", "ai")),
                          ("stages-main-walk", ("walk",)), ("stages-town-perf", ("perf",)),
                          ("stages-main-critter-fall", ("critter-fall",)), ("stages-crash-site-cave-tech-craft", ("tech-craft",))])
        self.assertEqual(self.arg(steps[4], "--area-map"), smoke.CAVE)
        debug = self.plan("pr", "--stages", "debug-travel,cast", "--no-build")
        self.assertEqual([(step.requires, "--debug-mode" in step.command) for step in debug],
                         [(("debug-travel",), True), (("cast",), False)])  # the debug mode only where it is needed

    def test_a_stage_given_the_wrong_configuration_is_refused_before_it_waits(self) -> None:
        with self.assertRaisesRegex(ValueError, "stage plants does not run at --sector 1452,1291: it needs map Arcanum1-024-fixed at sector 1384,239"):
            self.plan("pr", "--stages", "plants", "--sector", smoke.SHRINE)
        with self.assertRaisesRegex(ValueError, "stage wreck does not run at --sector 1409,1314: it needs map Arcanum1-024-fixed at the start sector"):
            self.plan("pr", "--stages", "wreck", "--sector", smoke.TOWN)
        with self.assertRaisesRegex(ValueError, "stage tile-ghost does not run on map elven ruins: it needs map crash site cave"):
            self.plan("map", "elven ruins", "--stages", "tile-ghost")
        with self.assertRaisesRegex(ValueError, "stage scene does not run on map Arcanum1-024-fixed: it needs a map other than "
                                                "Arcanum1-024-fixed \\(such as crematorium-lev 1\\) from the main menu"):
            self.plan("pr", "--stages", "scene", "--map", smoke.MAIN_MAP)
        with self.assertRaisesRegex(ValueError, "stage plants does not run at --sector.*; stage altar does not run at --sector"):
            self.plan("pr", "--stages", "plants,altar", "--sector", smoke.TOWN)
        with self.assertRaisesRegex(ValueError, "--no-config-check"):
            self.plan("pr", "--stages", "plants", "--sector", smoke.SHRINE)
        # a configuration the stage passes in is no error, and --no-config-check runs the request as given
        self.assertEqual(self.arg(self.plan("pr", "--stages", "transfer,roundtrip", "--sector", smoke.TOWN, "--no-build")[0],
                                  "--area-map-sector"), smoke.TOWN)
        self.assertEqual(self.arg(self.plan("pr", "--stages", "plants", "--sector", smoke.SHRINE, "--no-config-check", "--no-build")[0],
                                  "--area-map-sector"), smoke.SHRINE)
        # no option: the stage's own configuration
        scene = self.plan("pr", "--stages", "scene", "--no-build")[0]
        self.assertEqual((self.arg(scene, "--area-map"), "--main-menu" in scene.command), (smoke.SCENE_MAP, True))
        self.assertEqual(self.arg(self.plan("pr", "--stages", "plants", "--no-build")[0], "--area-map-sector"), smoke.WILD)
        self.assertEqual(self.arg(self.plan("pr", "--stages", "door-close", "--no-build")[0], "--area-map-sector"), smoke.TOWN)

    def test_a_pinned_map_or_sector_holds_for_every_stage_that_accepts_it(self) -> None:
        steps = self.plan("pr", "--stages", "cast,report,junk", "--map", smoke.RUINS, "--no-build")
        self.assertEqual([(step.name, self.arg(step, "--area-map"), self.arg(step, "--hero")) for step in steps],
                         [("stages", smoke.RUINS, "elf-female")])
        steps = self.plan("pr", "--stages", "cast,ai", "--sector", smoke.TOWN, "--no-build")
        self.assertEqual([self.arg(step, "--area-map-sector") for step in steps], [smoke.TOWN])

    def test_review_fixes_portable_map_spelling_sector_spelling_and_the_unchecked_run(self) -> None:
        # a portable stage rides only in a process on its own map: `map` is not run on the cave because tile-ghost is
        steps = self.plan("pr", "--stages", "map,resources,tile-ghost", "--no-build")
        self.assertEqual([(step.name, step.requires, self.arg(step, "--area-map")) for step in steps],
                         [("stages-crash-site-cave", ("tile-ghost",), smoke.CAVE), ("stages-main", ("map", "resources"), smoke.MAIN_MAP)])
        steps = self.plan("pr", "--stages", "cast-multi,map", "--no-build")
        self.assertEqual([(step.requires, self.arg(step, "--area-map")) for step in steps],
                         [(("cast-multi",), smoke.RUINS), (("map",), smoke.MAIN_MAP)])
        # the spelling of a map and of a sector does not matter
        self.assertEqual(len(self.plan("map", "Crash Site Cave", "--stages", "tile-ghost", "--no-build")), 1)
        self.assertEqual(self.arg(self.plan("pr", "--stages", "saved-runs", "--sector", "01409,01314", "--no-build")[0],
                                  "--area-map-sector"), smoke.TOWN)
        # --no-config-check also lets a stage without a hero run from the main menu; a hero stage never does
        self.assertEqual(len(self.plan("pr", "--stages", "report", "--main-menu", "--no-config-check", "--no-build")), 1)
        with self.assertRaisesRegex(ValueError, "needs a session"):
            self.plan("pr", "--stages", "report", "--main-menu")
        with self.assertRaisesRegex(ValueError, "needs a hero"):
            self.plan("pr", "--stages", "cast", "--main-menu", "--no-config-check")
        with self.assertRaisesRegex(ValueError, "not a regular expression"):
            smoke.parse_known_failures([{"stage": "ranged", "symptom": "(", "task": "unassigned", "since": "2026-11-11"}])


class ExecuteTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()

    def tearDown(self) -> None:
        self.temp.cleanup()

    def run_steps(self, steps: list[smoke.Step], codes: dict[str, int],
                  logs: dict[str, str] | None = None) -> tuple[int, list[tuple], list[str]]:
        logs = logs or {}
        calls: list[tuple] = []
        lines: list[str] = []

        def runner(kind: str, command: list[str], *, label: str, timeout: float | None, quiet: bool,
                   start: start_check.StartCheck | None, log_sink, estimate: float | None = None) -> int:
            calls.append((kind, label, quiet, start.start.pattern if start else None))
            name = label[len("smoke "):]
            if name in logs:
                path = Path(self.temp.name) / f"{name}.log"
                path.write_text(logs[name], encoding="utf-8")
                log_sink(path)
            return codes.get(name, 0)

        return smoke.execute(steps, runner, lines.append), calls, lines

    def test_every_smoke_runs_quiet_and_start_checked_and_a_failure_does_not_stop_the_chain(self) -> None:
        steps = [smoke.Step("build", "dotnet", ("sh",), gate=True), smoke.Step("crash-site", "window", ("g",), "^A$"),
                 smoke.Step("main", "window", ("g",), "^B$"), smoke.Step("self-test", "window", ("g",))]
        code, calls, lines = self.run_steps(steps, {"crash-site": 1})
        self.assertEqual(code, 1)
        self.assertEqual(calls, [("dotnet", "smoke build", True, None), ("window", "smoke crash-site", True, "^A$"),
                                 ("window", "smoke main", True, "^B$"),
                                 ("window", "smoke self-test", True, start_check.GODOT_START)])
        self.assertIn("  crash-site  FAIL exit=1", lines)
        self.assertIn("  main        PASS exit=0", lines)
        self.assertEqual(lines[-1], "SMOKE FAIL 3 of 4 steps passed")
        code, _, lines = self.run_steps(steps, {})
        self.assertEqual((code, lines[-1]), (0, "SMOKE PASS 4 of 4 steps passed"))

    def test_the_unfinished_stages_of_a_failed_process_run_one_to_a_process(self) -> None:
        pass_line = lambda stage: smoke.STAGE_PASS[stage] + " {}\n"
        again = lambda stage: smoke.Step(f"stages-retry-{stage}", "window", ("g",), requires=(stage,))
        group = smoke.Step("stages-main", "window", ("g",), requires=("cast", "ai", "life"), again=again)
        steps = [group, smoke.Step("main", "window", ("g",))]
        # cast passed, ai failed and stopped the process, life never ran; alone ai fails too and life passes
        code, calls, lines = self.run_steps(steps, {"stages-main": 1, "stages-retry-ai": 1},
                                            {"stages-main": pass_line("cast"), "stages-retry-ai": "",
                                             "stages-retry-life": pass_line("life")})
        self.assertEqual([call[1] for call in calls],
                         ["smoke stages-main", "smoke stages-retry-ai", "smoke stages-retry-life", "smoke main"])
        self.assertIn("  retrying 2 unfinished stage(s) one to a process: ai, life", lines)
        self.assertIn("  ai: fails alone too (exit 1)", lines)
        self.assertIn("  life: passes alone, so it fails only after the other stages of stages-main", lines)
        self.assertIn("  stages-retry-life  PASS exit=0", lines)
        self.assertEqual((code, lines[-1]), (1, "SMOKE FAIL 2 of 4 steps passed"))  # the failed group stays failed
        # a stopped retry ends the chain; a step without `again` is not retried
        code, calls, lines = self.run_steps(steps, {"stages-main": 1, "stages-retry-ai": 130}, {"stages-main": pass_line("cast")})
        self.assertEqual((code, len(calls)), (130, 2))
        code, calls, lines = self.run_steps(steps, {"stages-main": start_check.FAILED_START_EXIT})
        self.assertEqual([call[1] for call in calls], ["smoke stages-main", "smoke main"])  # a start failure is not retried
        self.assertTrue(any("not retried" in line for line in lines))
        plain = [smoke.Step("stages-main", "window", ("g",), requires=("cast", "ai"))]
        code, calls, _ = self.run_steps(plain, {"stages-main": 1}, {"stages-main": pass_line("cast")})
        self.assertEqual((code, len(calls)), (1, 1))

    def test_a_group_whose_unfinished_stages_all_pass_alone_is_order_dependent_and_does_not_fail_the_run(self) -> None:
        pass_line = lambda stage: smoke.STAGE_PASS[stage] + " {}\n"
        again = lambda stage: smoke.Step(f"stages-retry-{stage}", "window", ("g",), requires=(stage,))
        group = smoke.Step("stages-main", "window", ("g",), requires=("cast", "ai", "life"), again=again)
        steps = [group, smoke.Step("main", "window", ("g",))]
        code, calls, lines = self.run_steps(steps, {"stages-main": 1},
                                            {"stages-main": pass_line("cast"), "stages-retry-ai": pass_line("ai"),
                                             "stages-retry-life": pass_line("life")})
        self.assertEqual(code, 0)
        self.assertIn("  stages-main        ORDER exit=1", lines)
        self.assertIn("  order-dependent: stages-main (fails only after other stages of its process; each of ai, life passes alone)", lines)
        self.assertEqual(lines[-1], "SMOKE PASS 4 of 4 steps passed (1 order-dependent, each stage passes alone)")
        # one stage that fails alone too keeps the failure
        code, _, lines = self.run_steps(steps, {"stages-main": 1, "stages-retry-life": 1},
                                        {"stages-main": pass_line("cast"), "stages-retry-ai": pass_line("ai")})
        self.assertEqual(code, 1)
        self.assertFalse(any("order-dependent:" in line for line in lines))
        self.assertIn("  stages-main        FAIL exit=1", lines)

    def test_a_timeout_a_known_failure_and_a_stopped_retry_are_not_order_dependence(self) -> None:
        pass_line = lambda stage: smoke.STAGE_PASS[stage] + " {}\n"
        again = lambda stage: smoke.Step(f"stages-retry-{stage}", "window", ("g",), requires=(stage,))
        group = smoke.Step("stages-main", "window", ("g",), requires=("cast", "ai"), again=again)
        ok = {"stages-main": pass_line("cast"), "stages-retry-ai": pass_line("ai")}
        code, _, lines = self.run_steps([group], {"stages-main": run_queue.TIMEOUT_EXIT}, ok)
        self.assertEqual((code, lines[-1]), (1, "SMOKE FAIL 1 of 2 steps passed"))  # a hung group is a failure, whatever passes alone
        code, _, lines = self.run_steps([group], {"stages-main": 1}, {**ok, "stages-main": pass_line("cast")})
        self.assertEqual(code, 0)
        known = [smoke.KnownFailure("ai", re.compile("boom"), "unassigned", "2026-11-12")]
        lines = []
        calls = []

        def runner(kind, command, *, label, timeout, quiet, start, log_sink, **rest) -> int:
            name = label[len("smoke "):]
            path = Path(self.temp.name) / f"{name}.log"
            path.write_text(pass_line("cast") + "boom\n" if name == "stages-main" else pass_line("ai"), encoding="utf-8")
            log_sink(path)
            return 1 if name == "stages-main" else 0

        self.assertEqual(smoke.execute([group], runner, lines.append, known), 0)
        self.assertFalse(any("only known failures" in line for line in lines))  # listed as order-dependent, not as a known failure

    def test_a_failed_build_or_a_stopped_run_ends_the_chain(self) -> None:
        steps = [smoke.Step("build", "dotnet", ("sh",), gate=True), smoke.Step("crash-site", "window", ("g",)),
                 smoke.Step("main", "window", ("g",))]
        code, calls, lines = self.run_steps(steps, {"build": 1})
        self.assertEqual((code, len(calls)), (1, 1))
        self.assertIn("  not run: crash-site, main", lines)
        code, calls, lines = self.run_steps(steps, {"crash-site": 130})
        self.assertEqual((code, len(calls)), (130, 2))
        self.assertEqual(lines[-1], "SMOKE FAIL 1 of 3 steps passed")


    def test_a_requested_stage_without_its_pass_line_fails_its_smoke(self) -> None:
        steps = [smoke.Step("stages", "window", ("g",), requires=("ai", "cast")), smoke.Step("main", "window", ("g",))]
        ran = f"{smoke.STAGE_PASS['cast']} {{}}\n{smoke.STAGE_PASS['ai']} {{}}\n"
        code, _, lines = self.run_steps(steps, {}, {"stages": ran})
        self.assertEqual((code, lines[-1]), (0, "SMOKE PASS 2 of 2 steps passed"))
        skipped = f"{smoke.STAGE_PASS['cast']} {{}}\nARC-UI-050 native-smoke barter SKIP (no living hero: start with --hero)\n"
        code, _, lines = self.run_steps(steps, {}, {"stages": skipped})
        self.assertEqual(code, 1)
        self.assertTrue(any(line.startswith(f"  FAIL exit={smoke.STAGE_NOT_RUN} stages not run: ai (no PASS line in ")
                            for line in lines), lines)
        self.assertIn("  viewer: ARC-UI-050 native-smoke barter SKIP (no living hero: start with --hero)", lines)
        self.assertIn(f"  stages  FAIL exit={smoke.STAGE_NOT_RUN}", lines)
        self.assertEqual(lines[-1], "SMOKE FAIL 1 of 2 steps passed")
        early = "ARC-UI-050 native-smoke barter SKIP (x)\nARC-UI-021 native-smoke floaters NONE (y)\n" * 2
        _, _, lines = self.run_steps(steps, {}, {"stages": early + "ARC-WORLD-092 native-smoke ai x NONE (no NPC)\n"})
        self.assertEqual([line for line in lines if line.startswith("  viewer: ")][0],
                         "  viewer: ARC-WORLD-092 native-smoke ai x NONE (no NPC)")  # the missing stage's own line first
        code, _, lines = self.run_steps(steps, {})  # no log: nothing shows the stages ran
        self.assertEqual((code, lines[-1]), (1, "SMOKE FAIL 1 of 2 steps passed"))
        code, _, lines = self.run_steps(steps, {"stages": 1}, {"stages": ""})  # a failed run keeps its own exit
        self.assertIn("  stages  FAIL exit=1", lines)
        self.assertFalse(any("stages not run" in line for line in lines))

    # ARC-DEV-391: known failures on main

    def test_the_known_failures_file_is_well_formed(self) -> None:
        import json
        entries = smoke.parse_known_failures(json.loads(smoke.KNOWN_FAILURES.read_text(encoding="utf-8")))
        self.assertTrue(entries)
        self.assertEqual(entries, smoke.load_known_failures())
        for entry in entries:
            self.assertIn(entry.stage, smoke.STAGE_PASS)
        for bad in ({"stage": "nope", "symptom": "x", "task": "unassigned", "since": "2026-11-11"},
                    {"stage": "ranged", "symptom": "x", "task": "COMBAT-26", "since": "2026-11-11"},
                    {"stage": "ranged", "symptom": "x", "task": "unassigned", "since": "yesterday"},
                    {"stage": "ranged", "symptom": "x", "task": "unassigned"}):
            with self.assertRaises(ValueError):
                smoke.parse_known_failures([bad])
        with self.assertRaises(ValueError):
            smoke.parse_known_failures({})
        self.assertEqual(smoke.load_known_failures(Path(self.temp.name) / "missing.json"), ())

    def test_a_failure_listed_for_main_is_reported_as_known_and_still_fails(self) -> None:
        known = (smoke.KnownFailure("ranged", re.compile("ranged smoke FAIL: the dagger"), "ARC-COMBAT-026", "2026-11-11"),
                 smoke.KnownFailure("crash-site", re.compile("crash site smoke FAIL: x"), "unassigned", "2026-11-11"))
        steps = [smoke.Step("stages", "window", ("g",), requires=("cast", "ranged")), smoke.Step("map", "window", ("g",),
                 requires=smoke.BATTERY_STAGES), smoke.Step("crash-site", "window", ("g",)), smoke.Step("other", "window", ("g",), requires=("cast",))]
        logs = {"stages": f"{smoke.STAGE_PASS['cast']} x\nARC-COMBAT-015 ranged smoke FAIL: the dagger is picked up\n",
                "map": f"{smoke.STAGE_PASS['map']} x\nARC-COMBAT-015 ranged smoke FAIL: the dagger is picked up\n",
                "crash-site": "crash site smoke FAIL: x\n", "other": "something else failed\n"}
        lines: list[str] = []

        def runner(kind, command, *, label, timeout, quiet, start, log_sink, estimate=None) -> int:
            name = label[len("smoke "):]
            path = Path(self.temp.name) / f"{name}.log"
            path.write_text(logs[name], encoding="utf-8")
            log_sink(path)
            return 1

        code = smoke.execute(steps, runner, lines.append, known)
        self.assertEqual(code, 1)  # the exit code is the same as without the list
        self.assertIn("  known on main, not this PR: ranged (ARC-COMBAT-026, since 2026-11-11)", lines)
        self.assertIn("  known on main, not this PR: crash-site (unassigned, since 2026-11-11)", lines)
        self.assertIn("  stages      FAIL exit=1", lines)
        self.assertIn("  only known failures on main (not this PR): stages, map, crash-site", lines)
        self.assertEqual(lines[-1], "SMOKE FAIL 0 of 4 steps passed (3 of the 4 failed with only known failures on main)")
        self.assertEqual([line for line in lines if line.startswith("  not run for another reason")], [])
        # the summary lines still parse as before (daily.py reads them)
        self.assertTrue(all(re.match(r"^\s+(\S+)\s+(PASS|FAIL) exit=(\d+)$", line) or not re.match(r"^\s+\S+\s+(PASS|FAIL)", line)
                            for line in lines))

    def test_a_known_failure_does_not_hide_another_one(self) -> None:
        known = (smoke.KnownFailure("ranged", re.compile("the dagger"), "ARC-COMBAT-026", "2026-11-11"),)
        step = smoke.Step("stages", "window", ("g",), requires=("cast", "ranged", "ai"))
        log = {"stages": f"{smoke.STAGE_PASS['cast']} x\nranged smoke FAIL: the dagger\n"}
        lines: list[str] = []

        def runner(kind, command, *, label, timeout, quiet, start, log_sink, estimate=None) -> int:
            path = Path(self.temp.name) / "stages.log"
            path.write_text(log["stages"], encoding="utf-8")
            log_sink(path)
            return 1

        smoke.execute([step], runner, lines.append, known)
        self.assertIn("  known on main, not this PR: ranged (ARC-COMBAT-026, since 2026-11-11)", lines)
        self.assertIn("  not run for another reason: ai", lines)  # ai is no known failure
        self.assertNotIn("  only known failures on main (not this PR): stages", lines)
        self.assertEqual(lines[-1], "SMOKE FAIL 0 of 1 steps passed")
        # the symptom must show: the same stage failing another way is not known
        lines.clear()
        log["stages"] = f"{smoke.STAGE_PASS['cast']} x\nranged smoke FAIL: a new reason\n"
        smoke.execute([step], runner, lines.append, known)
        self.assertFalse(any("known on main" in line for line in lines))
        # a passing step, a failed build and a stopped run are never called known
        lines.clear()
        smoke.execute([smoke.Step("build", "dotnet", ("sh",), gate=True)], lambda *a, **k: 1, lines.append, known)
        self.assertFalse(any("known on main" in line for line in lines))


class RunTests(unittest.TestCase):
    """smoke.run: the settings folders, the checkout as working folder, the missing Godot, the dry run."""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        self.cwd = os.getcwd()

    def tearDown(self) -> None:
        os.chdir(self.cwd)
        self.temp.cleanup()

    def start(self, argv: tuple[str, ...], code: int, godot: bool = True) -> tuple[int, Path, list]:
        folder = self.base / "arcanum-smoke-x"
        seen: list = []

        def execute(steps: list[smoke.Step]) -> int:
            seen.append((os.getcwd(), sorted(path.name for path in folder.iterdir())))
            return code

        steps = [smoke.Step("build", "dotnet", ("sh",)), smoke.Step("main", "window", ("g",), settings="main"),
                 smoke.Step("self-test", "headless", ("g",), settings="self-test")]

        def mkdtemp(prefix: str) -> str:
            folder.mkdir()
            return str(folder)

        with mock.patch.object(smoke.tempfile, "mkdtemp", mkdtemp), mock.patch.object(smoke, "plan", return_value=steps), \
                mock.patch.object(smoke, "execute", execute), mock.patch.object(smoke.os, "access", return_value=godot), \
                mock.patch.object(smoke, "focus_guard_library", return_value=None), \
                mock.patch.dict(os.environ, {}, clear=False), redirect_stdout(io.StringIO()):
            os.environ.pop(run_queue.TICKET_ENV, None)
            result = smoke.run(parse(*argv))
        return result, folder, seen

    def test_the_settings_folders_go_after_a_pass_and_stay_after_a_failure_or_when_kept(self) -> None:
        code, folder, seen = self.start(("self-test",), 0)
        self.assertEqual((code, folder.exists()), (0, False))
        self.assertEqual(seen, [(str(smoke.ROOT), ["main", "self-test"])])
        code, folder, _ = self.start(("self-test",), 1)
        self.assertEqual((code, folder.exists()), (1, True))
        shutil.rmtree(folder)
        code, folder, _ = self.start(("self-test", "--keep-settings"), 0)
        self.assertEqual((code, folder.exists()), (0, True))

    def test_a_dry_run_or_a_missing_godot_runs_nothing_and_leaves_no_folder(self) -> None:
        code, folder, seen = self.start(("self-test", "--dry-run"), 0)
        self.assertEqual((code, folder.exists(), seen), (0, False, []))
        with self.assertRaisesRegex(ValueError, "no Godot"):
            self.start(("self-test",), 0, godot=False)
        self.assertFalse((self.base / "arcanum-smoke-x").exists())


class LauncherTests(unittest.TestCase):
    def test_the_launcher_has_the_smoke_runner_and_refuses_a_nested_run(self) -> None:
        result = subprocess.run([sys.executable, "-B", str(REPO / "arcanum"), "smoke", "--help"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        for target in ("pr", "full", "map", "crash-site", "self-test"):
            self.assertIn(target, result.stdout)
        nested = subprocess.run([sys.executable, "-B", str(REPO / "arcanum"), "smoke", "self-test", "--dry-run"],
                                capture_output=True, text=True, env={"PATH": "/usr/bin:/bin", run_queue.TICKET_ENV: "3"})
        self.assertEqual(nested.returncode, 2)
        self.assertIn("not inside ./arcanum run", nested.stderr)


class KnownTaskCodeTests(unittest.TestCase):
    def test_known_failure_task_accepts_old_and_issue_number_codes(self) -> None:
        # ARC-DEV-409: from 2026-11-12 a task code carries the GitHub issue number (four digits or more).
        for code in ("ARC-DEV-061", "ARC-UI-1093", "unassigned"):
            self.assertTrue(smoke.KNOWN_TASK.match(code), code)
        for code in ("ARC-UI-12", "ARC-1093", "arc-ui-1093"):
            self.assertFalse(smoke.KNOWN_TASK.match(code), code)


if __name__ == "__main__":
    unittest.main()
