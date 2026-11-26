"""ARC-DEV-028: protected versus disposable builds (docs/workflow/build-retention.md)."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

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

    def test_a_current_build_that_does_not_verify_supersedes_nothing(self) -> None:
        ids = self.fixture()
        (self.root / "artifacts" / TASK / ("a" * 40) / "GamepadLab.app/Contents/MacOS/GamepadLab").write_bytes(b"changed")
        found = {item["candidateId"]: item for item in clean.builds(config.load_config(), config.load_registry())}
        self.assertEqual(found[ids["b"]]["class"], "keep"); self.assertIn("does not verify", found[ids["b"]]["reason"])
        self.assertEqual(clean.apply(self.root)["retired"], [])
        self.assertTrue((self.root / "artifacts" / TASK / ("b" * 40) / "GamepadLab.app").is_dir())

    def test_a_running_viewer_refuses_apply_before_anything_changes(self) -> None:
        self.fixture()
        registry_before = config.registry_path().read_bytes()
        lease = retention._try_exclusive(config.home() / "locks" / "gamepad.lock")
        try:
            with self.assertRaisesRegex(ValueError, "viewer is running"):
                clean.apply(self.root)
        finally:
            retention._release(lease)
        self.assertEqual(config.registry_path().read_bytes(), registry_before)
        self.assertTrue((self.root / "artifacts" / TASK / ("e" * 40) / "GamepadLab.app").is_dir())

    def test_pruned_builds_do_not_count_as_surviving_payloads(self) -> None:
        ids = self.fixture()
        clean.apply(self.root)
        survivors = retention.surviving_payloads(config.load_config(), "gamepad")
        self.assertNotIn(ids["b"], survivors); self.assertNotIn(ids["e"], survivors); self.assertIn(ids["a"], survivors)

    def test_a_malformed_measurement_blocks_its_tool(self) -> None:
        self.fixture()
        registry = config.load_registry(); registry["measurements"] = {"gamepad": "not-a-record"}
        self.assertIn(["test", "gamepad"], retention.protection_snapshot(config.load_config(), registry)["blockedDomains"])
        self.assertTrue(all(item["class"] != "disposable" for item in clean.builds(config.load_config(), registry)))

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
        marker = self.root / "artifacts" / clean.MARKER; marker.unlink()
        other = self.root / "other"; other.write_text("mine"); os.link(other, marker)
        self.assertFalse(clean.write_marker(self.root / "artifacts")); self.assertEqual(other.read_text(), "mine")

    def test_unmanaged_entries_are_kept_and_owned_homes_are_named(self) -> None:
        checkout = self.root / "checkout"; artifacts = checkout / "artifacts"
        (artifacts / "world-builds" / "ARC-NOT-A-TASK").mkdir(parents=True)
        (artifacts / "world-builds" / "ARC-RENDER-008").mkdir()
        (artifacts / "builds" / "ARC-RENDER-005").mkdir(parents=True)
        (artifacts / "gamepad-builds").mkdir()
        (artifacts / "packages" / "macos").mkdir(parents=True)  # classified by clean.packages, not here
        home = checkout / "local" / "gpad005-pr-home"; home.mkdir(parents=True)
        (home / "config-v1.json").write_text(json.dumps({"artifactRoots": {"gpad005": str(artifacts / "gamepad-builds")}}), encoding="utf-8")
        conf = config.default_config(); conf["artifactRoots"] = {"world": str(artifacts / "world-builds")}
        found = {Path(item["path"]).relative_to(checkout).as_posix(): item for item in clean.unmanaged(conf, checkout)}
        self.assertEqual(sorted(found), ["artifacts/builds", "artifacts/gamepad-builds", "artifacts/world-builds/ARC-NOT-A-TASK"])
        self.assertTrue(all(item["class"] == "keep" for item in found.values()))
        self.assertIn(str(home.resolve()), found["artifacts/gamepad-builds"]["reason"])
        (artifacts / "elsewhere").mkdir(); (artifacts / "packages").rename(artifacts / "elsewhere" / "packages")
        (artifacts / "packages").symlink_to(artifacts / "elsewhere" / "packages")
        linked = {Path(item["path"]).name: item for item in clean.unmanaged(conf, checkout)}
        self.assertEqual((linked["packages"]["class"], linked["packages"]["reason"]), ("keep", clean.PACKAGES_REASON))
        self.assertEqual(clean.packages(checkout), [])

    def package_fixture(self) -> Path:
        """macOS: current app, previous/, an interrupted .incoming; Windows: only previous/ (no current zip); logs, a stranger."""
        checkout = self.root / "checkout"; base = checkout / "artifacts" / "packages"
        mac, win = base / "macos", base / "windows"
        for folder in (mac / "Arcanum Steamwoven.app/Contents/MacOS", mac / "previous/Arcanum Steamwoven.app/Contents/MacOS",
                       mac / ".incoming-Arcanum Steamwoven.app", win / "previous", base / "logs/macos", base / "mine"):
            folder.mkdir(parents=True)
        (mac / "Arcanum Steamwoven.app/Contents/MacOS/Arcanum Steamwoven").write_bytes(b"n" * 300)
        (mac / "previous/Arcanum Steamwoven.app/Contents/MacOS/Arcanum Steamwoven").write_bytes(b"o" * 200)
        (mac / ".incoming-Arcanum Steamwoven.app/part").write_bytes(b"p" * 50)
        (win / "previous/Arcanum Steamwoven Windows x64.zip").write_bytes(b"z" * 100)
        for folder, commit in ((mac, "1" * 40), (mac / "previous", "2" * 40), (win / "previous", "3" * 40)):
            (folder / "BUILD.txt").write_text(f"commit {commit}\ndate 2026-10-28\nproduct x\nsmoke PASS\n", encoding="utf-8")
            (folder / "README.md").write_text("readme", encoding="utf-8")
        (mac / "notes.txt").write_text("mine")
        return checkout

    def test_packages_keep_the_current_build_and_offer_only_superseded_ones(self) -> None:
        checkout = self.package_fixture(); base = checkout / "artifacts" / "packages"
        found = {Path(item["path"]).relative_to(base).as_posix(): item for item in clean.packages(checkout)}
        self.assertEqual({path: item["class"] for path, item in found.items()}, {
            "logs": "keep", "mine": "keep", "macos/Arcanum Steamwoven.app": "keep", "macos/previous": "disposable",
            "macos/.incoming-Arcanum Steamwoven.app": "disposable", "macos/notes.txt": "keep", "windows/previous": "keep"})
        self.assertIn("main 1111111111, 2026-10-28", found["macos/Arcanum Steamwoven.app"]["reason"])
        self.assertIn("main 2222222222", found["macos/previous"]["reason"])
        self.assertIn("no current package", found["windows/previous"]["reason"])
        self.assertEqual(found["macos/previous"]["bytes"], 200 + len("commit ") + 40 + len("\ndate 2026-10-28\nproduct x\nsmoke PASS\n") + 6)
        self.assertIn("player packages", clean.render({"home": "h", "builds": [], "unmanaged": [], "packages": list(found.values()),
                                                       "worktrees": [], "caches": []}))

    def test_the_profiling_package_folder_is_classified_like_a_package(self) -> None:
        """ARC-DEV-3379: ./arcanum package mac --profiling installs into packages/macos-profiling."""
        checkout = self.package_fixture(); folder = checkout / "artifacts/packages/macos-profiling"
        for app in (folder / "Arcanum Steamwoven Profiling.app", folder / "previous/Arcanum Steamwoven Profiling.app"):
            (app / "Contents/MacOS").mkdir(parents=True)
        found = {Path(item["path"]).relative_to(folder).as_posix(): item for item in clean.packages(checkout)
                 if folder in Path(item["path"]).parents}
        self.assertEqual({path: item["class"] for path, item in found.items()},
                         {"Arcanum Steamwoven Profiling.app": "keep", "previous": "disposable"})
        self.assertIn("current macOS profiling package", found["Arcanum Steamwoven Profiling.app"]["reason"])
        with mock.patch.object(clean, "_processes", return_value=[]):
            self.assertIn(str(folder / "previous"), clean.apply_packages(checkout)["removed"])
        self.assertTrue((folder / "Arcanum Steamwoven Profiling.app").is_dir())

    def test_apply_packages_removes_only_superseded_ones_and_respects_running_processes(self) -> None:
        checkout = self.package_fixture(); mac = checkout / "artifacts/packages/macos"
        with mock.patch.object(clean, "_processes", return_value=["python3 -B /x/tools/local_dev/package.py mac --ref origin/main"]):
            self.assertTrue(clean.apply_packages(checkout)["status"].startswith("stopped: ./arcanum package is running"))
        self.assertTrue((mac / "previous").is_dir())
        running = [f"{mac}/previous/Arcanum Steamwoven.app/Contents/MacOS/Arcanum Steamwoven", "python3 -m unittest test_package.py"]
        with mock.patch.object(clean, "_processes", return_value=running):
            result = clean.apply_packages(checkout)
        self.assertEqual((result["status"], result["removed"]), ("skipped running previous", [str(mac / ".incoming-Arcanum Steamwoven.app")]))
        self.assertTrue((mac / "previous").is_dir())
        with mock.patch.object(clean, "_processes", return_value=[]):
            result = clean.apply_packages(checkout)
            self.assertEqual((result["status"], result["removed"]), ("done", [str(mac / "previous")]))
            self.assertEqual(clean.apply_packages(checkout)["status"], "nothing-to-remove")
        self.assertTrue((mac / "Arcanum Steamwoven.app/Contents/MacOS/Arcanum Steamwoven").is_file())
        self.assertTrue((mac / "BUILD.txt").is_file()); self.assertTrue((mac / "notes.txt").is_file())
        self.assertTrue((checkout / "artifacts/packages/windows/previous/Arcanum Steamwoven Windows x64.zip").is_file())
        self.assertTrue((checkout / "artifacts/packages/logs/macos").is_dir())

    def test_previous_with_bug_reports_or_odd_entries_is_kept_and_a_windows_incoming_file_goes(self) -> None:
        checkout = self.package_fixture(); base = checkout / "artifacts/packages"; mac, win = base / "macos", base / "windows"
        (mac / "previous/reports/2026-10-28").mkdir(parents=True)
        (win / "Arcanum Steamwoven Windows x64.zip").write_bytes(b"w" * 10); (win / "BUILD.txt").write_text("garbled")
        (win / ".incoming-Arcanum Steamwoven Windows x64.zip").write_bytes(b"i" * 7)
        (base / "stray.txt").write_bytes(b"s" * 5)
        found = {Path(item["path"]).relative_to(base).as_posix(): item for item in clean.packages(checkout)}
        self.assertEqual(found["macos/previous"]["class"], "keep"); self.assertIn("reports", found["macos/previous"]["reason"])
        self.assertIn("main ?", found["windows/Arcanum Steamwoven Windows x64.zip"]["reason"])
        self.assertEqual((found["windows/previous"]["class"], found["windows/.incoming-Arcanum Steamwoven Windows x64.zip"]["bytes"],
                          found["stray.txt"]["bytes"]), ("disposable", 7, 5))
        with mock.patch.object(clean, "_processes", side_effect=OSError("no ps")):
            self.assertTrue(clean.apply_packages(checkout)["status"].startswith("stopped: cannot list processes"))
        with mock.patch.object(clean, "_processes", return_value=["python3 -m tools.local_dev.package win"]):
            self.assertTrue(clean.apply_packages(checkout)["status"].startswith("stopped: ./arcanum package is running"))
        with mock.patch.object(clean, "_processes", return_value=[]):
            result = clean.apply_packages(checkout)
        self.assertEqual(sorted(Path(path).relative_to(base).as_posix() for path in result["removed"]),
                         ["macos/.incoming-Arcanum Steamwoven.app", "windows/.incoming-Arcanum Steamwoven Windows x64.zip", "windows/previous"])
        self.assertTrue((mac / "previous/reports/2026-10-28").is_dir()); self.assertTrue((win / "Arcanum Steamwoven Windows x64.zip").is_file())

    def test_links_are_never_followed_or_removed(self) -> None:
        checkout = self.package_fixture(); mac = checkout / "artifacts/packages/macos"
        (mac / "previous").rename(self.root / "outside"); (mac / "previous").symlink_to(self.root / "outside")
        found = {Path(item["path"]).name: item["class"] for item in clean.packages(checkout)}
        self.assertEqual(found["previous"], "keep")
        moved = self.root / "real-artifacts"; (checkout / "artifacts").rename(moved); (checkout / "artifacts").symlink_to(moved)
        self.assertEqual(clean.packages(checkout), [])
        with mock.patch.object(clean, "_processes", return_value=[]):
            self.assertEqual(clean.apply_packages(checkout)["status"], "nothing-to-remove")
        self.assertTrue((self.root / "outside/Arcanum Steamwoven.app").is_dir())
        self.assertTrue((moved / "packages/macos/.incoming-Arcanum Steamwoven.app").is_dir())

    def test_apply_cli_reports_packages_and_stops_with_exit_2(self) -> None:
        checkout = self.package_fixture()
        with mock.patch.object(clean, "_processes", return_value=["/x/tools/local_dev/package.py mac"]), \
             mock.patch("sys.stdout", new_callable=__import__("io").StringIO) as output:
            self.assertEqual(clean.run(checkout, do_apply=True, as_json=True), 2)
        result = json.loads(output.getvalue())
        self.assertEqual((result["status"], result["packages"]["removed"]), ("nothing-to-remove", []))
        self.assertTrue((checkout / "artifacts/packages/macos/previous").is_dir())

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
        git("init", "-q", "-b", "main"); (checkout / "f").write_text("x"); (checkout / ".gitignore").write_text("/.claude/settings.json\n*.tmp\n")
        git("add", "f", ".gitignore"); git("commit", "-qm", "c")
        git("update-ref", "refs/remotes/origin/main", "HEAD")
        for name in ("merged", "dirty", "ahead", "stray"):
            git("worktree", "add", "-q", "-b", name, str(checkout / ".claude/worktrees" / name))
        git("worktree", "add", "-q", "-b", "hermes", str(checkout / ".worktrees/t_1"))
        (checkout / ".claude/worktrees/merged/bin").mkdir()
        (checkout / ".claude/worktrees/merged/.claude").mkdir(); (checkout / ".claude/worktrees/merged/.claude/settings.json").write_text("{}")
        (checkout / ".claude/worktrees/stray/other.tmp").write_text("x")
        (checkout / ".claude/worktrees/dirty/new").write_text("x")
        ahead = checkout / ".claude/worktrees/ahead"; (ahead / "g").write_text("y"); git("add", "g", cwd=ahead); git("commit", "-qm", "g", cwd=ahead)
        found = {Path(item["path"]).name: item["class"] for item in clean.worktrees(checkout)}
        self.assertEqual(found, {"merged": "removable", "dirty": "keep", "ahead": "keep", "stray": "keep", "t_1": "keep"})


if __name__ == "__main__":
    unittest.main()
