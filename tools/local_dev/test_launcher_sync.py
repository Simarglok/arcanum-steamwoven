"""The ordinary launcher must stay in step with the viewer's argument parser and pinned inputs."""
from __future__ import annotations

import csv
import hashlib
import io
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from tools.local_dev import config, launch

REPO = Path(__file__).resolve().parents[2]
# Viewer flags the ordinary launcher deliberately never passes (caller-side hash expectations).
NON_LAUNCHER_FLAGS = {"--expect-module-sha256", "--expect-resource-sha256", "--expect-actor-resource-sha256"}


class LauncherSyncTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name).resolve()

    def tearDown(self):
        self.temp.cleanup()

    def inputs(self) -> dict[str, str]:
        result = {"sourceId": "public-fixture"}
        for role, (relative, size, _) in launch.GAME_ROLES.items():
            path = self.root / "roles" / role
            if size is None:
                path.mkdir(parents=True, exist_ok=True); (path / "fixture.pro").write_bytes(b"x")
            else:
                path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"public fixture")
            result[role] = str(path)
        return result

    def test_every_viewer_flag_is_passed_or_explicitly_excluded(self):
        source = (REPO / "tools/terrain-viewer/Core/TerrainViewerArguments.cs").read_text()
        viewer = set(re.findall(r'case "(--[a-z0-9-]+)"', source))
        passed = set()
        for mode in launch.MODE_ROLES:
            argv = launch.terrain_arguments(self.inputs(), mode)
            passed |= {token for token in argv if token.startswith("--")}
            for role in launch.MODE_ROLES[mode]:
                self.assertIn(launch.ROLE_FLAGS[role], argv, f"{mode} must pass {role}")
        passed |= {token for token in launch.terrain_arguments(self.inputs(), "actor-diagnostic", {}) if token.startswith("--")}
        for task, roles in launch.TASK_ROLES.items():
            argv = launch.terrain_arguments(self.inputs(), launch.TASK_MODES[task], task=task)
            passed |= {token for token in argv if token.startswith("--")}
            for role in roles:
                self.assertIn(launch.ROLE_FLAGS[role], argv, f"{task} must pass {role}")
        argv = launch.map_arguments(self.inputs(), "crash site cave")
        passed |= {token for token in argv if token.startswith("--")}
        passed |= {token for token in launch.map_arguments(self.inputs(), "Arcanum1-024-fixed", sector="1452,1290") if token.startswith("--")}
        passed |= {token for token in launch.terrain_arguments(self.inputs(), "crash-site-area", hero="elf-male") if token.startswith("--")}
        passed |= {token for token in launch.map_arguments(self.inputs(), "Arcanum1-024-fixed", main_menu=True) if token.startswith("--")}
        self.assertTrue({launch.ROLE_FLAGS[role] for role in launch.MAP_ROLES} <= set(argv))
        self.assertEqual(viewer, passed | NON_LAUNCHER_FLAGS,
                         "a viewer flag was added or removed: update GAME_ROLES/MODE_ROLES/ROLE_FLAGS and terrain_arguments")
        self.assertTrue(set(launch.ROLE_FLAGS.values()) <= viewer)
        self.assertEqual(set(launch.ROLE_FLAGS), set(launch.GAME_ROLES))
        self.assertTrue(all(set(roles) <= set(launch.GAME_ROLES) for roles in launch.MODE_ROLES.values()))
        self.assertTrue(all(set(roles) <= set(launch.GAME_ROLES) for roles in launch.TASK_ROLES.values()))
        self.assertEqual(set(launch.TASK_ROLES), set(launch.TASK_MODES))

    def test_the_cave_input_is_passed_only_to_an_arc_world_009_area_build(self):
        # Older AREA builds reject --area-cave-mobile; only the launched manifest's own task adds it.
        for task in (None, "ARC-RENDER-007", "ARC-INPUT-001", "ARC-WORLD-007"):
            self.assertNotIn("--area-cave-mobile", launch.terrain_arguments(self.inputs(), "crash-site-area", task=task), task)
        argv = launch.terrain_arguments(self.inputs(), "crash-site-area", task="ARC-WORLD-009")
        self.assertEqual(1, argv.count("--area-cave-mobile"))
        self.assertNotIn("--area-cave-mobile", launch.terrain_arguments(self.inputs(), "world-diagnostic", task="ARC-WORLD-009"))
        without = {role: path for role, path in self.inputs().items() if role != "caveMobile"}
        launch.terrain_arguments(without, "crash-site-area", task="ARC-WORLD-007")
        with self.assertRaises(ValueError):
            launch.terrain_arguments(without, "crash-site-area", task="ARC-WORLD-009")

    def test_map_mode_passes_the_game_folder_and_one_safe_map_name(self):
        inputs = self.inputs()
        argv = launch.map_arguments(inputs, "caladon-castle main floor")
        self.assertEqual(argv[:2], ["--mode", "area"])
        self.assertEqual(argv[-2:], ["--area-map", "caladon-castle main floor"])
        # The map's inputs come from the content stack: no prototype folder or Crash Site inputs.
        for flag in ("--prototypes", "--area-mobile", "--area-effects", "--area-patch", "--area-cave-mobile"):
            self.assertNotIn(flag, argv)
        for name in ("", " ", "maps/x", "a\\b", "line\nbreak", "x" * (launch.MAX_MAP_NAME + 1)):
            with self.assertRaises(ValueError, msg=repr(name)):
                launch.map_arguments(inputs, name)
        with self.assertRaises(ValueError):
            launch.map_arguments({role: path for role, path in inputs.items() if role != "gameRoot"}, "void")

    def test_map_mode_passes_an_opening_sector_in_the_viewers_form(self):
        # ARC-RENDER-024: `--area-map-sector X,Y` (the viewer's ParseSector: two numbers below 2^26).
        inputs = self.inputs()
        self.assertNotIn("--area-map-sector", launch.map_arguments(inputs, "Arcanum1-024-fixed"))
        argv = launch.map_arguments(inputs, "Arcanum1-024-fixed", "elf-male", "1409,1314")
        self.assertEqual(argv[-6:], ["--area-map", "Arcanum1-024-fixed", "--hero", "elf-male", "--area-map-sector", "1409,1314"])
        self.assertEqual(launch.map_arguments(inputs, "x", sector="0,67108863")[-1], "0,67108863")
        for bad in ("", "1", "1,2,3", "-1,2", "1, 2", "x,2", "67108864,0", "123456789,1", "1.5,2"):
            with self.subTest(sector=bad), self.assertRaises(ValueError):
                launch.map_arguments(inputs, "x", sector=bad)
        source = (REPO / "tools/terrain-viewer/Core/TerrainViewerArguments.cs").read_text()
        self.assertIn('case "--area-map-sector"', source)
        self.assertIn("x < 67108864 && y < 67108864", source)
        self.assertEqual(launch.MAX_SECTOR, 67108863)
        # `./arcanum map NAME --sector X,Y` and the low-level launcher pass it on.
        self.assertIn('("--area-map-sector", args.sector)', (REPO / "arcanum").read_text())
        self.assertIn('launcher.add_argument("--area-map-sector"', (REPO / "tools/arcanum-local").read_text())

    def test_hero_names_match_the_viewer_and_are_passed_only_to_the_area_mode(self):
        # ARC-WORLD-027: the launcher's hero names are the viewer's `AreaStartingHero.Premade`, in main-menu order.
        source = (REPO / "tools/terrain-viewer/Core/AreaStartingHero.cs").read_text()
        viewer = re.findall(r'new\("([a-z-]+)", (1607\d|1606\d)\)', source)
        self.assertEqual([name for name, _ in viewer], list(launch.PREMADE_HEROES))
        self.assertEqual([int(number) for _, number in viewer], list(range(16067, 16079)))
        inputs = self.inputs()
        self.assertNotIn("--hero", launch.terrain_arguments(inputs, "crash-site-area"))
        argv = launch.terrain_arguments(inputs, "crash-site-area", hero="human-female")
        self.assertEqual(argv[-2:], ["--hero", "human-female"])
        self.assertEqual(launch.map_arguments(inputs, "crash site cave", "dwarf-male")[-4:], ["--area-map", "crash site cave", "--hero", "dwarf-male"])
        for bad in ("Human-Female", "16075", "", "ogre-male"):
            with self.assertRaises(ValueError, msg=bad):
                launch.terrain_arguments(inputs, "crash-site-area", hero=bad)
            with self.assertRaises(ValueError, msg=bad):
                launch.map_arguments(inputs, "void", bad)
        with self.assertRaises(ValueError):
            launch.terrain_arguments(inputs, "world-diagnostic", hero="elf-male")

    def test_the_main_menu_is_passed_to_the_area_mode_without_a_hero(self):
        # ARC-UI-011: `--main-menu true` opens the menu over the AREA scene; the menu picks the hero, so --hero is refused with it.
        inputs = self.inputs()
        self.assertNotIn("--main-menu", launch.terrain_arguments(inputs, "crash-site-area"))
        self.assertEqual(launch.terrain_arguments(inputs, "crash-site-area", main_menu=True)[-2:], ["--main-menu", "true"])
        self.assertEqual(launch.map_arguments(inputs, "Arcanum1-024-fixed", main_menu=True)[-4:], ["--area-map", "Arcanum1-024-fixed", "--main-menu", "true"])
        with self.assertRaisesRegex(ValueError, "drop --hero"):
            launch.map_arguments(inputs, "Arcanum1-024-fixed", "elf-male", main_menu=True)
        with self.assertRaisesRegex(ValueError, "drop --hero"):
            launch.terrain_arguments(inputs, "crash-site-area", hero="elf-male", main_menu=True)
        with self.assertRaisesRegex(ValueError, "area mode only"):
            launch.terrain_arguments(inputs, "world-diagnostic", main_menu=True)
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        for command in (["area", "--main-menu"], ["map", "Arcanum1-024-fixed", "--main-menu"]):
            self.assertTrue(project["parser"]().parse_args(command).main_menu, command)
        self.assertIn('launcher.add_argument("--main-menu"', (REPO / "tools/arcanum-local").read_text())
        # ARC-UI-012: `area` opens the menu by default; --no-main-menu or a hero start opens none; `map` only on request.
        requested = project["main_menu_requested"]
        for command, expected in ((["area"], True), (["area", "--no-main-menu"], False), (["area", "--hero", "elf-male"], False),
                                  (["map", "Arcanum1-024-fixed"], False), (["map", "Arcanum1-024-fixed", "--main-menu"], True)):
            self.assertEqual(requested(project["parser"]().parse_args(command)), expected, command)
        with self.assertRaises(SystemExit):
            project["parser"]().parse_args(["area", "--main-menu", "--no-main-menu"])

    def test_area_refuses_a_build_exported_before_the_main_menu(self):
        # ARC-UI-012: the menu's argument is refused by older builds, so `area` stops first with the way out.
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        run = project["run"]
        try:
            head = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL).strip()
            older = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", project["MAIN_MENU_COMMIT"] + "~1"], text=True,
                                            stderr=subprocess.DEVNULL).strip()
        except (OSError, subprocess.CalledProcessError):
            self.skipTest("needs a git checkout holding the main menu's history")
        self.assertTrue(project["build_has_main_menu"]({"candidate": {"commit": head}}))
        self.assertFalse(project["build_has_main_menu"]({"candidate": {"commit": older}}))
        self.assertFalse(project["build_has_main_menu"]({"candidate": {"commit": "0" * 40}}), "an unknown commit is not trusted")
        self.assertFalse(project["build_has_main_menu"]({}))
        conf = {"artifactRoots": {project["ROOT_ID"]: str(self.root)}, "launchSelection": {}}
        for commit, expected in ((older, 2), (head, 0)):
            calls = []
            with patch.dict(run.__globals__, {"local": lambda *arguments: calls.append(arguments) or 0, "build_has_area_mode": lambda manifest: True}), \
                    patch.object(run.__globals__["manifests"], "resolve_current",
                                 lambda root, tool, *, root_id, selected_task: (self.root / "build" / "manifest.json", {"candidate": {"commit": commit}}, None)), \
                    patch.object(run.__globals__["config"], "load_config", lambda: conf), \
                    patch.object(run.__globals__["config"], "save_config", lambda value: None), \
                    patch("sys.stderr", new_callable=io.StringIO) as stderr:
                self.assertEqual(expected, run(project["parser"]().parse_args(["area", "--dry-run"])))
            if expected:
                self.assertEqual(calls, [], "nothing is launched")
                self.assertIn("--no-main-menu", stderr.getvalue())
            else:
                self.assertEqual(calls[1][-3:], ("Arcanum1-024-fixed", "--main-menu", "--dry-run"))

    def test_the_debug_mode_is_passed_in_map_mode_and_refused_by_older_builds(self):
        # ARC-DEV-050: the viewer takes its lone --debug-mode out before parsing; the launchers pass it in map mode only.
        app = (REPO / "tools/terrain-viewer/Godot/TerrainViewerApp.cs").read_text()
        self.assertIn("ViewerDebugMode.Take(args)", app)
        self.assertIn(f'Flag = "{launch.DEBUG_MODE_FLAG}"', (REPO / "tools/terrain-viewer/Core/ViewerDebugMode.cs").read_text())
        inputs = self.inputs()
        self.assertNotIn("--debug-mode", launch.map_arguments(inputs, "Arcanum1-024-fixed"))
        self.assertEqual(launch.map_arguments(inputs, "Arcanum1-024-fixed", "elf-male", debug_mode=True)[-1], "--debug-mode")
        self.assertIn('launcher.add_argument("--debug-mode"', (REPO / "tools/arcanum-local").read_text())
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        for command in (["area", "--debug-mode"], ["map", "crash site cave", "--debug-mode"]):
            self.assertTrue(project["parser"]().parse_args(command).debug_mode, command)
        self.assertFalse(project["parser"]().parse_args(["area"]).debug_mode)
        run = project["run"]
        try:
            head = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL).strip()
            older = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", project["DEBUG_MODE_COMMIT"] + "~1"], text=True,
                                            stderr=subprocess.DEVNULL).strip()
        except (OSError, subprocess.CalledProcessError):
            self.skipTest("needs a git checkout holding the debug mode's history")
        self.assertTrue(project["build_has_debug_mode"]({"candidate": {"commit": head}}))
        self.assertFalse(project["build_has_debug_mode"]({"candidate": {"commit": older}}))
        conf = {"artifactRoots": {project["ROOT_ID"]: str(self.root)}, "launchSelection": {}}
        for commit, expected in ((older, 2), (head, 0)):
            calls = []
            with patch.dict(run.__globals__, {"local": lambda *arguments: calls.append(arguments) or 0, "build_has_area_mode": lambda manifest: True}), \
                    patch.object(run.__globals__["manifests"], "resolve_current",
                                 lambda root, tool, *, root_id, selected_task: (self.root / "build" / "manifest.json", {"candidate": {"commit": commit}}, None)), \
                    patch.object(run.__globals__["config"], "load_config", lambda: conf), \
                    patch.object(run.__globals__["config"], "save_config", lambda value: None), \
                    patch("sys.stderr", new_callable=io.StringIO) as stderr:
                self.assertEqual(expected, run(project["parser"]().parse_args(["area", "--debug-mode", "--dry-run"])))
            if expected:
                self.assertEqual(calls, [], "nothing is launched")
                self.assertIn("without --debug-mode", stderr.getvalue())
            else:
                self.assertEqual(calls[1][-4:], ("Arcanum1-024-fixed", "--main-menu", "--debug-mode", "--dry-run"))

    def test_the_area_mode_is_sent_as_area_and_older_builds_are_refused_with_a_re_export(self):
        # ARC-DEV-154: the viewer's `--mode area` replaces `crash-site-area`, which stays a permanent alias the launchers accept
        # but never send; a build exported before the rename refuses `area`, so `./arcanum area|map` stops first.
        inputs = self.inputs()
        self.assertEqual("area", launch.canonical_mode("crash-site-area"))
        self.assertEqual("area", launch.canonical_mode("area"))
        self.assertEqual("world-diagnostic", launch.canonical_mode("world-diagnostic"))
        for mode in ("area", "crash-site-area"):
            self.assertEqual(launch.terrain_arguments(inputs, mode)[:2], ["--mode", "area"], mode)
        self.assertEqual(launch.map_arguments(inputs, "Arcanum1-024-fixed")[:2], ["--mode", "area"])
        self.assertEqual(launch.pinned_roles("crash-site-area", "ARC-RENDER-008"), launch.pinned_roles("area", "ARC-RENDER-008"))
        local = (REPO / "tools/arcanum-local").read_text()
        self.assertIn("launch.canonical_mode(args.mode)", local)
        self.assertIn('"area", "crash-site-area"))', local)
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        run = project["run"]
        try:
            head = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL).strip()
            older = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", project["AREA_MODE_COMMIT"] + "~1"], text=True,
                                            stderr=subprocess.DEVNULL).strip()
        except (OSError, subprocess.CalledProcessError):
            self.skipTest("needs a git checkout holding the area mode's history")
        self.assertTrue(project["build_has_area_mode"]({"candidate": {"commit": head}}))
        self.assertFalse(project["build_has_area_mode"]({"candidate": {"commit": older}}))
        self.assertFalse(project["build_has_area_mode"]({"candidate": {"commit": "0" * 40}}), "an unknown commit is not trusted")
        self.assertFalse(project["build_has_area_mode"]({}))
        conf = {"artifactRoots": {project["ROOT_ID"]: str(self.root)}, "launchSelection": {}}
        for command in (["area"], ["map", "crash site cave"]):
            for commit, expected in ((older, 2), (head, 0)):
                calls = []
                with patch.dict(run.__globals__, {"local": lambda *arguments: calls.append(arguments) or 0}), \
                        patch.object(run.__globals__["manifests"], "resolve_current",
                                     lambda root, tool, *, root_id, selected_task: (self.root / "build" / "manifest.json", {"candidate": {"commit": commit}}, None)), \
                        patch.object(run.__globals__["config"], "load_config", lambda: conf), \
                        patch.object(run.__globals__["config"], "save_config", lambda value: None), \
                        patch("sys.stderr", new_callable=io.StringIO) as stderr:
                    self.assertEqual(expected, run(project["parser"]().parse_args([*command, "--dry-run"])), (command, commit))
                if expected:
                    self.assertEqual(calls, [], "nothing is launched")
                    self.assertIn("re-export", stderr.getvalue())
                    self.assertIn("--mode area", stderr.getvalue())
                else:
                    self.assertEqual(calls[1][:4], ("launch", "terrain", "--mode", "area"), command)

    def test_viewer_modes_match_the_launcher_modes(self):
        source = (REPO / "tools/terrain-viewer/Core/TerrainViewerArguments.cs").read_text()
        # ARC-DEV-154: a case may carry the old name as an alias (`"area" or "crash-site-area" => ...`); the launcher names the first.
        cases = re.findall(r'((?:"[a-z-]+"(?: or )?)+) => TerrainViewerMode\.', source)
        names = [re.findall(r'"([a-z-]+)"', case) for case in cases]
        self.assertEqual({group[0] for group in names}, set(launch.MODE_ROLES), "a viewer mode was added or removed: update MODE_ROLES")
        self.assertEqual({alias: group[0] for group in names for alias in group[1:]}, launch.MODE_ALIASES)

    def test_role_identities_match_the_data_layer_and_evidence_pins(self):
        profile = (REPO / "src/Arcanum.Data/Area/OriginalProfile.cs").read_text()
        for role in ("module", "actorResource", "terrainResource", "areaMobile"):
            _, size, sha256 = launch.GAME_ROLES[role]
            self.assertIn(f"{size}, \"{sha256}\"", profile, role)
        cave = (REPO / "src/Arcanum.Data/Area/CaveProfile.cs").read_text()
        _, size, sha256 = launch.GAME_ROLES["caveMobile"]
        self.assertIn(f"NormalGameMobileBytes = {size};", cave)
        self.assertIn(f"NormalGameMobileSha256 = \"{sha256}\";", cave)
        inventory = (REPO / "docs/research/area-inventory.md").read_text()
        _, size, sha256 = launch.GAME_ROLES["areaEffects"]
        self.assertIn(f"| {size} | `{sha256}` |", inventory)
        dat = (REPO / "docs/research/dat-evidence.md").read_text()
        _, size, sha256 = launch.GAME_ROLES["areaPatch"]
        self.assertIn(f"| {size:,} | `{sha256}` |", dat)

    def test_project_launcher_modes_bind_known_modes_and_tasks(self):
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        modes = project["MODES"]
        # Map mode (ARC-RENDER-017) exists in the universal build (ARC-DEV-015) and the newest AREA task.
        self.assertEqual(project["MAP_MODE"], ("area", (launch.UNIVERSAL_TASK, "ARC-RENDER-008")))
        # Same pairing arcanum-local enforces at launch: area mode only from
        # ARC-RENDER-007, ARC-INPUT-001 (diagnostic palette), ARC-WORLD-007 (LMB click-to-move) ARC-WORLD-009 (cave) or ARC-RENDER-008 (passability overlay).
        # Since ARC-RENDER-028 no MODES entry launches the pinned crash-site-area profile (`area` is map mode).
        allowed = {"world-diagnostic": {"ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003"}}
        for mode, tasks in modes.values():
            self.assertIn(mode, launch.MODE_ROLES)
            self.assertTrue(tasks and len(set(tasks)) == len(tasks), mode)
            for task in tasks:
                self.assertIn(task, allowed[mode], mode)

    def test_area_is_map_mode_on_the_main_world_map(self):
        # ARC-RENDER-028: the pinned Crash Site exterior is no launcher mode; `area` opens the main map in map mode.
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        self.assertNotIn("area", project["MODES"])
        self.assertEqual(project["AREA_MAP"], "Arcanum1-024-fixed")
        args = project["parser"]().parse_args(["area", "--hero", "elf-male", "--sector", "1409,1314", "--dry-run"])
        self.assertEqual((args.mode, args.hero, args.sector, args.func), ("area", "elf-male", "1409,1314", project["run"]))
        argv = launch.map_arguments(self.inputs(), project["AREA_MAP"], "elf-male", "1409,1314")
        self.assertEqual(argv[-6:], ["--area-map", "Arcanum1-024-fixed", "--hero", "elf-male", "--area-map-sector", "1409,1314"])
        for flag in ("--prototypes", "--area-mobile", "--area-effects", "--area-patch", "--area-cave-mobile"):
            self.assertNotIn(flag, argv)

    def test_a_game_folder_start_opens_what_area_opens(self):
        # ARC-DEV-046: a viewer start without inputs (a player's build) opens the stored game folder's main map like `./arcanum area`.
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        source = (REPO / "tools/terrain-viewer/Core/TerrainViewerArguments.cs").read_text()
        self.assertIn(f'GameFolderStartMap = "{project["AREA_MAP"]}";', source)
        self.assertIn('GameFolderSourceId = "local-original";', source)
        self.assertIn('config.safe_source_id(source_id or "local-original")', (REPO / "tools/arcanum-local").read_text())
        for role, flag in (("module", "--module"), ("terrainResource", "--resource"), ("actorResource", "--actor-resource"), ("gameRoot", "--game-root")):
            self.assertIn(role, launch.MAP_ROLES)
            self.assertIn(f'"{flag}", inputs.', source)
        self.assertEqual(("modules/Arcanum.dat", "arcanum1.dat", "arcanum2.dat"),
                         tuple(launch.GAME_ROLES[role][0] for role in ("module", "actorResource", "terrainResource")))

    def test_area_run_launches_the_map_mode_build_with_the_main_map(self):
        # ARC-RENDER-028: `run` for `area` tries the map-mode tasks in order and launches map mode on the main map.
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        run = project["run"]
        conf = {"artifactRoots": {project["ROOT_ID"]: str(self.root)}, "launchSelection": {"terrain": "previous"}}
        tried, calls = [], []

        def resolve(root, tool, *, root_id, selected_task):
            tried.append(selected_task)
            if selected_task == launch.UNIVERSAL_TASK:
                raise ValueError("no build")
            return self.root / "build" / "manifest.json", None, None

        saved = []
        with patch.dict(run.__globals__, {"local": lambda *arguments: calls.append(arguments) or 0, "build_has_area_mode": lambda manifest: True}), \
                patch.object(run.__globals__["manifests"], "resolve_current", resolve), \
                patch.object(run.__globals__["config"], "load_config", lambda: conf), \
                patch.object(run.__globals__["config"], "save_config", lambda value: saved.append(value)):
            args = project["parser"]().parse_args(["area", "--hero", "elf-male", "--sector", "1409,1314", "--dry-run"])
            self.assertEqual(0, run(args))
        self.assertEqual(tried, list(project["MAP_MODE"][1]))
        self.assertEqual(calls[0][:3], ("configure", "select-launch", "--tool"))
        self.assertEqual(calls[1], ("launch", "terrain", "--mode", "area", "--area-map", "Arcanum1-024-fixed", "--hero", "elf-male",
                                    "--area-map-sector", "1409,1314", "--dry-run"))
        self.assertEqual(saved[-1]["launchSelection"]["terrain"], "previous", "a dry run restores the previous selection")

    def test_the_oracle_launcher_matches_the_tool_and_the_tests(self):
        # ARC-DEV-007: `./arcanum oracle` names the oracle's functions, their OriginalExe tests and the pinned executable.
        sys.path.insert(0, str(REPO / "tools/original-oracle"))
        try:
            oracle_functions = __import__("oracle_functions")
            oracle_pe = __import__("oracle_pe")
        finally:
            sys.path.remove(str(REPO / "tools/original-oracle"))
        self.assertEqual(list(launch.ORACLE_FUNCTIONS), [f"0x{address:X}" for address in oracle_functions.FUNCTIONS])
        tests = "\n".join((REPO / path).read_text() for path in launch.ORACLE_TEST_CLASSES)
        self.assertIn('Category = "OriginalExe";', tests)
        for spec in oracle_functions.FUNCTIONS.values():
            name = launch.ORACLE_FUNCTIONS[f"0x{spec.address:X}"]
            body = re.search(rf"\[TestMethod, TestCategory\(Category\)\]\s+public void {name}\(\)\s*\{{\s*var file = OracleFile\.Load\(\"([0-9A-F]+)\", \"([a-z-]+)\"", tests)
            self.assertIsNotNone(body, name)
            self.assertEqual(body.groups(), (spec.key, spec.name), name)
        self.assertTrue((REPO / launch.ORACLE_TESTS).is_file())
        sha = launch.ORIGINAL_EXE_SHA256
        self.assertEqual(sha, oracle_pe.PINNED_EXE_SHA256)
        self.assertIn(f'ExeSha256 = "{sha}";', tests)
        self.assertIn(f'ExeSha256 = "{sha}";', (REPO / "tests/Arcanum.Session.Tests/Verification/FunctionRegistryTests.cs").read_text())
        self.assertIn(f"`{sha}`", (REPO / "docs/research/original-executable-check.md").read_text())
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        for function in launch.ORACLE_FUNCTIONS:
            self.assertEqual(project["parser"]().parse_args(["oracle", "--function", function]).function, function)
        with patch("sys.stderr"), self.assertRaises(SystemExit):
            project["parser"]().parse_args(["oracle", "--function", "0x401000"])
        # ARC-DEV-008: an F3 row of the function registry claims an oracle run, so its function must have an oracle.
        rows = list(csv.DictReader((REPO / "docs/verification/functions.csv").open(newline="")))
        f3 = {row["address"] for row in rows if row["level"] == "F3"}
        self.assertTrue(f3 <= set(launch.ORACLE_FUNCTIONS), f3 - set(launch.ORACLE_FUNCTIONS))
        self.assertTrue((REPO / launch.ORACLE_RECEIPTS).is_dir())

    def test_discovery_records_identity_and_rejects_links(self):
        game = self.root / "game"
        fixture = {"module": b"module", "areaMobile": b"mobile"}
        roles = {role: (f"files/{role}.bin", len(data), hashlib.sha256(data).hexdigest()) for role, data in fixture.items()}
        roles["prototypes"] = ("proto", None, None)
        roles["gameRoot"] = ("", None, None)
        for role, data in fixture.items():
            (game / "files").mkdir(parents=True, exist_ok=True); (game / roles[role][0]).write_bytes(data)
        (game / "proto").mkdir(); (game / "proto" / "a.pro").write_bytes(b"x")
        with patch.object(launch, "GAME_ROLES", roles):
            found, missing, _ = launch.discover_inputs(game)
            self.assertIn("gameRoot", missing)
            (game / "modules").mkdir()
            self.assertEqual((sorted(found := launch.discover_inputs(game)[0]), launch.discover_inputs(game)[1]),
                             (["areaMobile", "gameRoot", "module", "prototypes"], []))
            self.assertEqual(found["gameRoot"], str(game.resolve()))
            self.assertEqual(launch.discover_inputs(game)[2]["pinnedVerified"], ["areaMobile", "module"])
            # ARC-DEV-015: another installation's file is found and recorded, only not pinned.
            (game / roles["module"][0]).write_bytes(b"changed")
            found, missing, installation = launch.discover_inputs(game)
            self.assertEqual((missing, installation["pinnedVerified"]), ([], ["areaMobile"]))
            self.assertEqual(installation["roles"]["module"]["sha256"], hashlib.sha256(b"changed").hexdigest())
            (game / roles["module"][0]).unlink(); (game / roles["module"][0]).symlink_to(game / roles["areaMobile"][0])
            self.assertIn("module", launch.discover_inputs(game)[1])
            (game / "proto" / "a.pro").unlink()
            self.assertIn("prototypes", launch.discover_inputs(game)[1])

    def test_write_inputs_fails_closed_without_the_map_roles(self):
        home = self.root / "home"; home.mkdir(); game = self.root / "empty-game"; game.mkdir()
        env = dict(os.environ, ARCANUM_LOCAL_HOME=str(home))
        completed = subprocess.run([sys.executable, "-B", str(REPO / "tools/arcanum-local"), "configure", "discover",
                                    "--game-root", str(game), "--write-inputs"], env=env, capture_output=True, text=True)
        self.assertNotEqual(completed.returncode, 0)
        conf = json.loads((home / "config-v1.json").read_text()) if (home / "config-v1.json").exists() else config.default_config()
        self.assertEqual(conf["inputs"], {})


if __name__ == "__main__":
    unittest.main()
