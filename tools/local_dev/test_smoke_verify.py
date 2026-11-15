"""ARC-DEV-2223: `./arcanum smoke verify-pr` (tools/local_dev/smoke_verify.py): the stages `--auto` selects are a floor a merge
cannot go below, read from the queue journal."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import io
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

from tools.local_dev import smoke, smoke_history, smoke_verify, smoke_zones

KNOWN = ("map", "resources", "cast", "ai", "walk", "container-click", "census")
CHECKOUT = "mine"


def entry(seq: int, *asked: str, step: str = "stages-main", exit: int = 0, tree: str | None = "T1", checkout: str = CHECKOUT) -> dict:
    return {"seq": seq, "step": step, "checkout": checkout, "asked": list(asked), "exit": exit, "tree": tree}


def verify(files: list[str], entries: list[dict], tree: str | None = "T1", moved: dict | None = None, known=KNOWN):
    selection = smoke_zones.select_zones(files)
    moved = moved or {}
    return smoke_verify.verify(selection, files, known, entries, CHECKOUT, tree, lambda a, b: moved.get((a, b)))


def statuses(report: smoke_verify.Report) -> dict[str, str]:
    return {result.need.label(): result.status for result in report.results}


class NeedsTests(unittest.TestCase):
    def test_a_zone_requires_its_stages_and_core_is_every_cheap_stage(self) -> None:
        selection = smoke_zones.select_zones(["src/Arcanum.Simulation/X.cs"])
        self.assertEqual([need.name for need in smoke_verify.needs(selection, KNOWN)], ["map", "resources", "cast", "ai"])
        walk = smoke_zones.select_zones(["src/Arcanum.Simulation/Movement/X.cs"])
        self.assertIn(("stage", "walk"), {(need.kind, need.name) for need in smoke_verify.needs(walk, KNOWN)})

    def test_a_diff_no_smoke_covers_requires_nothing(self) -> None:
        self.assertEqual(smoke_verify.needs(smoke_zones.select_zones(["NOTES.md", "tests/X.cs"]), KNOWN), [])

    def test_complete_is_core_and_the_crash_site_smoke_at_a_pr(self) -> None:
        selection = smoke_zones.select_zones(["src/Arcanum.Data/X.cs"])
        found = {(need.kind, need.name) for need in smoke_verify.needs(selection, KNOWN)}
        self.assertEqual(found, {("stage", "map"), ("stage", "resources"), ("stage", "cast"), ("stage", "ai"),
                                 ("crash-site", "crash-site")})  # not walk, container-click or census: the rolling smoke's

    def test_click_or_walk_handling_adds_the_map_stage_and_the_self_test(self) -> None:
        selection = smoke_zones.select_zones(["src/Arcanum.World/AreaCellPicking.cs"])  # container-click zone only
        found = {(need.kind, need.name) for need in smoke_verify.needs(selection, KNOWN)}
        self.assertEqual(found, {("stage", name) for name in ("map", "resources", "cast", "ai", "container-click")}
                         | {("self-test", "self-test")})  # the file is also `core` code


class VerifyTests(unittest.TestCase):
    FILES = ["src/Arcanum.Simulation/X.cs"]
    ALL = [entry(1, "map", "resources", "cast", "ai")]

    def test_every_stage_run_on_these_files_passes(self) -> None:
        report = verify(self.FILES, self.ALL)
        self.assertTrue(report.ok)
        self.assertTrue(all(result.status == "ok" for result in report.results))

    def test_a_stage_with_no_run_is_missing(self) -> None:
        report = verify(self.FILES, [entry(1, "map", "resources", "cast")])
        self.assertFalse(report.ok)
        self.assertEqual(statuses(report), {"map": "ok", "resources": "ok", "cast": "ok", "ai": "missing"})

    def test_a_failed_run_or_another_checkout_does_not_count(self) -> None:
        report = verify(self.FILES, [entry(1, "map", "resources", "cast", "ai", exit=1),
                                     entry(2, "map", "resources", "cast", "ai", checkout="other")])
        self.assertEqual(set(statuses(report).values()), {"missing"})

    def test_a_stage_run_on_files_older_than_a_change_of_the_diff_is_stale(self) -> None:
        old = [entry(1, "map", "resources", "cast", "ai", tree="T0")]
        report = verify(self.FILES, old, moved={("T0", "T1"): {"src/Arcanum.Simulation/X.cs"}})
        self.assertFalse(report.ok)
        self.assertEqual(set(statuses(report).values()), {"stale"})
        self.assertIn("ran before a change to src/Arcanum.Simulation/X.cs", report.results[0].detail)

    def test_a_run_on_older_files_counts_when_no_file_of_the_diff_changed_since(self) -> None:
        old = [entry(1, "map", "resources", "cast", "ai", tree="T0")]
        report = verify(self.FILES, old, moved={("T0", "T1"): {"OTHER.md", "src/Arcanum.World/FromMain.cs"}})
        self.assertTrue(report.ok)  # main merged in, a document edited: the diff's files are as the run saw them

    def test_the_newest_passing_run_on_the_right_files_wins_over_an_older_stale_one(self) -> None:
        entries = [entry(1, "map", "resources", "cast", "ai", tree="T0"), entry(2, "map", "resources", "cast", "ai", tree="T1")]
        self.assertTrue(verify(self.FILES, entries, moved={("T0", "T1"): set(self.FILES)}).ok)

    def test_a_run_without_recorded_files_or_with_a_lost_tree_is_not_counted(self) -> None:
        report = verify(self.FILES, [entry(1, "map", "resources", "cast", "ai", tree=None)])
        self.assertEqual(set(statuses(report).values()), {"stale"})
        report = verify(self.FILES, [entry(1, "map", "resources", "cast", "ai", tree="T0")])  # git cannot compare: refused
        self.assertEqual(set(statuses(report).values()), {"stale"})
        self.assertFalse(verify(self.FILES, self.ALL, tree=None).ok)

    def test_a_path_no_row_names_requires_the_complete_floor(self) -> None:
        files = ["something/new.bin"]
        report = verify(files, [entry(1, "map", "resources", "cast", "ai")])
        self.assertFalse(report.ok)  # the Crash Site smoke is required for `complete` and has no run
        self.assertEqual(statuses(report)["crash-site smoke"], "missing")
        done = [entry(1, "map", "resources", "cast", "ai"), entry(2, step="crash-site")]
        self.assertTrue(verify(files, done).ok)
        self.assertTrue(smoke_zones.select_zones(files).unmapped)

    def test_complete_at_a_pr_needs_core_and_the_crash_site_but_not_the_heavy_stages(self) -> None:
        files = ["src/Arcanum.Data/X.cs"]
        entries = [entry(1, "map", "resources", "cast", "ai"), entry(2, step="crash-site")]
        report = verify(files, entries)
        self.assertTrue(report.ok)
        self.assertNotIn("walk", statuses(report))
        self.assertNotIn("census", statuses(report))
        self.assertFalse(verify(files, entries[:1]).ok)

    def test_a_click_or_walk_change_needs_the_self_test_and_the_map_stage_whatever_else_ran(self) -> None:
        files = ["src/Arcanum.World/AreaCellPicking.cs"]
        entries = [entry(1, "container-click", "resources", "cast", "ai", step="stages-town")]
        report = verify(files, entries)
        self.assertEqual({name: status for name, status in statuses(report).items() if status != "ok"},
                         {"map": "missing", "self-test": "missing"})
        full = entries + [entry(2, "map"), entry(3, step="self-test")]
        self.assertTrue(verify(files, full).ok)
        self.assertFalse(verify(files, full[:2] + [entry(3, step="self-test", exit=1)]).ok)

    def test_a_retried_stage_that_passed_alone_counts_beside_its_failed_group(self) -> None:
        entries = [entry(1, "map", "resources", "cast", exit=1), entry(2, "map"), entry(3, "resources"), entry(4, "cast"),
                   entry(5, "ai", step="stages-main-retry-ai")]
        self.assertTrue(verify(self.FILES, entries).ok)


class RunTests(unittest.TestCase):
    def go(self, files: list[str], entries: list[dict], tree: str = "T1") -> tuple[int, str]:
        output = io.StringIO()
        with mock.patch.object(smoke_zones, "changed_files", return_value=("abcdef0123", tuple(files))), \
                mock.patch.object(smoke_zones, "changed_lines", return_value=[]), \
                mock.patch.object(smoke, "map_stages", return_value=KNOWN), \
                mock.patch.object(smoke_history, "recent", return_value=entries), \
                mock.patch.object(smoke_history, "work_tree", return_value=tree), \
                mock.patch.object(smoke, "ROOT", Path("/x") / CHECKOUT), redirect_stdout(output):
            code = smoke.run(argparse.Namespace(target="verify-pr", base=None))
        return code, output.getvalue()

    def test_a_missing_stage_fails_with_exit_1_and_says_what_to_run(self) -> None:
        code, text = self.go(["src/Arcanum.Simulation/X.cs"], [entry(1, "map")])
        self.assertEqual(code, 1)
        self.assertIn("MISSING resources", text)
        self.assertIn("SMOKE verify-pr FAIL: 3 of 4 required units", text)
        self.assertIn("./arcanum smoke pr --auto", text)

    def test_a_covered_diff_passes_and_a_document_diff_requires_nothing(self) -> None:
        code, text = self.go(["src/Arcanum.Simulation/X.cs"], [entry(1, "map", "resources", "cast", "ai")])
        self.assertEqual(code, 0)
        self.assertIn("SMOKE verify-pr PASS: 4 required units", text)
        code, text = self.go(["NOTES.md"], [])
        self.assertEqual(code, 0)
        self.assertIn("nothing is required", text)

    def test_the_unmapped_paths_and_the_complete_floor_are_named(self) -> None:
        code, text = self.go(["new/thing.bin"], [])
        self.assertEqual(code, 1)
        self.assertIn("no zone row names, so `complete` is selected (fail closed): new/thing.bin", text)
        self.assertIn("a PR runs core + crash-site for `complete`", text)

    def test_the_command_is_in_the_launcher_help(self) -> None:
        parser = argparse.ArgumentParser()
        smoke.configure(parser)
        self.assertEqual(parser.parse_args(["verify-pr", "--base", "origin/dev"]).base, "origin/dev")


class WorkTreeTests(unittest.TestCase):
    """The recorded working-tree hash, in a real temporary repository."""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "user.email", "t@example.com")
        self.git("config", "user.name", "t")
        (self.root / "a.txt").write_text("a\n")
        (self.root / "b.txt").write_text("b\n")
        self.git("add", ".")
        self.git("commit", "-qm", "one")

    def tearDown(self) -> None:
        self.temp.cleanup()

    def git(self, *args: str) -> str:
        return subprocess.run(("git", "-C", str(self.root), *args), capture_output=True, text=True, check=True).stdout

    def tree(self) -> str | None:
        return smoke_history.work_tree(self.root)

    def test_the_hash_follows_the_working_files_and_ignores_how_they_are_committed(self) -> None:
        clean = self.tree()
        self.assertEqual(clean, self.git("rev-parse", "HEAD^{tree}").strip())
        (self.root / "a.txt").write_text("changed\n")
        (self.root / "new.txt").write_text("n\n")
        (self.root / "b.txt").unlink()
        dirty = self.tree()
        self.assertNotEqual(dirty, clean)
        self.assertEqual(self.git("status", "--porcelain").count("\n"), 3)  # the real index and files are untouched
        self.git("add", "-A")
        self.git("commit", "-qm", "two")
        self.assertEqual(self.tree(), dirty)  # committing the same files keeps the hash: a run before the commit still counts

    def test_the_differing_files_between_two_trees(self) -> None:
        clean = self.tree()
        (self.root / "a.txt").write_text("changed\n")
        self.assertEqual(smoke_verify.tree_differences(clean, self.tree() or "", self.root), {"a.txt"})
        self.assertIsNone(smoke_verify.tree_differences("0" * 40, clean, self.root))  # an object git does not have

    def test_a_folder_that_is_not_a_repository_gives_none(self) -> None:
        with tempfile.TemporaryDirectory() as other:
            self.assertIsNone(smoke_history.work_tree(Path(other)))


if __name__ == "__main__":
    unittest.main()
