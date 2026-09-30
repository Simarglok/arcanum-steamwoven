from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
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

    def v2_manifest(self, *, root_id: str = "test", task: str = "ARC-GPAD-003", name: str = "candidate") -> tuple[Path, str]:
        candidate = self.root / "artifacts" / task / name
        executable = candidate / "GamepadLab.app" / "Contents" / "MacOS" / "GamepadLab"
        executable.parent.mkdir(parents=True); executable.write_bytes(b"fixture")
        path = candidate / "manifest.json"
        payload = {"schema": "arcanum-build-manifest-v2", "project": "arcanum-steamwoven", "logicalTool": "gamepad",
                   "rootId": root_id, "task": task, "candidate": {"commit": "a" * 40, "tree": "b" * 40},
                   "app": {"relativePath": "GamepadLab.app", "executableRelativePath": "GamepadLab.app/Contents/MacOS/GamepadLab"},
                   "payloadInventory": [{"relativePath": "GamepadLab.app/Contents/MacOS/GamepadLab", "bytes": 7}],
                   "evidence": [], "exportBudget": {"outputBytes": 7, "temporaryLowWaterBytes": 0}, "acceptance": "not-accepted"}
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
        _, digest = self.manifest(); bound, _ = self.v2_manifest()
        executable = bound.parent / "GamepadLab.app" / "Contents" / "MacOS" / "GamepadLab"
        current.parent.mkdir(parents=True, exist_ok=True)
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "candidate/manifest.json", "manifestSha256": manifests.sha256(bound)}), encoding="utf-8")
        found, payload, _ = manifests.resolve_current(root, "gamepad")
        self.assertEqual(found, bound.resolve()); self.assertEqual(manifests.manifest_candidate(payload), "a" * 40)
        self.assertEqual(Path(payload["app"]["executable"]), executable.resolve())
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "../../manifest.json", "manifestSha256": digest}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "escapes"):
            manifests.resolve_current(root, "gamepad")

    def test_v2_rejects_mixed_schema_and_incomplete_immutable_bindings(self) -> None:
        manifest, _ = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["schemaVersion"] = 1
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "unsupported"):
            manifests._manifest(manifest, "gamepad")
        payload.pop("schemaVersion"); payload.pop("payloadInventory")
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "immutable"):
            manifests._manifest(manifest, "gamepad")

    def test_v2_rejects_unbound_evidence_budget_and_configured_root(self) -> None:
        manifest, digest = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8"))
        payload["evidence"] = [{"relativePath": "../private.log", "bytes": 1}]
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "evidence"):
            manifests._manifest(manifest, "gamepad")
        payload["evidence"] = [{"relativePath": "evidence/check.log", "bytes": 1}]
        payload["exportBudget"] = {"outputBytes": 7}
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "budget"):
            manifests._manifest(manifest, "gamepad")
        payload["exportBudget"] = {"outputBytes": 7, "temporaryLowWaterBytes": 0}
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        root = self.root / "artifacts"; current = root / "ARC-GPAD-003" / "current-v2.json"
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "candidate/manifest.json", "manifestSha256": manifests.sha256(manifest)}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "root identity"):
            manifests.resolve_current(root, "gamepad", root_id="other")

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

    def test_exact_process_path_preserves_bundle_spaces(self) -> None:
        executable = self.root / "Bundle With Spaces.app" / "Contents" / "MacOS" / "Tool"
        executable.parent.mkdir(parents=True); executable.write_bytes(b"fixture")
        result = subprocess.CompletedProcess([], 0, stdout=f"123 {executable}\n124 /unrelated Tool\n", stderr="")
        with patch("tools.local_dev.launch.subprocess.run", return_value=result):
            self.assertTrue(launch.exact_executable_in_use(executable))

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
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest()
        candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:candidate"
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        self.assertEqual(len(plan["payloads"]), 1)
        result = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(result["status"], "applied")
        self.assertFalse((candidate / "GamepadLab.app").exists())
        self.assertTrue((candidate / "manifest.json").exists())
        self.assertTrue((candidate / "payload-pruned-v1.json").is_file())
        self.assertEqual(retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])["removed"], [candidate_id])

    def test_apply_finalizes_crash_intent_and_rejects_pointer_drift(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest()
        candidate = manifest.parent; candidate_id = "test:ARC-GPAD-003:candidate"
        current = candidate.parent / "current-v2.json"
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "candidate/manifest.json", "manifestSha256": digest}), encoding="utf-8")
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        current.write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pointer changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "candidate/manifest.json", "manifestSha256": digest}), encoding="utf-8")
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        item = plan["payloads"][0]
        config.write_atomic(candidate / "prune-intent-v1.json", {"schema": "prune-intent-v1", "id": plan["id"], "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"], "manifestSha256": item["manifestSha256"]})
        shutil.rmtree(candidate / item["payloadRelativePath"])
        recovered = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(recovered["removed"], [candidate_id])
        self.assertTrue((candidate / "payload-pruned-v1.json").is_file())

    def test_apply_rejects_pointer_created_after_plan_and_tampered_recovery_manifest(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest(); candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:candidate"
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True)); item = plan["payloads"][0]
        (candidate.parent / "current-v2.json").write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pointer changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        (candidate.parent / "current-v2.json").unlink()
        config.write_atomic(candidate / "prune-intent-v1.json", {"schema": "prune-intent-v1", "id": plan["id"], "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"], "manifestSha256": item["manifestSha256"]})
        shutil.rmtree(candidate / item["payloadRelativePath"])
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["candidate"]["tree"] = "c" * 40; manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "manifest changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])

    def test_inventory_keeps_intermediate_payload_symlink_and_unsafe_candidate(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest(); candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:candidate"
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        moved = candidate / "actual"; (candidate / "GamepadLab.app").rename(moved)
        (candidate / "GamepadLab.app").symlink_to("actual", target_is_directory=True)
        entries = retention.inventory(conf, registry)
        self.assertEqual(entries[0]["reason"], "KEEP/SKIP: candidate is unsafe or unreadable")
        self.assertFalse(entries[0]["eligible"])
        unsafe = root / "ARC-GPAD-003" / "linked"; unsafe.symlink_to(candidate, target_is_directory=True)
        entries = retention.inventory(conf, registry)
        self.assertTrue(any(entry["candidate"] == "linked" and "KEEP/SKIP" in entry["reason"] for entry in entries))

    def test_accept_rotates_rollback_and_protects_it_from_retire(self) -> None:
        old, old_digest = self.v2_manifest(name="old"); new, new_digest = self.v2_manifest(name="new")
        old_id, new_id = "test:ARC-GPAD-003:old", "test:ARC-GPAD-003:new"
        registry = config.default_registry(); registry["builds"] = {
            old_id: {"lifecycle": "exported", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": old_digest},
            new_id: {"lifecycle": "exported", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": new_digest}}
        retention.accept(registry, tool="gamepad", candidate_id=old_id, manifest=old, authority="owner", review="review-1")
        retention.accept(registry, tool="gamepad", candidate_id=new_id, manifest=new, authority="owner", review="review-2")
        self.assertEqual(registry["accepted"]["gamepad"], new_id); self.assertEqual(registry["rollback"]["gamepad"], old_id)
        with self.assertRaisesRegex(ValueError, "rollback"):
            retention.retire(registry, old_id)

    def test_preflight_requires_measurement_and_reserves_two_gib(self) -> None:
        with self.assertRaisesRegex(ValueError, "trusted"):
            retention.export_preflight({}, "terrain", None, None)
        budget = retention.export_preflight({}, "terrain", 100, None)
        self.assertEqual(budget["reserveBytes"], 2 * 1024**3)
        self.assertEqual(budget["temporaryBytes"], 200)

    def test_export_registration_requires_passing_existing_manifest(self) -> None:
        manifest, _ = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["checks"] = [{"name": name, "outcome": "PASS"} for name in retention.REQUIRED_SUCCESS_GATES["gamepad"]]
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        registry = config.default_registry()
        candidate_id = "test:ARC-GPAD-003:candidate"
        retention.register_export(registry, tool="gamepad", candidate_id=candidate_id, manifest=manifest)
        self.assertEqual(registry["builds"][candidate_id]["lifecycle"], "exported")
        payload["checks"] = [{"name": "codesign-deep-strict", "outcome": "FAIL"}]; manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pass"):
            retention.register_export(registry, tool="gamepad", candidate_id=candidate_id, manifest=manifest)


if __name__ == "__main__":
    unittest.main()
