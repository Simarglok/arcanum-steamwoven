"""ARC-DEV-028: protected versus disposable builds (docs/workflow/build-retention.md)."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from tools.local_dev import clean, config, retention, test_local_dev

TASK = "ARC-GPAD-003"


class CleanDevTests(unittest.TestCase):
    # The retention fixtures (a complete registered v2 candidate) are shared with test_local_dev.
    setUp = test_local_dev.LocalDevTests.setUp
    tearDown = test_local_dev.LocalDevTests.tearDown
    v2_manifest = test_local_dev.LocalDevTests.v2_manifest
    conf = test_local_dev.LocalDevTests.conf

    def candidate(self, letter: str, task: str = TASK) -> tuple[str, str]:
        _, digest = self.v2_manifest(task=task, name=letter * 40)
        return f"test:{task}:{letter * 40}", digest

    def registered(self, registry: dict, candidate_id: str, digest: str, lifecycle: str, at: str) -> None:
        _, task, commit = candidate_id.split(":")
        registry["builds"][candidate_id] = {"lifecycle": lifecycle, "tool": "gamepad", "rootId": "test", "task": task,
                                            "commit": commit, "manifestSha256": digest, "registeredAt": f"2026-10-{at}T00:00:00+00:00"}

    def point(self, candidate_id: str, digest: str) -> None:
        _, task, commit = candidate_id.split(":")
        (self.root / "artifacts" / task / "current.json").write_text(json.dumps({
            "schema": "arcanum-build-current-v2", "manifestRelativePath": commit + "/manifest.json", "manifestSha256": digest}),
            encoding="utf-8")

    def fixture(self) -> dict[str, str]:
        """a current, b superseded by a, c exported with no current in its task, d the measurement, e retired, f selected,
        g registered after the current a (its pointer never moved)."""
        registry = config.default_registry()
        ids = {}
        for letter, task, at in (("a", TASK, "10"), ("b", TASK, "01"), ("c", "ARC-GPAD-002", "01"), ("d", TASK, "01"),
                                 ("e", TASK, "01"), ("f", TASK, "01"), ("g", TASK, "11")):
            candidate_id, digest = self.candidate(letter, task)
            self.registered(registry, candidate_id, digest, "retired" if letter == "e" else "exported", at)
            ids[letter] = candidate_id
            if letter == "a":
                self.point(candidate_id, digest)
        registry["measurements"] = {"gamepad": {"candidateId": ids["d"], "temporaryLowWaterBytes": 0}}
        conf = self.conf(); conf["launchSelection"] = {"gamepad": ids["f"]}
        config.save_config(conf); config.save_registry(registry)
        return ids

    def test_classes_name_every_protection_and_only_superseded_or_retired_builds_are_disposable(self) -> None:
        ids = self.fixture()
        report = clean.builds(config.load_config(), config.load_registry())
        classes = {item["candidateId"]: (item["class"], item["action"]) for item in report}
        reasons = {item["candidateId"]: item["reason"] for item in report}
        self.assertEqual((reasons[ids["a"]], reasons[ids["d"]], reasons[ids["f"]]),
                         (f"current build of {TASK}", "export measurement of gamepad", "launch selection of gamepad"))
        self.assertEqual(classes, {f"test:{TASK}:current.json": ("protected", None),
            ids["a"]: ("protected", None), ids["b"]: ("disposable", "retire-and-prune"), ids["c"]: ("keep", None),
            ids["d"]: ("protected", None), ids["e"]: ("disposable", "prune"), ids["f"]: ("protected", None),
            ids["g"]: ("keep", None)})

    def test_the_measurement_build_is_protected_from_retire(self) -> None:
        ids = self.fixture()
        self.assertIn(ids["d"], retention.protection_snapshot(config.load_config(), config.load_registry())["protected"])
        registry = config.load_registry()
        with self.assertRaisesRegex(ValueError, "current/selected/rollback"):
            retention.retire(registry, ids["d"], conf=config.load_config())

    def test_apply_removes_only_disposable_payloads_and_keeps_manifests_and_evidence(self) -> None:
        ids = self.fixture()
        result = clean.apply(self.root)
        self.assertEqual(result["retired"], [ids["b"]])
        self.assertEqual(sorted(result["removed"]), sorted([ids["b"], ids["e"]]))
        for letter, task in (("a", TASK), ("b", TASK), ("c", "ARC-GPAD-002"), ("d", TASK), ("e", TASK), ("f", TASK), ("g", TASK)):
            candidate = self.root / "artifacts" / task / (letter * 40)
            self.assertEqual((candidate / "GamepadLab.app").exists(), letter not in "be", letter)
            self.assertTrue((candidate / "manifest.json").is_file()); self.assertTrue((candidate / "evidence/check.log").is_file())
        self.assertEqual(config.load_registry()["builds"][ids["b"]]["lifecycle"], "retired")
        after = {item["candidateId"]: item["class"] for item in clean.builds(config.load_config(), config.load_registry())}
        self.assertEqual((after[ids["b"]], after[ids["e"]]), ("pruned", "pruned"))
        self.assertEqual(clean.apply(self.root)["status"], "nothing-to-remove")

    def test_dry_run_cli_changes_nothing_and_writes_the_marker(self) -> None:
        self.fixture()
        registry_before = config.registry_path().read_bytes()
        cli = Path(__file__).resolve().parents[1] / "arcanum-local"
        result = subprocess.run([sys.executable, "-B", str(cli), "builds", "clean", "--checkout", str(self.root), "--json"],
                                capture_output=True, text=True, timeout=60, env=dict(os.environ))
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report["schema"], "arcanum-clean-report-v1")
        self.assertEqual(config.registry_path().read_bytes(), registry_before)
        self.assertTrue(all((self.root / "artifacts" / task / (letter * 40) / "GamepadLab.app").is_dir()
                            for letter, task in (("b", TASK), ("e", TASK))))
        self.assertEqual((self.root / "artifacts" / clean.MARKER).read_text(encoding="utf-8"), clean.MARKER_TEXT)

    def test_marker_needs_an_existing_folder(self) -> None:
        self.assertFalse(clean.write_marker(self.root / "missing"))
        self.assertFalse((self.root / "missing").exists())
        (self.root / "artifacts").mkdir()
        self.assertTrue(clean.write_marker(self.root / "artifacts")); self.assertTrue(clean.write_marker(self.root / "artifacts"))

    def test_unmanaged_entries_are_kept_and_owned_homes_are_named(self) -> None:
        checkout = self.root / "checkout"; artifacts = checkout / "artifacts"
        (artifacts / "world-builds" / "ARC-NOT-A-TASK").mkdir(parents=True)
        (artifacts / "world-builds" / "ARC-RENDER-008").mkdir()
        (artifacts / "builds" / "ARC-RENDER-005").mkdir(parents=True)
        (artifacts / "gamepad-builds").mkdir()
        home = checkout / "local" / "gpad005-pr-home"; home.mkdir(parents=True)
        (home / "config-v1.json").write_text(json.dumps({"artifactRoots": {"gpad005": str(artifacts / "gamepad-builds")}}), encoding="utf-8")
        conf = config.default_config(); conf["artifactRoots"] = {"world": str(artifacts / "world-builds")}
        found = {Path(item["path"]).relative_to(checkout).as_posix(): item for item in clean.unmanaged(conf, checkout)}
        self.assertEqual(sorted(found), ["artifacts/builds", "artifacts/gamepad-builds", "artifacts/world-builds/ARC-NOT-A-TASK"])
        self.assertTrue(all(item["class"] == "keep" for item in found.values()))
        self.assertIn("local/gpad005-pr-home", found["artifacts/gamepad-builds"]["reason"])

    def test_caches_are_build_outputs_next_to_a_project_only(self) -> None:
        checkout = self.root / "checkout"
        (checkout / "src/App/bin").mkdir(parents=True); (checkout / "src/App/App.csproj").write_text("<Project/>")
        (checkout / "tools/Viewer/.godot").mkdir(parents=True); (checkout / "tools/Viewer/project.godot").write_text("")
        (checkout / "tools/data/bin").mkdir(parents=True)
        (checkout / "toolkit/Game/obj").mkdir(parents=True); (checkout / "toolkit/Game/Game.csproj").write_text("<Project/>")
        found = sorted(Path(item["path"]).relative_to(checkout).as_posix() for item in clean.caches(checkout))
        self.assertEqual(found, ["src/App/bin", "tools/Viewer/.godot"])

    def test_worktrees_are_removable_only_when_merged_and_clean(self) -> None:
        checkout = self.root / "checkout"; checkout.mkdir()
        environment = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t")
        def git(*arguments: str, cwd: Path = checkout) -> None:
            subprocess.run(["git", *arguments], cwd=cwd, check=True, capture_output=True, env=environment)
        git("init", "-q", "-b", "main"); (checkout / "f").write_text("x"); git("add", "f"); git("commit", "-qm", "c")
        git("update-ref", "refs/remotes/origin/main", "HEAD")
        for name in ("merged", "dirty", "ahead"):
            git("worktree", "add", "-q", "-b", name, str(checkout / ".claude/worktrees" / name))
        git("worktree", "add", "-q", "-b", "hermes", str(checkout / ".worktrees/t_1"))
        (checkout / ".claude/worktrees/merged/bin").mkdir()
        (checkout / ".claude/worktrees/dirty/new").write_text("x")
        ahead = checkout / ".claude/worktrees/ahead"; (ahead / "g").write_text("y"); git("add", "g", cwd=ahead); git("commit", "-qm", "g", cwd=ahead)
        found = {Path(item["path"]).name: item["class"] for item in clean.worktrees(checkout)}
        self.assertEqual(found, {"merged": "removable", "dirty": "keep", "ahead": "keep", "t_1": "keep"})


if __name__ == "__main__":
    unittest.main()
