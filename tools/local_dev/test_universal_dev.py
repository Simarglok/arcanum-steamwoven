"""ARC-DEV-015: the launcher and the export without pinned file identities; public synthetic fixtures only."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import unittest
from unittest.mock import patch

from tools.local_dev import config, launch, manifests, retention
from tools.local_dev import test_local_dev as fixtures

REPO = Path(__file__).resolve().parents[2]


class UniversalDevTests(unittest.TestCase):
    root: Path
    setUp = fixtures.LocalDevTests.setUp
    tearDown = fixtures.LocalDevTests.tearDown
    v2_manifest = fixtures.LocalDevTests.v2_manifest
    conf = fixtures.LocalDevTests.conf
    authority_fixture = fixtures.LocalDevTests.authority_fixture

    def game(self, pinned: dict[str, bytes] | None = None) -> tuple[Path, dict[str, tuple[str, int | None, str | None]]]:
        """A game folder holding every role at its relative path; the roles named in `pinned` hold their pinned bytes."""
        game = self.root / "game"
        pinned = pinned or {}
        roles: dict[str, tuple[str, int | None, str | None]] = {}
        for role, (relative, size, _) in launch.GAME_ROLES.items():
            if role == "gameRoot":
                roles[role] = (relative, None, None); continue
            path = game / relative
            if size is None:
                path.mkdir(parents=True, exist_ok=True); (path / "a.pro").write_bytes(b"x")
                roles[role] = (relative, None, None); continue
            data = pinned.get(role, b"another installation's " + role.encode())
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
            # The pin is the fixture's own pinned bytes when named, else bytes this installation does not have.
            pin = pinned.get(role, b"the pinned " + role.encode())
            roles[role] = (relative, len(pin), hashlib.sha256(pin).hexdigest())
        (game / "modules").mkdir(parents=True, exist_ok=True)
        return game, roles

    def test_discovery_records_every_present_role_and_which_pins_match(self):
        game, roles = self.game({"module": b"pinned module"})
        with patch.object(launch, "GAME_ROLES", roles):
            found, missing, installation = launch.discover_inputs(game)
            self.assertEqual(missing, [])
            self.assertEqual(set(found), set(roles))
            self.assertEqual(installation["pinnedVerified"], ["module"])
            module = installation["roles"]["module"]
            self.assertEqual((module["bytes"], module["sha256"], module["pinned"]), (13, hashlib.sha256(b"pinned module").hexdigest(), True))
            self.assertFalse(installation["roles"]["actorResource"]["pinned"])
            self.assertEqual(installation["roles"]["prototypes"]["files"], 1)
            # A link is never followed; a prototype folder without .pro files is absent.
            target = game / roles["actorResource"][0]
            target.unlink(); target.symlink_to(game / roles["module"][0])
            (game / roles["prototypes"][0] / "a.pro").unlink()
            found, missing, installation = launch.discover_inputs(game)
            self.assertEqual(sorted(missing), ["actorResource", "prototypes"])
            self.assertNotIn("actorResource", installation["roles"])

    def test_setup_writes_the_inputs_of_any_installation_and_refuses_one_without_map_roles(self):
        game, roles = self.game()
        environment = dict(os.environ)
        cli = runpy.run_path(str(REPO / "tools/arcanum-local"))
        with patch.object(launch, "GAME_ROLES", roles):
            args = cli["parser"]().parse_args(["configure", "discover", "--game-root", str(game), "--write-inputs"])
            with patch("builtins.print") as printed:
                cli["command_configure"](args)
            report = json.loads(printed.call_args[0][0])
        conf = config.load_config()
        self.assertEqual(set(conf["inputs"]), set(roles) | {"sourceId"})
        self.assertEqual(conf["installation"]["pinnedVerified"], [])
        self.assertEqual(report["pinnedDiffer"], sorted(role for role, (_, size, _) in roles.items() if size is not None))
        # Without a role map mode needs, nothing is written.
        empty = self.root / "empty"; empty.mkdir()
        home = self.root / "other-home"; home.mkdir()
        completed = subprocess.run([sys.executable, "-B", str(REPO / "tools/arcanum-local"), "configure", "discover", "--game-root", str(empty),
                                    "--write-inputs"], env=dict(environment, ARCANUM_LOCAL_HOME=str(home)), capture_output=True, text=True)
        self.assertNotEqual(completed.returncode, 0)
        self.assertIn("roles every mode needs", completed.stderr)

    def inputs(self) -> dict[str, str]:
        result = {"sourceId": "public-fixture"}
        for role, (relative, size, _) in launch.GAME_ROLES.items():
            path = self.root / "roles" / role
            if role == "gameRoot" or size is None:
                path.mkdir(parents=True, exist_ok=True); (path / "a.pro").write_bytes(b"x")
            else:
                path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"public fixture")
            result[role] = str(path)
        return result

    def test_pinned_modes_need_their_pinned_roles_verified_and_map_mode_does_not(self):
        inputs = self.inputs()
        everything = [role for role, (_, size, _) in launch.GAME_ROLES.items() if size is not None]
        for mode in launch.MODE_ROLES:
            launch.terrain_arguments(inputs, mode)                       # an older configuration: no record, no gate
            launch.terrain_arguments(inputs, mode, verified=everything)
            if launch.pinned_roles(mode):
                with self.assertRaisesRegex(ValueError, "pinned"):
                    launch.terrain_arguments(inputs, mode, verified=[])
        # The cave role counts only for a build of its task.
        cave = [role for role in everything if role != "caveMobile"]
        launch.terrain_arguments(inputs, "crash-site-area", task="ARC-WORLD-007", verified=cave)
        with self.assertRaisesRegex(ValueError, "caveMobile"):
            launch.terrain_arguments(inputs, "crash-site-area", task="ARC-WORLD-009", verified=cave)
        self.assertEqual(launch.pinned_roles("crash-site-area", "ARC-RENDER-008")[-1], "caveMobile")
        launch.map_arguments(inputs, "crash site cave")

    def launch_cli(self, task: str, *arguments: str, installation: dict | None = None) -> tuple[dict, object]:
        self.authority_fixture(task=task, tool="terrain")
        conf = config.load_config(); conf["inputs"] = self.inputs()
        if installation is not None:
            conf["installation"] = installation
        conf["launchSelection"] = {"terrain": f"test:{task}:" + "a" * 40}; config.save_config(conf)
        cli = runpy.run_path(str(REPO / "tools/arcanum-local"))
        return cli, cli["parser"]().parse_args(["launch", "terrain", *arguments, "--dry-run"])

    def test_a_universal_build_launches_map_mode_only(self):
        cli, args = self.launch_cli(launch.UNIVERSAL_TASK, "--mode", "crash-site-area", "--area-map", "crash site cave", "--hero", "elf-male",
                                    installation={"pinnedVerified": []})
        with patch.object(launch, "start", return_value={"started": False}) as start:
            cli["command_launch"](args)
            argv = start.call_args[0][1]
        self.assertEqual(argv[argv.index("--area-map") + 1], "crash site cave")
        for mode in (("--mode", "crash-site-area"), ("--mode", "world-diagnostic"), ()):
            with self.subTest(mode=mode):
                self.tearDown(); self.setUp()
                cli, args = self.launch_cli(launch.UNIVERSAL_TASK, *mode)
                with patch.object(launch, "start") as start, self.assertRaisesRegex(ValueError, "map mode only"):
                    cli["command_launch"](args)
                start.assert_not_called()

    def test_a_universal_build_takes_the_main_menu(self):
        # ARC-UI-011: `--main-menu` reaches the viewer as `--main-menu true` after the map arguments.
        cli, args = self.launch_cli(launch.UNIVERSAL_TASK, "--mode", "crash-site-area", "--area-map", "Arcanum1-024-fixed", "--main-menu",
                                    installation={"pinnedVerified": []})
        with patch.object(launch, "start", return_value={"started": False}) as start:
            cli["command_launch"](args)
            argv = start.call_args[0][1]
        self.assertEqual(argv[-2:], ["--main-menu", "true"])
        self.tearDown(); self.setUp()
        cli, args = self.launch_cli(launch.UNIVERSAL_TASK, "--mode", "world-diagnostic", "--main-menu")
        with patch.object(launch, "start") as start, self.assertRaisesRegex(ValueError, "main menu|map mode only"):
            cli["command_launch"](args)
        start.assert_not_called()

    def test_a_pinned_area_launch_refuses_an_installation_whose_pins_differ(self):
        cli, args = self.launch_cli("ARC-RENDER-008", "--mode", "crash-site-area", installation={"pinnedVerified": ["module"]})
        with patch.object(launch, "start") as start, self.assertRaisesRegex(ValueError, "pinned"):
            cli["command_launch"](args)
        start.assert_not_called()
        # Map mode on the same build and installation runs.
        self.tearDown(); self.setUp()
        cli, args = self.launch_cli("ARC-RENDER-008", "--mode", "crash-site-area", "--area-map", "void", installation={"pinnedVerified": []})
        with patch.object(launch, "start", return_value={"started": False}) as start:
            cli["command_launch"](args)
            start.assert_called_once()

    def test_a_malformed_installation_record_verifies_nothing(self):
        self.assertIsNone(launch.verified_roles({}))
        self.assertEqual(launch.verified_roles({"installation": {"pinnedVerified": ["module"]}}), ["module"])
        for record in (None, "module", [], {}, {"pinnedVerified": "module"}, {"pinnedVerified": ["module", 1]}):
            with self.subTest(record=record):
                self.assertEqual(launch.verified_roles({"installation": record}), [])
        # A launch of a pinned mode with such a record fails closed.
        cli, args = self.launch_cli("ARC-RENDER-008", "--mode", "crash-site-area", installation={"pinnedVerified": "module"})
        with patch.object(launch, "start") as start, self.assertRaisesRegex(ValueError, "pinned"):
            cli["command_launch"](args)
        start.assert_not_called()

    def test_status_names_the_pinned_roles_that_differ_and_explicit_inputs_drop_the_record(self):
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        conf = config.load_config(); conf["inputs"] = self.inputs(); conf["installation"] = {"pinnedVerified": []}; config.save_config(conf)

        def lines() -> list[str]:
            with patch("builtins.print") as printed:
                project["status"](None)
            return [" ".join(str(part) for part in call.args) for call in printed.call_args_list]

        world = next(line for line in lines() if line.startswith("world"))
        self.assertIn("pinned identity differs: " + ", ".join(launch.pinned_roles("world-diagnostic")), world)
        self.assertTrue(any(line.startswith("map: ready") for line in lines()))
        # ARC-RENDER-028: `area` is map mode, ready on any installation.
        self.assertTrue(any(line.startswith("area: ready") for line in lines()))
        # No record (an older configuration): the pinned modes are ready as before.
        conf = config.load_config(); conf.pop("installation"); config.save_config(conf)
        self.assertTrue(next(line for line in lines() if line.startswith("world")).endswith(": ready"))
        # `configure inputs` replaces the inputs and drops a record setup wrote.
        conf = config.load_config(); conf["installation"] = {"pinnedVerified": []}; config.save_config(conf)
        inputs = self.inputs()
        cli = runpy.run_path(str(REPO / "tools/arcanum-local"))
        args = cli["parser"]().parse_args(["configure", "inputs", "--module", inputs["module"], "--terrain-resource", inputs["terrainResource"],
                                           "--actor-resource", inputs["actorResource"], "--prototypes", inputs["prototypes"]])
        with patch("builtins.print"):
            cli["command_configure"](args)
        self.assertNotIn("installation", config.load_config())

    def test_the_universal_export_carries_no_pinned_gate(self):
        self.assertIn(launch.UNIVERSAL_TASK, manifests.SUPPORTED_TOOLS["terrain"])
        required = retention.REQUIRED_SUCCESS_GATES["terrain"] | retention.REQUIRED_TASK_SUCCESS_GATES[launch.UNIVERSAL_TASK]
        self.assertEqual(required, retention.REQUIRED_SUCCESS_GATES["terrain"] | {"focused-tests", "dual-hostfxr"})
        self.assertFalse(any("original" in gate or "persistence" in gate or gate.endswith(("-grid", "-seams", "-nearest")) for gate in required))
        tools = REPO / "tools/terrain-viewer/Godot/tools"
        writer = runpy.run_path(str(tools / "terrain_write_manifest.py"), run_name="terrain_write_manifest")
        self.assertEqual(writer["TASKS"][launch.UNIVERSAL_TASK], "world-builds")
        self.assertNotIn(launch.UNIVERSAL_TASK, writer["AREA_TASKS"])
        script = (tools / "validate-macos.sh").read_text()
        self.assertIn("ARC-DEV-001|ARC-DEV-015) pinned=0", script)
        self.assertNotIn("ARC-DEV-015) area_task=1", script)
        # `./arcanum map` prefers the universal build and falls back to the newest AREA build.
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        self.assertEqual(project["MAP_MODE"], ("crash-site-area", (launch.UNIVERSAL_TASK, "ARC-RENDER-008")))


if __name__ == "__main__":
    unittest.main()
