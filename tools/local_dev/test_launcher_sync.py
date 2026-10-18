"""The ordinary launcher must stay in step with the viewer's argument parser and pinned inputs."""
from __future__ import annotations

import csv
import hashlib
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
        passed |= {token for token in launch.terrain_arguments(self.inputs(), "crash-site-area", hero="elf-male") if token.startswith("--")}
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
        self.assertEqual(argv[:2], ["--mode", "crash-site-area"])
        self.assertEqual(argv[-2:], ["--area-map", "caladon-castle main floor"])
        # The map's inputs come from the content stack: no prototype folder or Crash Site inputs.
        for flag in ("--prototypes", "--area-mobile", "--area-effects", "--area-patch", "--area-cave-mobile"):
            self.assertNotIn(flag, argv)
        for name in ("", " ", "maps/x", "a\\b", "line\nbreak", "x" * (launch.MAX_MAP_NAME + 1)):
            with self.assertRaises(ValueError, msg=repr(name)):
                launch.map_arguments(inputs, name)
        with self.assertRaises(ValueError):
            launch.map_arguments({role: path for role, path in inputs.items() if role != "gameRoot"}, "void")

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

    def test_viewer_modes_match_the_launcher_modes(self):
        source = (REPO / "tools/terrain-viewer/Core/TerrainViewerArguments.cs").read_text()
        modes = set(re.findall(r'"([a-z-]+)" => TerrainViewerMode\.', source))
        self.assertEqual(modes, set(launch.MODE_ROLES), "a viewer mode was added or removed: update MODE_ROLES")

    def test_role_identities_match_the_data_layer_and_evidence_pins(self):
        profile = (REPO / "src/Arcanum.Data/Area/OriginalProfile.cs").read_text()
        for role in ("module", "actorResource", "terrainResource", "areaMobile"):
            _, size, sha256 = launch.GAME_ROLES[role]
            self.assertIn(f"{size}, \"{sha256}\"", profile, role)
        cave = (REPO / "src/Arcanum.Data/Area/CaveProfile.cs").read_text()
        _, size, sha256 = launch.GAME_ROLES["caveMobile"]
        self.assertIn(f"NormalGameMobileBytes = {size};", cave)
        self.assertIn(f"NormalGameMobileSha256 = \"{sha256}\";", cave)
        inventory = (REPO / "docs/research/crash-site-area-inventory.md").read_text()
        _, size, sha256 = launch.GAME_ROLES["areaEffects"]
        self.assertIn(f"| {size} | `{sha256}` |", inventory)
        dat = (REPO / "docs/research/dat-evidence.md").read_text()
        _, size, sha256 = launch.GAME_ROLES["areaPatch"]
        self.assertIn(f"| {size:,} | `{sha256}` |", dat)

    def test_project_launcher_modes_bind_known_modes_and_tasks(self):
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        modes = project["MODES"]
        # Map mode (ARC-RENDER-017) exists in the universal build (ARC-DEV-015) and the newest AREA task.
        self.assertEqual(project["MAP_MODE"], ("crash-site-area", (launch.UNIVERSAL_TASK, "ARC-RENDER-008")))
        # Same pairing arcanum-local enforces at launch: crash-site-area only from
        # ARC-RENDER-007, ARC-INPUT-001 (diagnostic palette), ARC-WORLD-007 (LMB click-to-move) ARC-WORLD-009 (cave) or ARC-RENDER-008 (passability overlay).
        allowed = {"crash-site-area": {"ARC-RENDER-007", "ARC-INPUT-001", "ARC-WORLD-007", "ARC-WORLD-009", "ARC-RENDER-008"},
                   "world-diagnostic": {"ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003"}}
        for mode, tasks in modes.values():
            self.assertIn(mode, launch.MODE_ROLES)
            self.assertTrue(tasks and len(set(tasks)) == len(tasks), mode)
            for task in tasks:
                self.assertIn(task, allowed[mode], mode)

    def test_the_oracle_launcher_matches_the_tool_and_the_tests(self):
        # ARC-DEV-007: `./arcanum oracle` names the oracle's functions, their OriginalExe tests and the pinned executable.
        sys.path.insert(0, str(REPO / "tools/original-oracle"))
        try:
            oracle_functions = __import__("oracle_functions")
            oracle_pe = __import__("oracle_pe")
        finally:
            sys.path.remove(str(REPO / "tools/original-oracle"))
        self.assertEqual(list(launch.ORACLE_FUNCTIONS), [f"0x{address:X}" for address in oracle_functions.FUNCTIONS])
        tests = (REPO / launch.ORACLE_TEST_CLASS).read_text()
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
