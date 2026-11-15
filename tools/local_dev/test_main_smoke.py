"""ARC-DEV-2226: ./arcanum main-smoke runs the core smoke on a fresh worktree of origin/main when main moved, files or updates
one issue and writes the red-on-main flag on red, clears it on green, and `smoke pr` prints the flag; fake runner and fake gh,
synthetic git repo."""
from __future__ import annotations

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import plistlib
import subprocess
import tempfile
import unittest
from unittest import mock

from tools.local_dev import daily, main_smoke, package, red_flag, run_queue, smoke

SMOKE_GREEN = """SMOKE step build kind=dotnet
PASS exit=0 ran=20s lines=10 log=/q/run-1-dotnet.log
SMOKE summary
  build       PASS exit=0
  crash-site  PASS exit=0
  stages-main PASS exit=0
SMOKE PASS 3 of 3 steps passed
"""
SMOKE_RED = """SMOKE step stages-main kind=window
FAIL exit=1 ran=60s lines=700 log=/q/run-4-window.log
first error at line 650 of 700 (1 error line(s)):
  ARC smoke FAIL stage=walk
SMOKE step stages-main-retry-walk kind=window
FAIL exit=1 ran=30s lines=70 log=/q/run-5-window.log
SMOKE summary
  build                   PASS exit=0
  stages-main             FAIL exit=1
  stages-main-retry-walk  FAIL exit=1
  stages-main-retry-cast  PASS exit=0
SMOKE FAIL 2 of 4 steps passed
"""
SMOKE_ORDER = """SMOKE summary
  build        PASS exit=0
  stages-main  ORDER exit=1
  order-dependent: stages-main (fails only after other stages of its process; each of cast, ranged passes alone)
SMOKE PASS 2 of 2 steps passed (1 order-dependent, each stage passes alone)
"""
NOW = datetime(2026, 11, 15, 12, 0, tzinfo=timezone.utc)


class PureTests(unittest.TestCase):
    def test_classify(self) -> None:
        self.assertEqual(main_smoke.classify(daily.parse_smoke(SMOKE_GREEN, 0), 0)[0], "green")
        self.assertEqual(main_smoke.classify(daily.parse_smoke(SMOKE_ORDER, 0), 0)[0], "green")
        self.assertEqual(main_smoke.classify(daily.parse_smoke(SMOKE_RED, 1), 1)[0], "red")
        # a stopped run, the time limit and a missing summary prove nothing about main
        self.assertEqual(main_smoke.classify(daily.parse_smoke(SMOKE_GREEN, 130), 130)[0], "inconclusive")
        self.assertEqual(main_smoke.classify(daily.parse_smoke(SMOKE_RED, 124), 124)[0], "inconclusive")
        self.assertEqual(main_smoke.classify(daily.parse_smoke("Traceback ...\n", 1), 1)[0], "inconclusive")

    def test_a_failed_viewer_build_is_red(self) -> None:
        text = "SMOKE summary\n  build  FAIL exit=1\n  not run: import, crash-site\n"
        self.assertEqual(main_smoke.classify(daily.parse_smoke(text, 1), 1)[0], "red")

    def test_failing_stages_name_the_stage_that_fails_alone_and_not_its_process(self) -> None:
        self.assertEqual(main_smoke.failing_stages(daily.parse_smoke(SMOKE_RED, 1)), ["walk"])
        text = "SMOKE summary\n  crash-site  FAIL exit=1\n  town  FAIL exit=1\n"
        self.assertEqual(main_smoke.failing_stages(daily.parse_smoke(text, 1)), ["crash-site", "town"])

    def test_flake_records_log_alone_reruns(self) -> None:
        records = main_smoke.flake_records(daily.parse_smoke(SMOKE_RED, 1), "a" * 40, "T")
        self.assertEqual([(r["stage"], r["outcome"]) for r in records], [("walk", "fails-alone")])
        records = main_smoke.flake_records(daily.parse_smoke(SMOKE_ORDER, 0), "a" * 40, "T")
        self.assertEqual([(r["stage"], r["outcome"]) for r in records], [("cast", "passes-alone"), ("ranged", "passes-alone")])
        text = "SMOKE summary\n  crash-site  FAIL exit=1\n"
        self.assertEqual(main_smoke.flake_records(daily.parse_smoke(text, 1), "a" * 40, "T")[0]["outcome"], "failed")

    def test_area_label_by_the_first_failing_stage(self) -> None:
        self.assertEqual(main_smoke.area_label(["menu-death"]), "area:ui-media-lifecycle")
        self.assertEqual(main_smoke.area_label(["cast"]), "area:combat-magic-tech")
        self.assertEqual(main_smoke.area_label(["barter", "walk"]), "area:items-economy")
        self.assertEqual(main_smoke.area_label(["something-new"]), main_smoke.DEFAULT_AREA)
        self.assertEqual(main_smoke.area_label([]), main_smoke.DEFAULT_AREA)


class FlagTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.folder = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)

    def test_write_read_clear_and_text(self) -> None:
        self.assertIsNone(red_flag.read(self.folder))
        self.assertFalse(red_flag.clear(self.folder))
        red_flag.write({"firstRed": "a" * 40, "checked": "b" * 40, "stages": ["walk", "town"], "issue": 77}, self.folder)
        flag = red_flag.read(self.folder)
        self.assertEqual(flag["issue"], 77)
        shown = red_flag.text(flag)
        self.assertIn("red on main since aaaaaaaaaa, #77", shown)
        self.assertIn("failing: walk, town", shown)
        self.assertTrue(red_flag.clear(self.folder))
        self.assertIsNone(red_flag.read(self.folder))

    def test_an_unreadable_flag_is_no_flag(self) -> None:
        (self.folder / red_flag.FLAG).write_text("{not json")
        self.assertIsNone(red_flag.read(self.folder))
        (self.folder / red_flag.FLAG).write_text(json.dumps({"schema": "other", "firstRed": "x"}))
        self.assertIsNone(red_flag.read(self.folder))

    def test_a_flag_without_an_issue_says_so(self) -> None:
        self.assertIn("issue not filed yet", red_flag.text({"firstRed": "a" * 40, "stages": []}))

    def test_smoke_pr_prints_the_flag_and_other_targets_do_not(self) -> None:
        red_flag.write({"firstRed": "a" * 40, "checked": "b" * 40, "stages": ["walk"], "issue": 5}, self.folder)
        with mock.patch.object(red_flag, "directory", return_value=self.folder):
            import argparse
            self.assertIn("#5", smoke.red_on_main_note(argparse.Namespace(target="pr")) or "")
            self.assertIsNone(smoke.red_on_main_note(argparse.Namespace(target="complete")))
            printed = []
            with mock.patch("builtins.print", side_effect=lambda *a, **k: printed.append(a[0])), \
                    mock.patch.object(smoke, "run_smoke", return_value=1):
                self.assertEqual(smoke.run(argparse.Namespace(target="pr")), 1)
            self.assertEqual(len(printed), 2)  # at the start and again after the failure
            red_flag.clear(self.folder)
            printed.clear()
            with mock.patch("builtins.print", side_effect=lambda *a, **k: printed.append(a[0])), \
                    mock.patch.object(smoke, "run_smoke", return_value=1):
                smoke.run(argparse.Namespace(target="pr"))
            self.assertEqual(printed, [])


class QueueTests(unittest.TestCase):
    def test_low_priority_goes_behind_ordinary_runs(self) -> None:
        low = {"seq": 1, "kind": "window", "priority": run_queue.LOW, "enqueuedAt": 1.0}
        normal = {"seq": 2, "kind": "window", "priority": run_queue.NORMAL, "enqueuedAt": 99999.0}
        front = {"seq": 3, "kind": "window", "priority": run_queue.FRONT, "enqueuedAt": 99999.0}
        self.assertEqual([t["seq"] for t in sorted([low, normal, front], key=run_queue._key)], [3, 2, 1])

    def test_the_low_priority_option_passes_the_priority_to_the_queue(self) -> None:
        seen = []

        def fake(kind, command, **options):
            seen.append(options.get("priority"))
            return 0
        steps = [smoke.Step("one", "quick", ("true",))]
        with mock.patch.object(run_queue, "run", fake), mock.patch.object(smoke, "load_known_failures", return_value=()), \
                mock.patch.object(smoke.smoke_history, "append"):
            import argparse
            args = argparse.Namespace(target="pr", low_priority=True, no_build=True, keep_settings=False, dry_run=False)
            with mock.patch.object(smoke, "plan", return_value=steps), mock.patch.object(smoke, "focus_guard_target", return_value=None), \
                    mock.patch.object(smoke.config, "load_config", return_value={}), mock.patch.object(smoke.os, "access", return_value=True), \
                    mock.patch.object(smoke.os, "chdir"), mock.patch("builtins.print"):
                smoke.run_smoke(args)
                args.low_priority = False
                smoke.run_smoke(args)
        self.assertEqual(seen, [run_queue.LOW, None])


class RunTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.tmp = self.root / "tmp"
        self.tmp.mkdir()
        self.folder = self.root / "main-smoke"
        self.addCleanup(self.temporary.cleanup)
        patched = mock.patch.object(main_smoke.tempfile, "gettempdir", return_value=str(self.tmp)); patched.start(); self.addCleanup(patched.stop)
        quiet = mock.patch.object(main_smoke, "say"); quiet.start(); self.addCleanup(quiet.stop)
        self.env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t")
        self.git("init", "-q", "-b", "main")
        self.commits = [self.commit("one")]
        self.calls: list[tuple[tuple[str, ...], Path, float | None]] = []
        self.text, self.code = SMOKE_GREEN, 0
        self.gh_calls: list[tuple[tuple[str, ...], str | None]] = []
        self.open_issues: list[dict] = []
        self.gh_fails = False

    def git(self, *arguments: str) -> str:
        return subprocess.run(["git", *arguments], cwd=self.repo, check=True, capture_output=True, env=self.env, text=True).stdout.strip()

    def commit(self, name: str) -> str:
        (self.repo / "tracked.txt").write_text(name + "\n")
        self.git("add", "tracked.txt"); self.git("commit", "-qm", f"change {name}")
        self.git("update-ref", "refs/remotes/origin/main", "HEAD")
        return self.git("rev-parse", "HEAD")

    def runner(self, command, cwd, environment, log, minutes) -> int:
        self.calls.append((tuple(command), cwd, minutes))
        self.assertTrue((cwd / "tracked.txt").is_file(), "the smoke runs in the fresh checkout of the commit")
        log.write_text(self.text)
        return self.code

    def gh(self, arguments, stdin) -> str:
        self.gh_calls.append((tuple(arguments), stdin))
        if self.gh_fails:
            raise main_smoke.GhError("no network")
        if arguments[:2] == ["issue", "view"]:
            number = int(arguments[2])
            return json.dumps({"state": "OPEN" if any(i["number"] == number for i in self.open_issues) else "CLOSED"})
        if arguments[:2] == ["issue", "list"]:
            return json.dumps(self.open_issues)
        if arguments[:2] == ["issue", "create"]:
            self.open_issues.append({"number": 900, "title": arguments[arguments.index("--title") + 1]})
            return "https://github.com/x/y/issues/900"
        return ""

    def run_it(self, **options) -> int:
        settings = {"fetch": False, "force": False, "keep_work": False, "dry_run": False, "issues": True, "zones": main_smoke.ZONES}
        settings.update(options)
        return main_smoke.main_smoke(root=self.repo, folder=self.folder, runner=self.runner, gh=self.gh, environ={"PATH": "/usr/bin"},
                                     now=lambda: NOW, **settings)

    def state(self) -> dict:
        return main_smoke.read_state(self.folder)

    def commands(self, verb: str) -> list[tuple[tuple[str, ...], str | None]]:
        return [call for call in self.gh_calls if call[0][:2] == ("issue", verb)]

    def test_green_runs_the_core_smoke_at_low_priority_in_a_fresh_worktree_and_records_it(self) -> None:
        self.assertEqual(self.run_it(), main_smoke.PASS)
        command, cwd, minutes = self.calls[0]
        self.assertEqual(command, ("./arcanum", "smoke", "pr", "--zones", "core,crash-site", "--retry", "--low-priority"))
        self.assertEqual(minutes, main_smoke.SMOKE_MINUTES)
        self.assertNotEqual(cwd, self.repo)
        self.assertFalse(cwd.exists(), "the worktree is removed")
        self.assertEqual(self.state()["lastGreen"]["sha"], self.commits[0])
        self.assertIsNone(red_flag.read(self.folder))
        self.assertEqual(self.gh_calls, [])
        self.assertIn("RESULT GREEN", (self.folder / "logs" / "report.txt").read_text())

    def test_no_change_since_the_last_run_is_skipped_and_force_runs_it_again(self) -> None:
        self.run_it()
        self.assertEqual(self.run_it(), main_smoke.PASS)
        self.assertEqual(len(self.calls), 1)
        self.run_it(force=True)
        self.assertEqual(len(self.calls), 2)
        self.commits.append(self.commit("two"))
        self.run_it()
        self.assertEqual(len(self.calls), 3)

    def test_red_files_one_issue_with_the_range_writes_the_flag_and_does_not_rerun_the_same_commit(self) -> None:
        self.run_it()
        green = self.commits[0]
        self.commits += [self.commit("two"), self.commit("three")]
        self.text, self.code = SMOKE_RED, 1
        self.assertEqual(self.run_it(), main_smoke.RED)
        create = self.commands("create")
        self.assertEqual(len(create), 1)
        arguments, body = create[0]
        labels = arguments[arguments.index("--label") + 1].split(",")
        self.assertEqual(labels[:3], ["bug", "type:dev", "status:ready"])
        self.assertIn("area:world-travel-runtime", labels)  # walk
        self.assertIn(f"`{self.commits[2]}`", body)
        self.assertIn("change three", body)
        self.assertIn("change two", body)
        self.assertNotIn("change one", body)  # the green commit itself is not in the range
        self.assertIn("2 first-parent commit(s)", body)
        self.assertIn("walk", body)
        flag = red_flag.read(self.folder)
        self.assertEqual((flag["firstRed"], flag["issue"], flag["stages"], flag["lastGreen"]), (self.commits[2], 900, ["walk"], green))
        self.assertIn("red on main since", red_flag.text(flag))
        self.assertEqual(self.run_it(), main_smoke.PASS, "the same red commit is not run again")
        self.assertEqual(len(self.calls), 2)
        flakes = [json.loads(line) for line in (self.folder / main_smoke.FLAKES).read_text().splitlines()]
        self.assertEqual([(f["stage"], f["outcome"]) for f in flakes], [("walk", "fails-alone")])

    def test_a_later_red_updates_the_same_issue_and_keeps_the_first_red_commit(self) -> None:
        self.text, self.code = SMOKE_RED, 1
        self.run_it()
        first = self.commits[0]
        self.assertEqual(self.state()["red"]["lastGreen"], None)
        self.assertIn("range is unknown", self.commands("create")[0][1])
        self.commits.append(self.commit("two"))
        self.run_it()
        self.assertEqual(len(self.commands("create")), 1)
        edit = self.commands("edit")
        self.assertEqual(len(edit), 1)
        self.assertEqual(edit[0][0][2], "900")
        flag = red_flag.read(self.folder)
        self.assertEqual((flag["firstRed"], flag["checked"]), (first, self.commits[1]))

    def test_an_issue_that_was_closed_meanwhile_is_not_edited_an_open_one_with_the_title_is_found(self) -> None:
        self.open_issues = [{"number": 55, "title": main_smoke.TITLE + ": old"}]
        self.text, self.code = SMOKE_RED, 1
        self.run_it()
        self.assertEqual(len(self.commands("create")), 0)
        self.assertEqual(self.commands("edit")[0][0][2], "55")
        self.assertEqual(red_flag.read(self.folder)["issue"], 55)

    def test_green_after_red_clears_the_flag_comments_and_resets_the_streak(self) -> None:
        self.text, self.code = SMOKE_RED, 1
        self.run_it()
        self.commits.append(self.commit("two"))
        self.text, self.code = SMOKE_GREEN, 0
        self.assertEqual(self.run_it(), main_smoke.PASS)
        self.assertIsNone(red_flag.read(self.folder))
        self.assertIsNone(self.state()["red"])
        self.assertEqual(self.state()["lastGreen"]["sha"], self.commits[1])
        comment = self.commands("comment")
        self.assertEqual(comment[0][0][2], "900")
        self.assertIn("green again", comment[0][1])

    def test_an_inconclusive_run_leaves_the_flag_and_the_issue(self) -> None:
        self.text, self.code = SMOKE_RED, 1
        self.run_it()
        flag = red_flag.read(self.folder)
        self.commits.append(self.commit("two"))
        self.text, self.code = SMOKE_GREEN, 130
        self.assertEqual(self.run_it(), main_smoke.INCONCLUSIVE)
        self.assertEqual(red_flag.read(self.folder), flag)
        self.assertEqual(self.state()["lastRun"]["result"], "inconclusive")
        self.assertEqual(len(self.commands("edit")), 0)
        self.assertEqual(self.run_it(), main_smoke.INCONCLUSIVE, "an inconclusive commit is tried again")
        self.assertEqual(len(self.calls), 3)

    def test_a_gh_failure_still_writes_the_flag_without_an_issue_and_the_next_red_run_files_it(self) -> None:
        self.text, self.code = SMOKE_RED, 1
        self.gh_fails = True
        self.assertEqual(self.run_it(), main_smoke.RED)
        self.assertIsNone(red_flag.read(self.folder)["issue"])
        self.gh_fails = False
        self.commits.append(self.commit("two"))
        self.run_it()
        self.assertEqual(red_flag.read(self.folder)["issue"], 900)

    def test_no_issue_never_calls_gh(self) -> None:
        self.text, self.code = SMOKE_RED, 1
        self.run_it(issues=False)
        self.assertEqual(self.gh_calls, [])
        self.assertIsNone(red_flag.read(self.folder)["issue"])

    def test_dry_run_runs_nothing_and_inside_the_queue_is_refused(self) -> None:
        self.assertEqual(self.run_it(dry_run=True), main_smoke.PASS)
        self.assertEqual(self.calls, [])
        self.assertFalse(self.folder.exists())
        with self.assertRaises(package.PackageError):
            main_smoke.main_smoke(root=self.repo, folder=self.folder, fetch=False, force=False, keep_work=False, dry_run=True, issues=True,
                                  zones="core", environ={run_queue.TICKET_ENV: "3"})

    def test_a_stopped_worktree_step_is_inconclusive(self) -> None:
        def stopping(*_arguments):
            raise package.PackageError("stopped by signal 15")
        self.runner = stopping  # type: ignore[method-assign]
        self.assertEqual(self.run_it(), main_smoke.INCONCLUSIVE)
        self.assertEqual(self.state()["lastRun"]["result"], "inconclusive")
        self.assertEqual(len(list(self.tmp.iterdir())), 0, "the temporary folder is removed")


class LaunchdTests(unittest.TestCase):
    def test_the_agent_runs_every_two_hours_from_a_fresh_worktree(self) -> None:
        data = plistlib.loads(main_smoke.launchd_plist(Path("/repo with space"), {"PATH": "/usr/bin"}))
        self.assertEqual(data["Label"], main_smoke.LABEL)
        self.assertEqual(data["StartInterval"], 7200)
        self.assertFalse(data["RunAtLoad"])
        script = data["ProgramArguments"][2]
        self.assertIn("'/repo with space'", script)
        self.assertIn("./arcanum main-smoke", script)
        self.assertIn("worktree add --detach", script)
        self.assertEqual(data["EnvironmentVariables"]["PATH"], "/usr/bin")


if __name__ == "__main__":
    unittest.main()
