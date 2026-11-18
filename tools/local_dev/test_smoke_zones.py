"""ARC-DEV-396: the smoke zones (tools/local_dev/smoke_zones.py) and their plans in `./arcanum smoke pr --zones/--auto` and
`./arcanum smoke complete`."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

from tools.local_dev import launch, run_queue, smoke, smoke_zones
from tools.local_dev.smoke_zones import COMPLETE, CONTAINER_CLICK, CORE, CRASH_SITE, CENSUS, SOAK, WALK
from tools.local_dev.test_smoke import ENVIRON, parse

REPO = Path(__file__).resolve().parents[2]


class PrFloorSelectionTests(unittest.TestCase):
    """ARC-DEV-2223: the unmapped paths, the PR form of `complete` and the click or walk files of a selection."""

    def test_a_path_no_row_names_selects_complete_and_is_named(self) -> None:
        selection = smoke_zones.select_zones(["unknown/thing.bin", "docs/a.md"])
        self.assertTrue(selection.complete)
        self.assertEqual(selection.unmapped, ("unknown/thing.bin",))
        said = "\n".join(smoke_zones.describe(selection, "abcdef0123", "origin/main"))
        self.assertIn("no zone row names, so `complete` is selected (fail closed): unknown/thing.bin", said)
        self.assertEqual(smoke_zones.select_zones(["src/Arcanum.Simulation/X.cs"]).unmapped, ())

    def test_a_pr_takes_complete_as_core_and_the_crash_site_smoke(self) -> None:
        wide = smoke_zones.select_zones(["src/Arcanum.Data/X.cs"])
        self.assertEqual(wide.zones, (COMPLETE,))
        self.assertEqual(wide.pr_zones, (CORE, CRASH_SITE))
        self.assertTrue(any("rolling main smoke" in line for line in smoke_zones.describe(wide, "abc", "origin/main")))
        narrow = smoke_zones.select_zones(["src/Arcanum.Simulation/Navigation/X.cs"])
        self.assertEqual(narrow.pr_zones, narrow.zones)

    def test_click_and_walk_files_are_named_in_the_selection(self) -> None:
        selection = smoke_zones.select_zones(["src/Arcanum.World/AreaCellPicking.cs", "src/Arcanum.Simulation/Navigation/X.cs",
                                              "src/Arcanum.Session/GameSessionHeal.cs"])
        self.assertEqual(selection.click_walk, ("src/Arcanum.Simulation/Navigation/X.cs", "src/Arcanum.World/AreaCellPicking.cs"))
        self.assertEqual(smoke_zones.select_zones(["src/Arcanum.Session/GameSessionHeal.cs"]).click_walk, ())


class ZoneTableTests(unittest.TestCase):
    def test_every_stage_is_in_exactly_one_zone_and_the_heavy_ones_exist(self) -> None:
        known = smoke.map_stages()
        zoned = [stage for name in smoke_zones.ZONES for stage in smoke_zones.zone_stages(name, known)]
        self.assertEqual(sorted(zoned), sorted(known))  # no stage twice, none lost: 76 in all at this base
        for name, zone in smoke_zones.ZONES.items():
            self.assertTrue(set(zone.stages or ()) <= set(known), name)
        self.assertEqual(smoke_zones.HEAVY, (WALK, CONTAINER_CLICK, CRASH_SITE, CENSUS, SOAK))
        self.assertEqual(smoke_zones.zone_stages(CRASH_SITE, known), ())  # the Crash Site smoke is a step, not a stage
        self.assertIn("map", smoke_zones.zone_stages(CORE, known))
        self.assertNotIn("walk", smoke_zones.zone_stages(CORE, known))

    def test_the_complete_run_lists_every_stage_but_the_two_the_batteries_vouch_for_the_heavy_ones_last(self) -> None:
        known = smoke.map_stages()
        stages = smoke_zones.complete_stages(known)
        # ARC-DEV-1398: the soak zone runs only when named, so the complete run leaves it out
        self.assertEqual(set(stages), set(known) - smoke_zones.IN_BATTERIES - {"soak"})
        self.assertEqual(stages[-3:], ("walk", "container-click", "census"))
        self.assertEqual(smoke_zones.stages_of((COMPLETE,), known), tuple(smoke_zones.stages_of(
            tuple(name for name in smoke_zones.ZONES if name not in smoke_zones.ON_REQUEST), known)))
        self.assertEqual(smoke_zones.stages_of((SOAK,), known), ("soak",))

    def test_the_trigger_table_is_well_formed_and_every_row_names_something_real(self) -> None:
        tracked = subprocess.run(("git", "ls-files"), cwd=REPO, capture_output=True, text=True, check=True).stdout.splitlines()
        for pattern, zones, why in smoke_zones.TRIGGERS:
            self.assertTrue(pattern and why, pattern)
            self.assertTrue(set(zones) <= {*smoke_zones.ZONES, COMPLETE}, pattern)
            self.assertTrue(any(smoke_zones.pattern_matches(pattern, path) for path in tracked), f"{pattern} matches no tracked file")
        for path in smoke_zones.REGISTRATIONS:
            self.assertIn(path, tracked)


class SelectionTests(unittest.TestCase):
    def select(self, *paths: str, lines=None) -> smoke_zones.Selection:
        return smoke_zones.select_zones(paths, lines)

    def test_a_document_or_test_only_diff_selects_nothing(self) -> None:
        selection = self.select("docs/workflow/tasks.md", "tests/Arcanum.World.Tests/X.cs", "README.md",
                                "tools/research/exe_census.py", "tools/hd_pack/hd_pack.py")
        self.assertEqual(selection.zones, ())
        self.assertEqual(len(selection.uncovered), 5)
        self.assertIn("nothing to run", smoke_zones.describe(selection, "abc", "origin/main")[1])

    def test_ordinary_game_code_selects_the_core(self) -> None:
        selection = self.select("src/Arcanum.Session/GameSessionHeal.cs", "tools/terrain-viewer/Godot/AreaInventoryPanel.cs",
                                "tools/terrain-viewer/Core/AreaMainMenu.cs", "docs/x.md")
        self.assertEqual(selection.zones, (CORE,))
        self.assertEqual(selection.because[CORE][0], "src/Arcanum.Session/GameSessionHeal.cs")

    def test_a_heavy_trigger_adds_its_zone_beside_the_core(self) -> None:
        selection = self.select("src/Arcanum.Simulation/Navigation/RouteFollower.cs", "src/Arcanum.World/AreaCellPicking.cs",
                                "src/Arcanum.World/CrashSiteProfile.cs", "tools/terrain-viewer/Godot/TerrainViewerAreaMapCensus.cs")
        self.assertEqual(selection.zones, (CORE, WALK, CONTAINER_CLICK, CRASH_SITE, CENSUS))  # in zone order
        self.assertEqual(self.select("tools/terrain-viewer/Godot/TerrainViewerAreaMapWalkSmoke.cs").zones, (CORE, WALK))

    def test_shared_data_save_schema_render_and_project_files_select_the_complete_run(self) -> None:
        for path in ("src/Arcanum.Data/Area/AreaBuilder.cs", "src/Arcanum.Session/GameSessionSnapshotCodec.cs",
                     "src/Arcanum.Session/GameSaveLibrary.cs", "tools/terrain-viewer/Godot/TerrainCanvas.cs",
                     "tools/terrain-viewer/Core/AreaFrameDriver.cs", "Directory.Packages.props", "global.json",
                     "tools/terrain-viewer/Godot/Arcanum.TerrainViewer.csproj", "src/Arcanum.Data/packages.lock.json",
                     "tools/terrain-viewer/Godot/Main.tscn"):
            self.assertEqual(self.select(path, "docs/a.md").zones, (COMPLETE,), path)

    def test_a_no_smoke_area_vetoes_the_generic_project_rows_but_the_viewers_own_files_still_select(self) -> None:
        for path in ("tests/Arcanum.World.Tests/Arcanum.World.Tests.csproj", "tests/Arcanum.World.Tests/packages.lock.json",
                     "experiments/gamepad-lab/GamepadLab.sln", "tools/research/Arcanum.ArtProbe/packages.lock.json",
                     "src/Arcanum.Godot/Arcanum.Godot.sln", "src/Arcanum.Godot/Main.tscn",
                     "tools/terrain-viewer/Godot/AreaBarterPanel.cs.uid"):
            self.assertEqual(self.select(path).zones, (), path)
        self.assertEqual(self.select("tools/terrain-viewer/Godot/Arcanum.TerrainViewer.csproj").zones, (COMPLETE,))
        self.assertEqual(self.select("src/Arcanum.Executable/PeImage.cs", "src/Arcanum.Input.Godot/GodotInputAdapter.cs",
                                     "tests/Arcanum.TerrainViewer.Tests/GridWireFixture.cs", "arcanum").zones, (CORE,))

    def test_neighbouring_walk_and_click_files_add_their_zones(self) -> None:
        for path in ("src/Arcanum.World/AreaRealSteps.cs", "src/Arcanum.Simulation/Travel/WorldRoute.cs",
                     "tools/terrain-viewer/Godot/TerrainViewerAreaMapTransfer.cs", "src/Arcanum.Session/GameSessionTransfer.cs"):
            self.assertIn(WALK, self.select(path).zones, path)

    def test_a_file_no_row_names_selects_the_complete_run(self) -> None:
        selection = self.select("some/new/place/file.txt", "src/Arcanum.Session/GameSessionHeal.cs")
        self.assertEqual(selection.zones, (COMPLETE,))
        self.assertEqual(selection.because[COMPLETE], ("some/new/place/file.txt",))

    def test_the_runner_and_its_own_files_select_the_core_and_the_other_launcher_tooling_none(self) -> None:
        self.assertEqual(self.select("tools/local_dev/smoke.py", "tools/local_dev/smoke_zones.py").zones, (CORE,))
        self.assertEqual(self.select("tools/local_dev/daily.py", "tools/local_dev/test_smoke.py").zones, ())
        self.assertEqual(self.select("arcanum", "tools/local_dev/test_smoke.py").zones, (CORE,))  # the launcher wires the smoke command
        self.assertEqual(self.select("src/Arcanum.Godot/ViewerApp.HdReview.cs").zones, ())

    def test_a_stage_registration_alone_does_not_pull_in_the_shared_zones(self) -> None:
        viewer = "tools/terrain-viewer/Godot/"
        registered = lambda path: ['    if (only?.Contains("new-stage") == true) await RunNewStageAsync();', ""]
        self.assertEqual(self.select(viewer + "TerrainViewerApp.cs", lines=registered).zones, (CORE,))
        commented = lambda path: ["// ARC-DEV-1 native observation (stage new, only when named):",
                                  'if (Stage("death")) await RunAreaDeathSmokeAsync(result);',
                                  'if (_readyMapName is not null && only?.Contains("walk") == true) await RunAreaMapWalkSmokeAsync();']
        self.assertEqual(self.select(viewer + "TerrainViewerApp.cs", lines=commented).zones, (CORE,))
        rewritten = lambda path: ["return stages.All(MapSmokeStageNames.Contains) ? stages : throw new Exception();"]
        self.assertEqual(self.select(viewer + "TerrainViewerAreaMapStream.cs", lines=rewritten).zones, (CORE, WALK))
        stage_list = lambda path: ["    private static readonly string[] MapSmokeStageNames = [...];"]
        self.assertEqual(self.select(viewer + "TerrainViewerAreaMapStream.cs", lines=stage_list).zones, (CORE,))
        other = lambda path: ["    int streamMargin = 9;"]
        self.assertEqual(self.select(viewer + "TerrainViewerAreaMapStream.cs", lines=other).zones, (CORE, WALK))
        self.assertEqual(self.select(viewer + "TerrainViewerApp.cs", lines=other).zones, (COMPLETE,))
        self.assertEqual(self.select(viewer + "TerrainViewerApp.cs").zones, (COMPLETE,))  # unreadable lines: the safe side

    def test_the_explanation_names_each_zone_and_the_files_that_chose_it(self) -> None:
        selection = self.select(*(f"src/Arcanum.Session/GameSession{name}.cs" for name in "ABCDE"), "docs/a.md")
        lines = smoke_zones.describe(selection, "0123456789abcdef", "origin/main")
        self.assertEqual(lines[0], "SMOKE auto: 6 changed files since origin/main (0123456789)")
        self.assertEqual(lines[1], "SMOKE auto: zone core because of src/Arcanum.Session/GameSessionA.cs, "
                                   "src/Arcanum.Session/GameSessionB.cs, src/Arcanum.Session/GameSessionC.cs and 2 more")
        self.assertIn("not covered by any zone: 1 files", lines[2])

    def test_zone_names_are_parsed_and_complete_stands_alone(self) -> None:
        self.assertEqual(smoke_zones.parse_zones("core, walk,core"), (CORE, WALK))
        self.assertEqual(smoke_zones.parse_zones("walk,complete"), (COMPLETE,))
        self.assertEqual(smoke_zones.parse_zones(None), ())
        with self.assertRaisesRegex(ValueError, "unknown smoke zone cor "):
            smoke_zones.parse_zones("cor")

    def test_the_diff_is_read_against_the_merge_base_with_untracked_files(self) -> None:
        calls = []

        def git(*args: str) -> str:
            calls.append(args)
            return {"merge-base": "abc123\n", "diff": "src/A.cs\ndocs/b.md\n", "ls-files": "new.txt\nsrc/A.cs\n"}[args[0]]

        mark, files = smoke_zones.changed_files("origin/main", git)
        self.assertEqual((mark, files), ("abc123", ("docs/b.md", "new.txt", "src/A.cs")))
        self.assertEqual(calls[0], ("merge-base", "origin/main", "HEAD"))
        self.assertEqual(calls[1], ("diff", "--name-only", "--no-renames", "abc123"))
        lines = smoke_zones.changed_lines("abc123", "f.cs", lambda *args: "--- a/f.cs\n+++ b/f.cs\n@@ -1 +1 @@\n-old\n+new\n")
        self.assertEqual(lines, ["old", "new"])


class ZonePlanTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name).resolve()
        inputs = {"sourceId": "public-fixture"}
        for role, (_, size, _) in launch.GAME_ROLES.items():
            path = root / "roles" / role
            if size is None:
                path.mkdir(parents=True)
            else:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"public fixture")
            inputs[role] = str(path)
        self.conf, self.prefs, self.said = {"inputs": inputs}, root / "prefs", []

    def tearDown(self) -> None:
        self.temp.cleanup()

    def plan(self, *argv: str) -> list[smoke.Step]:
        return smoke.plan(parse(*argv), self.conf, self.prefs, ENVIRON, True, say=self.said.append)

    def stages(self, steps: list[smoke.Step]) -> list[str]:
        return [stage for step in steps for stage in step.requires]

    def test_the_core_zone_runs_every_cheap_stage_in_a_few_processes_and_no_heavy_one(self) -> None:
        steps = self.plan("pr", "--zones", "core", "--no-build")
        stages = self.stages(steps)
        known = smoke.map_stages()
        self.assertEqual(sorted(stages), sorted(smoke_zones.zone_stages(CORE, known)))
        for heavy in ("walk", "container-click", "census"):
            self.assertNotIn(heavy, stages)
        self.assertNotIn("crash-site", [step.name for step in steps])
        self.assertLess(len(steps), len(stages) / 2)  # grouped by configuration, not one process a stage
        self.assertTrue(all(step.name.startswith("stages") for step in steps))

    def test_a_heavy_zone_runs_its_stage_in_a_process_of_its_own(self) -> None:
        steps = self.plan("pr", "--zones", "walk,container-click", "--no-build")
        self.assertEqual(self.stages(steps), ["walk", "container-click"])
        self.assertEqual(len(steps), 2)
        self.assertEqual(self.plan("pr", "--zones", "census", "--no-build")[0].requires, ("census",))

    def test_the_soak_zone_runs_on_request_only_with_a_timeout_that_follows_its_minutes(self) -> None:
        # ARC-DEV-1398: the leak soak is never selected by a diff, `complete` leaves it out, and a named run gets a process of its own
        # (the stage's own default minutes plus the tail; `--env ARC_SOAK_STAGE_MINUTES` moves the timeout with it).
        steps = self.plan("pr", "--zones", "soak", "--no-build")
        self.assertEqual(self.stages(steps), ["soak"])
        self.assertEqual(steps[0].timeout, smoke.SOAK_DEFAULT_MINUTES + smoke.SOAK_TAIL_MINUTES)
        self.assertEqual(steps[0].requires, ("soak",))
        long = self.plan("pr", "--stages", "soak", "--env", "ARC_SOAK_STAGE_MINUTES=45", "--no-build")[0]
        self.assertEqual(long.timeout, 45 + smoke.SOAK_TAIL_MINUTES)
        self.assertIn("ARC_SOAK_STAGE_MINUTES=45", long.command)
        self.assertIsNone(self.plan("pr", "--stages", "cast", "--no-build")[0].timeout)
        self.assertIsNone(smoke.soak_timeout(["soak"], (("ARC_SOAK_STAGE_MINUTES", "many"),)))  # the viewer refuses it itself
        for touched in ("tools/terrain-viewer/Godot/TerrainViewerSoakSmoke.cs", "tools/terrain-viewer/Core/ViewerSoak.cs"):
            self.assertNotIn(SOAK, smoke_zones.select_zones([touched]).zones)

    def test_the_crash_site_zone_is_the_crash_site_smoke_step_alone(self) -> None:
        steps = self.plan("pr", "--zones", "crash-site", "--no-build")
        self.assertEqual([step.name for step in steps], ["crash-site"])
        steps = self.plan("pr", "--zones", "crash-site,walk", "--stages", "cast", "--no-build")
        self.assertEqual([step.name for step in steps][0], "crash-site")
        self.assertEqual(self.stages(steps), ["walk", "cast"])

    def test_zones_and_explicit_stages_combine_without_duplicates_and_pins_are_refused(self) -> None:
        steps = self.plan("pr", "--zones", "walk", "--stages", "walk,hover", "--no-build")
        self.assertEqual(sorted(self.stages(steps)), ["hover", "walk"])
        with self.assertRaisesRegex(ValueError, "go with --stages, not with the zones"):
            self.plan("pr", "--zones", "core", "--sector", "1409,1314", "--no-build")
        with self.assertRaisesRegex(ValueError, "go with --stages, not with the zones"):
            self.plan("pr", "--zones", "complete", "--stages", "cast", "--map", "elven ruins", "--no-build")
        steps = self.plan("pr", "--zones", "census", "--stages", "cast", "--map", "elven ruins", "--no-build")
        self.assertEqual([(step.name, step.requires) for step in steps],
                         [("zones-census", ("census",)), ("stages", ("cast",))])  # the pin holds for the named stage only
        self.assertIn("elven ruins", steps[1].command)
        self.assertNotIn("elven ruins", steps[0].command)
        with self.assertRaisesRegex(ValueError, "unknown smoke zone"):
            self.plan("pr", "--zones", "fast")

    def test_the_complete_run_is_the_full_chain_and_then_every_stage(self) -> None:
        steps = self.plan("complete", "--no-build")
        names = [step.name for step in steps]
        self.assertEqual(names[:5], ["crash-site", "main", "town", "wild", "ruins"])
        known = smoke.map_stages()
        self.assertEqual(sorted(self.stages(steps[5:])), sorted(smoke_zones.complete_stages(known)))
        self.assertEqual(len(smoke_zones.complete_stages(known)) + 3, len(known))  # the two batteries' and `soak`
        # the stages the batteries do not vouch for are all listed: 74 of the 76 at this base
        self.assertEqual(sorted(self.stages(steps[1:5])), ["map"] * 4 + ["resources"] * 4)
        self.assertTrue(names[-3:] == ["stages-main-walk", "stages-town-container-click", "stages-main-census"], names[-3:])
        self.assertEqual(len(names), len(set(names)), "every step has its own name and settings folder")
        self.assertEqual([step.name for step in self.plan("pr", "--zones", "complete", "--no-build")], names)

    def test_auto_picks_the_zones_from_the_diff_and_prints_why(self) -> None:
        def run(files: tuple[str, ...], *argv: str) -> list[smoke.Step]:
            with mock.patch.object(smoke_zones, "changed_files", return_value=("abcdef0123", files)), \
                    mock.patch.object(smoke_zones, "changed_lines", return_value=[]):
                return self.plan("pr", "--auto", "--no-build", *argv)

        steps = run(("src/Arcanum.Simulation/Navigation/RouteFollower.cs", "docs/a.md"))
        self.assertEqual(sorted(self.stages(steps)), sorted([*smoke_zones.zone_stages(CORE, smoke.map_stages()), "walk"]))
        self.assertIn("SMOKE auto: zone walk because of src/Arcanum.Simulation/Navigation/RouteFollower.cs", self.said)
        self.said.clear()
        self.assertEqual(run(("docs/a.md", "tests/X.cs")), [])  # nothing is covered: no build, no run
        self.assertTrue(any("nothing to run" in line for line in self.said))
        self.assertEqual([step.name for step in run(("docs/a.md",), "--crash-site")], ["crash-site"])
        steps = run(("tools/terrain-viewer/Godot/TerrainViewerAreaMapWalkSmoke.cs",))
        self.assertEqual(steps[-1].requires, ("walk",))
        # ARC-DEV-2223: the shared code selects `complete`, which a PR runs as core + the Crash Site smoke (the floor); the heavy
        # zones and the batteries are the rolling main smoke's and the daily build's; `--zones complete` still runs it all
        steps = run(("src/Arcanum.Data/X.cs",))
        self.assertEqual(steps[0].name, "crash-site")
        self.assertEqual(sorted(self.stages(steps[1:])), sorted(smoke_zones.zone_stages(CORE, smoke.map_stages())))
        self.assertTrue(any("a PR runs core + crash-site for `complete`" in line for line in self.said))
        self.assertEqual(len(run(("src/Arcanum.Data/X.cs",), "--zones", "complete")), len(self.plan("complete", "--no-build")))
        steps = run(("src/Arcanum.Session/GameSessionHeal.cs",), "--stages", "walk")
        self.assertEqual(sorted(self.stages(steps)), sorted([*smoke_zones.zone_stages(CORE, smoke.map_stages()), "walk"]))

    def test_click_or_walk_handling_adds_the_map_stage_to_an_auto_run(self) -> None:
        with mock.patch.object(smoke_zones, "changed_files", return_value=("abcdef0123", ("src/Arcanum.World/AreaCellPicking.cs",))), \
                mock.patch.object(smoke_zones, "changed_lines", return_value=[]):
            steps = self.plan("pr", "--auto", "--no-build")
        self.assertIn("map", self.stages(steps))
        self.assertIn("container-click", self.stages(steps))
        self.assertTrue(any("`smoke self-test` are mandatory" in line for line in self.said))

    def test_a_pinned_run_marks_its_steps_custom_and_a_default_run_does_not(self) -> None:  # ARC-DEV-2223
        self.assertFalse(any(step.custom for step in self.plan("pr", "--stages", "cast", "--no-build")))
        self.assertTrue(all(step.custom for step in self.plan("pr", "--stages", "cast", "--hero", "elf-male", "--no-build")))
        self.assertTrue(all(step.custom for step in self.plan("pr", "--stages", "cast", "--env", "A=1", "--no-build")))
        self.assertTrue(all(step.custom for step in self.plan("pr", "--stages", "cast", "--map", "elven ruins", "--no-build")))
        self.assertTrue(all(step.custom for step in self.plan("map", "elven ruins", "--no-build")))
        self.assertFalse(any(step.custom for step in self.plan("complete", "--no-build")))

    def test_a_run_with_no_zone_to_run_passes_and_builds_nothing(self) -> None:
        output = io.StringIO()
        args = parse("pr", "--auto", "--dry-run")
        env = {k: v for k, v in os.environ.items() if k != run_queue.TICKET_ENV}  # ARC-DEV-2209: also green inside ./arcanum run
        with mock.patch.object(smoke_zones, "changed_files", return_value=("abc", ("docs/a.md",))), \
                mock.patch.object(smoke, "focus_guard_library", return_value=None), \
                mock.patch.dict(os.environ, env, clear=True), redirect_stdout(output):
            self.assertEqual(smoke.run(args), 0)
        self.assertIn("SMOKE PASS nothing to run", output.getvalue())

    def test_a_run_inside_the_queue_is_still_refused(self) -> None:
        with mock.patch.dict(os.environ, {run_queue.TICKET_ENV: "7"}), self.assertRaisesRegex(ValueError, "not inside ./arcanum run"):
            smoke.run(parse("pr", "--auto", "--dry-run"))

    def test_the_retry_of_unfinished_stages_is_on_for_complete_and_off_for_a_pr_unless_asked(self) -> None:
        def retrying(*argv: str) -> list[str]:
            return [step.name for step in self.plan(*argv, "--no-build") if step.again is not None]

        self.assertEqual(retrying("pr", "--stages", "cast,ai"), [])
        self.assertEqual(retrying("pr", "--stages", "cast,ai", "--retry"), ["stages"])  # not for a process of one stage
        self.assertEqual(retrying("pr", "--stages", "walk", "--retry"), [])
        self.assertTrue(retrying("complete"))  # the stage groups of the nightly run, not the batteries
        self.assertNotIn("main", retrying("complete"))
        self.assertTrue(all(name.startswith("stages") for name in retrying("complete")))
        self.assertEqual(retrying("complete", "--no-retry"), [])
        step = next(step for step in self.plan("pr", "--stages", "cast,ai", "--retry", "--no-build"))
        (self.prefs / (step.settings or "")).mkdir(parents=True)
        alone = step.again("ai")
        self.assertEqual((alone.name, alone.requires), ("stages-retry-ai", ("ai",)))
        self.assertTrue((self.prefs / (alone.settings or "")).is_dir())
        self.assertIn("ARC_AREA_MAP_SMOKE_ONLY=ai", alone.command)

    def test_the_stages_that_failed_in_the_daily_run_have_the_configuration_they_pass_in(self) -> None:
        wanted = {"junk": (smoke.RUINS, False), "critter-light": (smoke.LIGHT_MAP, False), "ai-cast": (smoke.MAIN_MAP, True),
                  "npc-door": (smoke.MAIN_MAP, True)}
        for stage, (map_name, own) in wanted.items():
            self.assertEqual((smoke.STAGE_NEEDS[stage].map, smoke.STAGE_NEEDS[stage].own), (map_name, own), stage)
        steps = self.plan("pr", "--stages", "junk,critter-light,ai-cast,npc-door", "--no-build")
        self.assertEqual(sorted(step.requires for step in steps), [("ai-cast",), ("critter-light",), ("junk",), ("npc-door",)])

    def test_the_launcher_help_lists_the_zone_options(self) -> None:
        args = parse("pr", "--zones", "core", "--auto", "--base", "origin/dev")
        self.assertEqual((args.zones, args.auto, args.base), ("core", True, "origin/dev"))
        self.assertEqual(parse("complete").target, "complete")


if __name__ == "__main__":
    unittest.main()
