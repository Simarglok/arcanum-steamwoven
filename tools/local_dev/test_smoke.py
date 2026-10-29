"""ARC-DEV-061: ./arcanum smoke plans the per-PR scope and the full map smoke chain and runs each smoke through the queue."""
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

    def tearDown(self) -> None:
        self.temp.cleanup()

    def plan(self, *argv: str, imported: bool = True) -> list[smoke.Step]:
        return smoke.plan(parse(*argv), self.conf, self.prefs, ENVIRON, imported)

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
                self.assertIn(f"{name}_TEST_PATH={self.prefs / step.settings / (name.lower() + '.json')}", command)
            self.assertIn("__GODOT_SHELL_ENV_SET=1", command)
            self.assertIn("PATH=/sdk:/usr/bin:/bin:/usr/sbin:/sbin", command)
            self.assertEqual(command[command.index("--path") + 1], "tools/terrain-viewer/Godot")
        self.assertEqual([step.settings for step in steps], [None, "crash-site", "stages"])

    def test_the_start_line_names_the_intended_mode_map_and_smoke(self) -> None:
        steps = self.plan("pr", "--stages", "map", "--map", "elven ruins", "--crash-site")
        crash, stages = (re.compile(step.expect or "") for step in steps[1:])
        line = "ARC-DEV-059 viewer start mode=crash-site-area map={} main-menu=False smoke=True"
        self.assertTrue(crash.search(line.format("-")))
        self.assertFalse(crash.search(line.format("elven ruins")))
        self.assertTrue(stages.search(line.format("elven ruins")))
        for wrong in (line.format("-"), line.format("elven ruins 2"), line.format("elven ruins").replace("True", "False"),
                      line.format("elven ruins").replace("crash-site-area", "world-diagnostic")):
            self.assertFalse(stages.search(wrong), wrong)
        menu = self.plan("map", "Arcanum1-024-fixed", "--main-menu")[-1]
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

    def test_one_map_crash_site_and_self_test(self) -> None:
        one = self.plan("map", "crash site cave", "--stages", "junk", "--no-build")
        self.assertEqual([step.name for step in one], ["map"])
        self.assertIn("ARC_AREA_MAP_SMOKE_ONLY=junk", one[0].command)
        crash = self.plan("crash-site", "--hero", "elf-female", "--no-build")[0].command
        self.assertEqual(crash[crash.index("--hero") + 1], "elf-female")
        test = self.plan("self-test", "--headless", "--no-build")[0]
        self.assertEqual((test.kind, test.expect), ("headless", None))
        self.assertIn("--headless", test.command)
        self.assertEqual(test.command[-1], "--self-test")
        self.assertEqual(self.plan("self-test", "--no-build")[0].kind, "window")


class ExecuteTests(unittest.TestCase):
    def run_steps(self, steps: list[smoke.Step], codes: dict[str, int]) -> tuple[int, list[tuple], list[str]]:
        calls: list[tuple] = []
        lines: list[str] = []

        def runner(kind: str, command: list[str], *, label: str, timeout: float | None, quiet: bool,
                   start: start_check.StartCheck | None) -> int:
            calls.append((kind, label, quiet, start.start.pattern if start else None))
            return codes.get(label[len("smoke "):], 0)

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

    def test_a_failed_build_or_a_stopped_run_ends_the_chain(self) -> None:
        steps = [smoke.Step("build", "dotnet", ("sh",), gate=True), smoke.Step("crash-site", "window", ("g",)),
                 smoke.Step("main", "window", ("g",))]
        code, calls, lines = self.run_steps(steps, {"build": 1})
        self.assertEqual((code, len(calls)), (1, 1))
        self.assertIn("  not run: crash-site, main", lines)
        code, calls, lines = self.run_steps(steps, {"crash-site": 130})
        self.assertEqual((code, len(calls)), (130, 2))
        self.assertEqual(lines[-1], "SMOKE FAIL 1 of 3 steps passed")


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


if __name__ == "__main__":
    unittest.main()
