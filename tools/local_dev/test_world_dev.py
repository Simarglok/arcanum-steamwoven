"""WORLD-specific synthetic authority tests; no original payloads or exports."""
from __future__ import annotations

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


class WorldDevTests(unittest.TestCase):
    root: Path
    setUp = fixtures.LocalDevTests.setUp
    tearDown = fixtures.LocalDevTests.tearDown
    v2_manifest = fixtures.LocalDevTests.v2_manifest
    conf = fixtures.LocalDevTests.conf
    authority_snapshot = fixtures.LocalDevTests.authority_snapshot
    authority_fixture = fixtures.LocalDevTests.authority_fixture
    assert_authority_rejection = fixtures.LocalDevTests.assert_authority_rejection

    def roles(self):
        inputs = {"sourceId": "public-fixture"}
        for role in ("module", "terrainResource", "actorResource"):
            path = self.root / role
            path.write_bytes(b"public fixture")
            inputs[role] = str(path)
        prototypes = self.root / "prototypes"; prototypes.mkdir()
        inputs["prototypes"] = str(prototypes)
        return inputs

    def test_literal_world_argv_and_each_missing_or_unsafe_role(self):
        inputs = self.roles()
        argv = launch.terrain_arguments(inputs, "world-diagnostic")
        self.assertEqual(argv, ["--mode", "world-diagnostic", "--module", inputs["module"],
            "--resource", inputs["terrainResource"], "--source-id", "public-fixture",
            "--prototypes", inputs["prototypes"], "--actor-resource", inputs["actorResource"]])
        launch.assert_ordinary(argv)
        self.assertFalse(any(value.startswith("--actor-") and value != "--actor-resource" for value in argv))
        for role in inputs:
            with self.subTest(role=role), self.assertRaises(ValueError):
                launch.terrain_arguments({k:v for k,v in inputs.items() if k != role}, "world-diagnostic")
        for role in ("module", "terrainResource", "actorResource", "prototypes"):
            link = self.root / (role + "-link"); link.symlink_to(inputs[role])
            with self.subTest(role=role), self.assertRaises(ValueError):
                launch.terrain_arguments(inputs | {role: str(link)}, "world-diagnostic")
        with self.assertRaises(ValueError):
            launch.terrain_arguments(inputs | {"prototypes": inputs["module"]}, "world-diagnostic")
        with self.assertRaises(ValueError):
            launch.terrain_arguments(inputs, "world-diagnostic", {"actor_frame": "0"})

    def test_cli_old_task_rejects_world_before_launch_and_world_preserves_old_default(self):
        pointer = self.authority_fixture(task="ARC-DEV-001", tool="terrain")
        conf = config.load_config(); conf["inputs"] = self.roles(); config.save_config(conf)
        cli = runpy.run_path(str(Path(__file__).resolve().parents[1] / "arcanum-local"))
        args = cli["parser"]().parse_args(["launch", "terrain", "--mode", "world-diagnostic", "--dry-run"])
        with patch.object(launch, "start") as start:
            with self.assertRaisesRegex(ValueError, "ARC-WORLD-001"): cli["command_launch"](args)
            start.assert_not_called()
        pointer.unlink()
        self.authority_fixture(task="ARC-WORLD-001", tool="terrain")
        conf = config.load_config(); conf["inputs"] = self.roles_existing(conf); config.save_config(conf)
        for mode, expected in (([], "terrain-only"), (["--mode", "world-diagnostic"], "world-diagnostic")):
            args = cli["parser"]().parse_args(["launch", "terrain", "--dry-run", *mode])
            with patch.object(launch, "start", return_value={}) as start:
                cli["command_launch"](args)
                observed = start.call_args.args[1]
                self.assertEqual(observed[observed.index("--mode")+1], expected)
                launch.assert_ordinary(observed)

    def roles_existing(self, conf):
        return {"sourceId": "public-fixture", **{role:str(self.root / role) for role in
            ("module", "terrainResource", "actorResource", "prototypes")}}

    def test_separate_root_keeps_legacy_pointer_and_second_world_pointer_rejects(self):
        old = self.authority_fixture(task="ARC-DEV-001", tool="terrain")
        old_bytes = old.read_bytes()
        world = self.authority_fixture(task="ARC-WORLD-001", tool="terrain")
        separate = self.root / "world-builds"; separate.mkdir()
        world.parent.rename(separate / "ARC-WORLD-001")
        world = separate / "ARC-WORLD-001/current.json"
        conf = config.load_config(); conf["artifactRoots"]["world"] = str(separate)
        payload_path = separate / "ARC-WORLD-001" / ("a" * 40) / "manifest.json"
        payload = json.loads(payload_path.read_bytes()); payload["rootId"] = "world"
        payload["exportBudget"]["observation"]["candidateId"] = "world:ARC-WORLD-001:" + "a" * 40
        payload_path.write_text(json.dumps(payload))
        pointer = json.loads(world.read_bytes()); pointer["manifestSha256"] = manifests.sha256(payload_path)
        world.write_text(json.dumps(pointer))
        conf["launchSelection"] = {"terrain": "world:ARC-WORLD-001:" + "a" * 40}; config.save_config(conf)
        cli = runpy.run_path(str(Path(__file__).resolve().parents[1] / "arcanum-local"))
        self.assertEqual(cli["selected_root"](conf, config.default_registry(), "terrain"), separate)
        self.assertEqual(old.read_bytes(), old_bytes)
        second = world.with_name("current-v2.json"); second.write_bytes(world.read_bytes())
        with self.assertRaises(ValueError): cli["selected_root"](conf, config.default_registry(), "terrain")
        second.unlink(); second.symlink_to(self.root / "absent")
        with self.assertRaises(ValueError): cli["selected_root"](conf, config.default_registry(), "terrain")
        self.assertEqual(old.read_bytes(), old_bytes)

    def world_manifest(self):
        path, _ = self.v2_manifest(task="ARC-WORLD-001")
        payload = json.loads(path.read_bytes()); payload["logicalTool"] = "terrain"
        budget = payload["exportBudget"]
        budget["preflight"]["tool"] = "terrain"; budget["observation"]["tool"] = "terrain"
        for name, key in (("export-preflight.json", "preflight"), ("export-observation.json", "observation")):
            record = path.parent / "evidence" / name; record.write_text(json.dumps(budget[key]))
            entry = next(e for e in payload["evidence"] if e["relativePath"].endswith(name))
            entry.update(bytes=record.stat().st_size, sha256=manifests.sha256(record))
        payload["checks"] = [{"name":name,"outcome":"PASS"} for name in (
            "locked-build", "source-headless", "source-graphical", "export", "exported-headless",
            "exported-graphical", "codesign-deep-strict", "focused-tests", "source-world-original", "exported-world-original")]
        payload["delivery"] = {"originalInputModes":"PASS"}
        return path, payload

    def test_required_world_gates_missing_failed_unverified_never_register(self):
        path, payload = self.world_manifest()
        identity = "test:ARC-WORLD-001:" + "a" * 40
        def reject(changed):
            path.write_text(json.dumps(changed)); before = self.authority_snapshot()
            registry = config.default_registry()
            with self.assertRaises(ValueError):
                retention.register_export(registry, conf=self.conf(), tool="terrain", candidate_id=identity, manifest=path)
            self.assertEqual(registry, config.default_registry()); self.assertEqual(self.authority_snapshot(),before)
        for name in ("focused-tests", "source-world-original", "exported-world-original"):
            reject(payload | {"checks":[c for c in payload["checks"] if c["name"] != name]})
            for outcome in ("FAIL", "NOT_VERIFIED"):
                reject(payload | {"checks":[c | {"outcome":outcome} if c["name"] == name else c for c in payload["checks"]]})
        for status in ("FAIL", "NOT_VERIFIED", None): reject(payload | {"delivery":{"originalInputModes":status}})
        path.write_text(json.dumps(payload)); registry = config.default_registry()
        retention.register_export(registry, conf=self.conf(), tool="terrain", candidate_id=identity, manifest=path)
        self.assertEqual(registry["builds"][identity]["lifecycle"],"exported")

    def test_world_writer_closed_task_head_root_and_missing_gate_binding(self):
        script = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/terrain_write_manifest.py"
        main = runpy.run_path(str(script))["main"]
        owner = self.root / "owner"; candidate = self.root / "candidate"
        head = "a" * 40
        build = owner / "artifacts/world-builds/ARC-WORLD-001" / head
        app = build / "ArcanumTerrainViewer.app/Contents/MacOS/viewer"
        app.parent.mkdir(parents=True); app.write_bytes(b"synthetic executable")
        argv = [str(script), str(candidate), str(owner), str(build), head, str(app), "ARC-WORLD-001"]
        def git(_, *args): return head if args == ("rev-parse", "HEAD") else ""
        with patch.dict(os.environ, ARCANUM_LOCAL_ROOT_ID="world"), patch.dict(main.__globals__, git=git), patch.object(sys, "argv", argv):
            with self.assertRaisesRegex(SystemExit, "missing durable"): main()
            with patch.object(sys, "argv", argv[:-1] + ["ARC-UNKNOWN"]):
                with self.assertRaisesRegex(SystemExit, "Unsupported"): main()
            with patch.dict(main.__globals__, git=lambda *_: "b" * 40):
                with self.assertRaisesRegex(SystemExit, "current clean"): main()
            wrong = list(argv); wrong[3] = str(owner / "artifacts/builds/ARC-WORLD-001" / head)
            with patch.object(sys, "argv", wrong):
                with self.assertRaisesRegex(SystemExit, "exact candidate"): main()
            evidence = build / "evidence"; evidence.mkdir()
            for log in ("locked-build", "source-headless", "source-graphical", "export", "exported-headless", "exported-graphical", "codesign", "tests", "hostfxr"):
                (evidence / (log + ".log")).write_text("synthetic PASS\n")
            (evidence / "export-preflight.json").write_text(json.dumps({"schema":"arcanum-export-preflight-v1", "tool":"terrain", "candidateId":"world:ARC-WORLD-001:" + head, "candidateCommit":head}))
            for host in ("source", "exported"):
                for kind in ("original", "persistence"):
                    (evidence / f"{host}-world-{kind}.log").write_text(json.dumps({"schema":"arcanum-world-validation-v1", "candidateCommit":"b" * 40, "host":host, "kind":kind, "outcome":"PASS"}))
            with self.assertRaisesRegex(SystemExit, "does not bind"): main()
            self.assertFalse((build / "manifest.json").exists())

    def test_world_pointer_digest_task_and_head_tampering_fail_closed(self):
        pointer = self.authority_fixture(task="ARC-WORLD-001", tool="terrain")
        original = pointer.read_bytes(); payload = json.loads(original)
        for change in ({"manifestSha256":"b" * 64}, {"manifestRelativePath":"b" * 40 + "/manifest.json"}):
            pointer.write_text(json.dumps(payload | change))
            with self.assertRaises((ValueError, OSError)): manifests.resolve_current(self.root / "artifacts", "terrain")
        pointer.write_bytes(original)
        manifest = pointer.parent / payload["manifestRelativePath"]
        manifest_bytes = manifest.read_bytes(); record = json.loads(manifest_bytes)
        for change in ({"candidate":record["candidate"] | {"commit":"b" * 40}}, {"task":"ARC-UNKNOWN"}, {"rootId":"wrong"}):
            manifest.write_text(json.dumps(record | change))
            pointer.write_text(json.dumps(payload | {"manifestSha256":manifests.sha256(manifest)}))
            with self.assertRaises(ValueError): manifests.resolve_current(self.root / "artifacts", "terrain", root_id="test")
        manifest.write_bytes(manifest_bytes); pointer.write_bytes(original)
        self.assertEqual(manifests.resolve_current(self.root / "artifacts", "terrain")[1]["task"], "ARC-WORLD-001")

    def test_closed_shell_task_rejects_before_build_or_export(self):
        script = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/validate-macos.sh"
        result = subprocess.run(["/bin/sh",str(script)], env=dict(os.environ,ARCANUM_TERRAIN_EXPORT_TASK="ARC-UNKNOWN"),
            capture_output=True,text=True,timeout=5)
        self.assertEqual(result.returncode,2); self.assertIn("Unsupported terrain export task",result.stderr)


if __name__ == "__main__": unittest.main()
