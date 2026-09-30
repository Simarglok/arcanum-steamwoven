from __future__ import annotations

import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch

from tools.local_dev import config, launch, manifests, retention


class LocalDevTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.previous = os.environ.get("ARCANUM_LOCAL_HOME")
        os.environ["ARCANUM_LOCAL_HOME"] = str(self.root / "local")

    def tearDown(self) -> None:
        if self.previous is None:
            os.environ.pop("ARCANUM_LOCAL_HOME", None)
        else:
            os.environ["ARCANUM_LOCAL_HOME"] = self.previous
        self.temporary.cleanup()

    def manifest(self, task: str = "ARC-GPAD-003") -> tuple[Path, str]:
        path = self.root / "manifest.json"
        payload = {"schemaVersion": 1, "task": task, "candidate": {"commit": "a" * 40},
                   "app": {"executable": "/bin/true", "engineArgs": ["--rendering-driver", "gl_compatibility"], "ordinaryArgs": ["--ordinary"]}}
        path.write_text(json.dumps(payload), encoding="utf-8")
        return path, manifests.sha256(path)

    def test_atomic_local_files_are_owner_only(self) -> None:
        value = config.default_config()
        config.save_config(value)
        self.assertEqual(stat.S_IMODE(config.config_path().stat().st_mode), 0o600)
        self.assertEqual(config.load_config()["schema"], config.CONFIG_SCHEMA)

    def test_argv_has_literal_separator_and_spaces_unicode(self) -> None:
        executable, argv = launch.argv_from_manifest({"app": {"executable": "/bin/true", "engineArgs": ["--title", "Привет мир"], "ordinaryArgs": ["--safe-name", "two words"]}})
        self.assertEqual(executable, Path("/bin/true"))
        self.assertEqual(argv, ["--title", "Привет мир", "--", "--safe-name", "two words"])

    def test_forbidden_automation_and_no_implicit_export(self) -> None:
        with self.assertRaisesRegex(ValueError, "ordinary launch"):
            launch.assert_ordinary(["--self-test"])
        self.assertNotIn("export", launch.argv_from_manifest({"app": {"executable": "/bin/true"}})[1])

    def test_terrain_args_use_explicit_inputs_and_actor_defaults(self) -> None:
        inputs = {}
        for role in ("module", "terrainResource", "actorResource", "prototypes"):
            path = self.root / f"{role} input"
            path.write_bytes(b"fixture")
            inputs[role] = str(path)
        inputs["sourceId"] = "synthetic-local"
        combined = launch.terrain_arguments(inputs, "combined")
        self.assertEqual(combined[:2], ["--mode", "combined"])
        self.assertIn("--prototypes", combined)
        actor = launch.terrain_arguments(inputs, "actor-diagnostic")
        self.assertIn("--actor-resource", actor)
        self.assertEqual(actor[actor.index("--actor-render-branch") + 1], "normal-uncached")
        self.assertEqual(actor[actor.index("--actor-scale-percent") + 1], "100")
        self.assertNotIn("--self-test", actor)
        _, base = launch.argv_from_manifest({"app": {"executable": "/bin/true", "engineArgs": ["--rendering-driver", "gl_compatibility"], "ordinaryArgs": ["--ignored"]}})
        argv = [*base[:base.index("--") + 1], *actor]
        self.assertEqual(argv[2], "--")
        self.assertEqual(argv[3:5], ["--mode", "actor-diagnostic"])
        self.assertNotIn("--ignored", argv)

    def test_actor_preview_overrides_are_bounded_and_action_frame_aware(self) -> None:
        inputs = {}
        for role in ("module", "terrainResource", "actorResource", "prototypes"):
            path = self.root / role
            path.write_bytes(b"fixture")
            inputs[role] = str(path)
        inputs["sourceId"] = "synthetic"
        actor = launch.terrain_arguments(inputs, "actor-diagnostic", {"actor_action": "walk", "actor_facing": 7, "actor_frame": 7})
        self.assertEqual(actor[actor.index("--actor-action") + 1], "walk")
        self.assertEqual(actor[actor.index("--actor-facing") + 1], "7")
        with self.assertRaisesRegex(ValueError, "invalid for the chosen action"):
            launch.terrain_arguments(inputs, "actor-diagnostic", {"actor_action": "walk", "actor_frame": 8})
        with self.assertRaisesRegex(ValueError, "fixed normal-uncached"):
            launch.terrain_arguments(inputs, "actor-diagnostic", {"actor_scale_percent": 101})

    def test_terrain_args_fail_closed_for_missing_role_or_symlink(self) -> None:
        with self.assertRaisesRegex(ValueError, "explicit terrain"):
            launch.terrain_arguments({}, "terrain-only")
        target = self.root / "target"; target.write_bytes(b"fixture")
        linked = self.root / "linked"; linked.symlink_to(target)
        inputs = {"module": str(linked), "terrainResource": str(target), "actorResource": str(target), "prototypes": str(target), "sourceId": "fixture"}
        with self.assertRaisesRegex(ValueError, "unavailable or unsafe"):
            launch.terrain_arguments(inputs, "terrain-only")

    def test_v2_pointer_requires_manifest_hash_and_containment(self) -> None:
        root = self.root / "artifacts"; current = root / "ARC-GPAD-003" / "current-v2.json"
        _, digest = self.manifest(); bound = root / "ARC-GPAD-003" / "candidate" / "manifest.json"
        executable = bound.parent / "GamepadLab.app" / "Contents" / "MacOS" / "GamepadLab"
        executable.parent.mkdir(parents=True); executable.write_bytes(b"fixture")
        bound.write_text(json.dumps({"schemaVersion": 1, "task": "ARC-GPAD-003", "candidate": {"commit": "a" * 40},
                                     "app": {"executableRelativePath": "GamepadLab.app/Contents/MacOS/GamepadLab", "engineArgs": [], "ordinaryArgs": []}}), encoding="utf-8")
        current.parent.mkdir(parents=True, exist_ok=True)
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "candidate/manifest.json", "manifestSha256": manifests.sha256(bound)}), encoding="utf-8")
        found, payload, _ = manifests.resolve_current(root, "gamepad")
        self.assertEqual(found, bound.resolve()); self.assertEqual(manifests.manifest_candidate(payload), "a" * 40)
        self.assertEqual(Path(payload["app"]["executable"]), executable.resolve())
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "../../manifest.json", "manifestSha256": digest}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "escapes"):
            manifests.resolve_current(root, "gamepad")

    def test_legacy_pointer_schemas_are_tool_specific(self) -> None:
        root = self.root / "artifacts"
        app = root / "ARC-UI-001" / "candidate" / "Display.app" / "Contents" / "MacOS" / "Display"
        app.parent.mkdir(parents=True); app.write_bytes(b"fixture")
        manifest = app.parents[3] / "manifest.json"
        manifest.write_text(json.dumps({"schemaVersion": 1, "task": "ARC-UI-001", "candidate": {"commit": "b" * 40},
                                        "app": {"executableRelativePath": "Display.app/Contents/MacOS/Display"}}), encoding="utf-8")
        pointer = root / "ARC-UI-001" / "current.json"
        pointer.write_text(json.dumps({"schemaVersion": 1, "manifest": str(manifest)}), encoding="utf-8")
        _, payload, _ = manifests.resolve_current(root, "display")
        self.assertEqual(Path(payload["app"]["executable"]), app.resolve())
        pointer.write_text(json.dumps({"schemaVersion": 1, "manifestRelativePath": "candidate/manifest.json"}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "absolute"):
            manifests.resolve_current(root, "display")

    def test_missing_mixed_and_stale_pointer_fail_closed(self) -> None:
        root = self.root / "root"; root.mkdir()
        with self.assertRaisesRegex(ValueError, "exactly one"):
            manifests.resolve_current(root, "gamepad")
        for task in ("ARC-GPAD-001", "ARC-GPAD-003"):
            pointer = root / task / "current.json"; pointer.parent.mkdir(parents=True)
            pointer.write_text(json.dumps({"schemaVersion": 1, "manifest": "/missing"}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            manifests.resolve_current(root, "gamepad")

    def test_duplicate_guard_is_exact_and_opt_in(self) -> None:
        with patch("tools.local_dev.launch.exact_executable_in_use", return_value=True):
            with self.assertRaisesRegex(ValueError, "already in use"):
                launch.start(Path("/usr/bin/true"), ["--", "--ordinary"], allow_duplicate=False, lock_root=self.root, tool="gamepad", dry_run=True)
            result = launch.start(Path("/usr/bin/true"), ["--", "--ordinary"], allow_duplicate=True, lock_root=self.root, tool="gamepad", dry_run=True)
        self.assertFalse(result["started"])

    def test_inventory_keeps_active_open_review_and_unknown(self) -> None:
        artifact = self.root / "artifacts" / "ARC-GPAD-003"
        for name in ("accepted", "review", "unknown"):
            (artifact / name).mkdir(parents=True); (artifact / name / "payload").write_bytes(b"x")
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(self.root / "artifacts")}
        registry = config.default_registry(); registry["accepted"] = {"gamepad": "test:ARC-GPAD-003:accepted"}; registry["builds"] = {
            "test:ARC-GPAD-003:accepted": {"lifecycle": "accepted"}, "test:ARC-GPAD-003:review": {"lifecycle": "open-review"}}
        entries = retention.inventory(conf, registry)
        self.assertTrue(all(not entry["eligible"] for entry in entries))
        self.assertTrue(any("unknown" in entry["reason"] for entry in entries))

    def test_plan_binds_generations_and_apply_is_noop_safe(self) -> None:
        conf, registry = config.default_config(), config.default_registry()
        plan = retention.create_plan(conf, registry, [])
        result = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(result["status"], "no-op-safe")
        conf["generation"] += 1
        with self.assertRaisesRegex(ValueError, "changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])

    def test_retired_registered_app_is_pruned_with_idempotent_receipt(self) -> None:
        root = self.root / "artifacts"; candidate = root / "ARC-GPAD-003" / "candidate"
        app = candidate / "GamepadLab.app" / "Contents" / "MacOS"
        app.mkdir(parents=True); (app / "GamepadLab").write_bytes(b"fixture")
        manifest = candidate / "manifest.json"
        manifest.write_text(json.dumps({"schemaVersion": 1, "task": "ARC-GPAD-003", "app": {"relativePath": "GamepadLab.app"}}), encoding="utf-8")
        candidate_id = "test:ARC-GPAD-003:candidate"
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired"}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        self.assertEqual(len(plan["payloads"]), 1)
        result = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(result["status"], "applied")
        self.assertFalse((candidate / "GamepadLab.app").exists())
        self.assertTrue((candidate / "manifest.json").exists())
        self.assertTrue((candidate / "payload-pruned-v1.json").is_file())
        self.assertEqual(retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])["removed"], [candidate_id])

    def test_accept_rotates_rollback_and_protects_it_from_retire(self) -> None:
        manifest, _ = self.manifest()
        registry = config.default_registry(); retention.accept(registry, tool="gamepad", candidate_id="old", manifest=manifest, authority="owner", review="review-1")
        retention.accept(registry, tool="gamepad", candidate_id="new", manifest=manifest, authority="owner", review="review-2")
        self.assertEqual(registry["accepted"]["gamepad"], "new"); self.assertEqual(registry["rollback"]["gamepad"], "old")
        with self.assertRaisesRegex(ValueError, "rollback"):
            retention.retire(registry, "old")

    def test_preflight_requires_measurement_and_reserves_two_gib(self) -> None:
        with self.assertRaisesRegex(ValueError, "trusted"):
            retention.export_preflight({}, "terrain", None, None)
        budget = retention.export_preflight({}, "terrain", 100, None)
        self.assertEqual(budget["reserveBytes"], 2 * 1024**3)
        self.assertEqual(budget["temporaryBytes"], 200)

    def test_export_registration_requires_passing_existing_manifest(self) -> None:
        manifest, _ = self.manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["checks"] = [{"name": "codesign", "outcome": "PASS"}]
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        registry = config.default_registry()
        retention.register_export(registry, tool="gamepad", candidate_id="candidate", manifest=manifest)
        self.assertEqual(registry["builds"]["candidate"]["lifecycle"], "exported")
        payload["checks"] = [{"name": "codesign", "outcome": "FAIL"}]; manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pass"):
            retention.register_export(registry, tool="gamepad", candidate_id="bad", manifest=manifest)


if __name__ == "__main__":
    unittest.main()
