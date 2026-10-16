"""ARC-RENDER-007 launcher, manifest and retention bindings; public synthetic fixtures only."""
from __future__ import annotations

from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch

from tools.local_dev import config, grid_export_gates, launch, manifests, retention
from tools.local_dev import test_local_dev as fixtures


class Render007DevTests(unittest.TestCase):
    def test_motion_oracle_is_frozen_and_matches_the_native_probe_literals(self):
        oracle = runpy.run_path(str(Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/area_motion_oracle.py"))["oracle"]()
        probe = (Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/TerrainViewerAreaGridTest.cs").read_text()
        self.assertIn("f5b26e1784a392e4ed6b299090912e6c7b800a16ad005880c5b95fe018b40f9e", probe)
        self.assertIn("new TerrainRect({},{},{},{})".format(*oracle["canopyRect"]), probe)
        for view in oracle["views"]:
            self.assertIn('Placement("{}", new TerrainRect({},{},{},{}))'.format(view["name"], *view["actorRect"]), probe)
            self.assertIn('Render("{}",{},{},'.format(view["name"], *view["witness"]), probe)

    root: Path
    setUp = fixtures.LocalDevTests.setUp
    tearDown = fixtures.LocalDevTests.tearDown
    v2_manifest = fixtures.LocalDevTests.v2_manifest
    conf = fixtures.LocalDevTests.conf
    authority_snapshot = fixtures.LocalDevTests.authority_snapshot
    authority_fixture = fixtures.LocalDevTests.authority_fixture

    def roles(self, area=True):
        inputs = {"sourceId": "public-fixture"}
        for role in ("module", "terrainResource", "actorResource") + (("areaMobile", "areaEffects", "areaPatch") if area else ()):
            path = self.root / role
            path.write_bytes(b"public fixture")
            inputs[role] = str(path)
        prototypes = self.root / "prototypes"; prototypes.mkdir(exist_ok=True)
        inputs["prototypes"] = str(prototypes)
        return inputs

    def test_area_argv_requires_every_role_and_rejects_actor_overrides(self):
        inputs = self.roles()
        argv = launch.terrain_arguments(inputs, "crash-site-area")
        self.assertEqual(argv, ["--mode", "crash-site-area", "--module", inputs["module"],
            "--resource", inputs["terrainResource"], "--source-id", "public-fixture",
            "--prototypes", inputs["prototypes"], "--actor-resource", inputs["actorResource"],
            "--area-mobile", inputs["areaMobile"], "--area-effects", inputs["areaEffects"], "--area-patch", inputs["areaPatch"]])
        launch.assert_ordinary(argv)
        for role in inputs:
            with self.subTest(role=role), self.assertRaises(ValueError):
                launch.terrain_arguments({k: v for k, v in inputs.items() if k != role}, "crash-site-area")
        link = self.root / "mobile-link"; link.symlink_to(inputs["areaMobile"])
        with self.assertRaises(ValueError):
            launch.terrain_arguments(inputs | {"areaMobile": str(link)}, "crash-site-area")
        with self.assertRaises(ValueError):
            launch.terrain_arguments(inputs, "crash-site-area", {"actor_frame": "0"})
        # The WORLD argv is unchanged by the optional area role.
        self.assertNotIn("--area-mobile", launch.terrain_arguments(inputs, "world-diagnostic"))
        self.assertNotIn("--area-effects", launch.terrain_arguments(inputs, "world-diagnostic"))
        self.assertNotIn("--area-patch", launch.terrain_arguments(inputs, "world-diagnostic"))

    def test_task_registry_gates_and_retention_bind_the_literal_task(self):
        self.assertIn("ARC-RENDER-007", manifests.SUPPORTED_TOOLS["terrain"])
        self.assertNotIn("ARC-RENDER-007", manifests.SUPPORTED_TOOLS["gamepad"])
        world, area = grid_export_gates.gates_for("ARC-WORLD-003"), grid_export_gates.gates_for("ARC-RENDER-007")
        added = {f"{host}-{name}": f"{host}-{name}.log" for host in ("source", "exported")
                 for name in ("area-original", "area-grid", "area-seams", "area-nearest")}
        self.assertEqual(8, len(added))
        self.assertEqual({**world, **added}, area)
        with self.assertRaises(ValueError):
            grid_export_gates.gates_for("ARC-RENDER-006")
        self.assertEqual(retention.REQUIRED_TASK_SUCCESS_GATES["ARC-WORLD-003"] | set(added),
                         retention.REQUIRED_TASK_SUCCESS_GATES["ARC-RENDER-007"])
        script = (Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/validate-macos.sh").read_text()
        for log in added.values():
            self.assertIn(log, script)
        with tempfile.TemporaryDirectory(dir=str(self.root)) as evidence:
            # A WORLD-003 receipt name space cannot run an area gate.
            with self.assertRaises(ValueError):
                grid_export_gates.run_gate(Path(evidence), "a" * 40, "source-area-original.log", ["/usr/bin/true"])
            with self.assertRaises(ValueError):
                grid_export_gates.validate_reports(Path(evidence), "a" * 40, "ARC-RENDER-007")

    def test_cli_crash_site_area_requires_an_exact_render007_artifact(self):
        self.authority_fixture(task="ARC-WORLD-003", tool="terrain")
        conf = config.load_config(); conf["inputs"] = self.roles(); config.save_config(conf)
        cli = runpy.run_path(str(Path(__file__).resolve().parents[1] / "arcanum-local"))
        args = cli["parser"]().parse_args(["launch", "terrain", "--mode", "crash-site-area", "--dry-run"])
        with patch.object(launch, "start") as start:
            with self.assertRaisesRegex(ValueError, "ARC-RENDER-007"):
                cli["command_launch"](args)
            start.assert_not_called()
        # Selecting the task is accepted at the closed task-specific pointer.
        self.assertIn("ARC-RENDER-007", cli["parser"]().parse_args(
            ["configure", "select-launch", "--tool", "terrain", "--candidate", "root:ARC-RENDER-007:" + "a" * 40]).candidate)

    def test_input001_binds_literally_to_the_area_gate_set(self):
        # ARC-INPUT-001 (diagnostic palette) ships on the whole-core AREA build with
        # every ARC-RENDER-007 gate; nothing broader than the literal task is admitted.
        self.assertIn("ARC-INPUT-001", manifests.SUPPORTED_TOOLS["terrain"])
        self.assertEqual(grid_export_gates.gates_for("ARC-RENDER-007"), grid_export_gates.gates_for("ARC-INPUT-001"))
        self.assertEqual(retention.REQUIRED_TASK_SUCCESS_GATES["ARC-RENDER-007"], retention.REQUIRED_TASK_SUCCESS_GATES["ARC-INPUT-001"])
        for other in ("ARC-INPUT-002", "ARC-RENDER-008", "ARC-INPUT"):
            self.assertNotIn(other, manifests.SUPPORTED_TOOLS["terrain"])
            with self.assertRaises(ValueError):
                grid_export_gates.gates_for(other)
        tools = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools"
        self.assertIn("ARC-RENDER-007|ARC-INPUT-001|ARC-WORLD-007|ARC-WORLD-009) area_task=1; regression_task=ARC-WORLD-003", (tools / "validate-macos.sh").read_text())
        writer = runpy.run_path(str(tools / "terrain_write_manifest.py"), run_name="terrain_write_manifest")
        self.assertEqual("world-builds", writer["TASKS"]["ARC-INPUT-001"])
        self.assertIn("ARC-INPUT-001", writer["AREA_TASKS"])
        with tempfile.TemporaryDirectory(dir=str(self.root)) as evidence:
            with self.assertRaises(ValueError):
                grid_export_gates.validate_reports(Path(evidence), "a" * 40, "ARC-INPUT-001")

    def test_world007_binds_literally_to_the_area_gate_set(self):
        # ARC-WORLD-007 (LMB click-to-move) ships on the same whole-core AREA build; its route
        # matrix runs inside the area-grid probe, so the gate set is exactly ARC-RENDER-007's.
        self.assertIn("ARC-WORLD-007", manifests.SUPPORTED_TOOLS["terrain"])
        self.assertEqual(grid_export_gates.gates_for("ARC-RENDER-007"), grid_export_gates.gates_for("ARC-WORLD-007"))
        self.assertEqual(retention.REQUIRED_TASK_SUCCESS_GATES["ARC-RENDER-007"], retention.REQUIRED_TASK_SUCCESS_GATES["ARC-WORLD-007"])
        self.assertIn("ARC-WORLD-007", retention.AREA_TASKS)
        for other in ("ARC-WORLD-008", "ARC-WORLD-010", "ARC-WORLD"):
            self.assertNotIn(other, manifests.SUPPORTED_TOOLS["terrain"])
            with self.assertRaises(ValueError):
                grid_export_gates.gates_for(other)
        tools = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools"
        self.assertIn("ARC-RENDER-007|ARC-INPUT-001|ARC-WORLD-007|ARC-WORLD-009) area_task=1; regression_task=ARC-WORLD-003", (tools / "validate-macos.sh").read_text())
        writer = runpy.run_path(str(tools / "terrain_write_manifest.py"), run_name="terrain_write_manifest")
        self.assertEqual("world-builds", writer["TASKS"]["ARC-WORLD-007"])
        self.assertIn("ARC-WORLD-007", writer["AREA_TASKS"])
        with tempfile.TemporaryDirectory(dir=str(self.root)) as evidence:
            with self.assertRaises(ValueError):
                grid_export_gates.validate_reports(Path(evidence), "a" * 40, "ARC-WORLD-007")

    def test_world009_binds_literally_to_the_area_gate_set_with_the_cave(self):
        # ARC-WORLD-009 (cave and return) ships on the same whole-core AREA build; area_validate.py adds the
        # cave input and requires the transition smoke inside the area-original gate.
        self.assertIn("ARC-WORLD-009", manifests.SUPPORTED_TOOLS["terrain"])
        self.assertEqual(grid_export_gates.gates_for("ARC-RENDER-007"), grid_export_gates.gates_for("ARC-WORLD-009"))
        self.assertEqual(retention.REQUIRED_TASK_SUCCESS_GATES["ARC-RENDER-007"], retention.REQUIRED_TASK_SUCCESS_GATES["ARC-WORLD-009"])
        self.assertIn("ARC-WORLD-009", retention.AREA_TASKS)
        tools = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools"
        script = (tools / "validate-macos.sh").read_text()
        # Both hosts' area-original gates pass the literal task, so the cave input reaches them.
        self.assertEqual(1, script.count('area_validate.py" --candidate-root "$candidate_root" --task "$task"'))
        self.assertEqual(1, script.count('area_validate.py" --candidate-root "$candidate_root" --app "$app" --task "$task"'))
        writer = runpy.run_path(str(tools / "terrain_write_manifest.py"), run_name="terrain_write_manifest")
        self.assertEqual("world-builds", writer["TASKS"]["ARC-WORLD-009"])
        self.assertIn("ARC-WORLD-009", writer["AREA_TASKS"])
        with tempfile.TemporaryDirectory(dir=str(self.root)) as evidence:
            with self.assertRaises(ValueError):
                grid_export_gates.validate_reports(Path(evidence), "a" * 40, "ARC-WORLD-009")


if __name__ == "__main__":
    unittest.main()
