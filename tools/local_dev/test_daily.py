"""ARC-DEV-062: ./arcanum daily skips an already packaged main, runs the root checks, the smoke chain and both packages from
a fresh worktree of origin/main, and makes no package when anything failed; fake runner, synthetic git repo."""
from __future__ import annotations

from contextlib import redirect_stdout
from datetime import datetime, timezone
import io
import os
from pathlib import Path
import runpy
import subprocess
import tempfile
import unittest
from unittest import mock

from tools.local_dev import daily, package, run_queue

REPO = Path(__file__).resolve().parents[2]
SMOKE_PASS = """SMOKE settings folder /tmp/arcanum-smoke-x
SMOKE step build kind=dotnet
PASS exit=0 ran=20s lines=10 log=/q/run-1-dotnet.log
SMOKE step crash-site kind=window
PASS exit=0 ran=94s lines=900 log=/q/run-2-window.log
SMOKE summary
  build       PASS exit=0
  crash-site  PASS exit=0
  main        PASS exit=0
  town        PASS exit=0
SMOKE PASS 4 of 4 steps passed
"""
SMOKE_FAIL = """SMOKE step build kind=dotnet
PASS exit=0 ran=20s lines=10 log=/q/run-1-dotnet.log
SMOKE step town kind=window
ARCANUM_QUEUE start ticket=4 kind=window waited=0s
FAIL exit=1 ran=60s lines=700 log=/q/run-4-window.log
first error at line 650 of 700 (1 error line(s)):
  ARC-RENDER smoke FAIL stage=Upload code=RenderFailure
SMOKE step ruins kind=window
FAIL exit=125 ran=60s lines=3 log=/q/run-5-window.log
SMOKE summary
  build       PASS exit=0
  crash-site  PASS exit=0
  main        PASS exit=0
  town        FAIL exit=1
  ruins       FAIL exit=125
SMOKE FAIL 3 of 5 steps passed
SMOKE settings folder kept: /tmp/arcanum-smoke-x
"""
SMOKE_BUILD_FAIL = """SMOKE summary
  build  FAIL exit=1
  not run: import, crash-site, main
SMOKE FAIL 0 of 4 steps passed
"""


class SmokeSummaryTests(unittest.TestCase):
    def test_a_passing_chain_names_its_smokes(self) -> None:
        result = daily.parse_smoke(SMOKE_PASS, 0)
        self.assertEqual((result.passed, result.failed, result.not_run), (["build", "crash-site", "main", "town"], [], []))
        self.assertIsNone(result.failure)
        self.assertEqual(result.line(), "PASS (crash-site, main, town)")

    def test_failed_smokes_block_and_keep_each_steps_run_log_and_first_error(self) -> None:
        result = daily.parse_smoke(SMOKE_FAIL, 1)
        self.assertEqual(result.failed, ["town", "ruins"])
        self.assertEqual(result.failure, "smoke failed: town, ruins")
        self.assertEqual(result.details["town"], ["FAIL exit=1 ran=60s lines=700 log=/q/run-4-window.log",
                                                  "first error at line 650 of 700 (1 error line(s)):",
                                                  "  ARC-RENDER smoke FAIL stage=Upload code=RenderFailure"])
        self.assertEqual(result.details["ruins"], ["FAIL exit=125 ran=60s lines=3 log=/q/run-5-window.log"])

    def test_a_failed_viewer_build_a_stopped_chain_or_no_summary_blocks(self) -> None:
        result = daily.parse_smoke(SMOKE_BUILD_FAIL, 1)
        self.assertEqual(result.not_run, ["import", "crash-site", "main"])
        self.assertIn("viewer build failed", result.failure or "")
        self.assertIn("stopped", daily.parse_smoke(SMOKE_PASS, 130).failure or "")
        self.assertIn("no summary", daily.parse_smoke("Traceback ...\n", 1).failure or "")
        self.assertIn("smoke failed: exit 1", daily.parse_smoke(SMOKE_PASS, 1).failure or "")


class FailureLinesTests(unittest.TestCase):
    def test_the_headers_stay_and_only_the_context_is_cut(self) -> None:
        context = [f"  context {number}" for number in range(40)]
        lines = ["noise", "FAIL exit=1 ran=60s lines=900 log=/q/run-4-window.log",
                 "first error at line 10 of 900 (2 error line(s)):", *context,
                 "last error (line 890): ARC smoke FAIL stage=Upload code=RenderFailure"]
        shown = daily.failure_lines(lines)
        self.assertEqual(shown[:2], lines[1:3])
        self.assertEqual(shown[2:2 + daily.CONTEXT_LINES], context[:daily.CONTEXT_LINES])
        self.assertEqual(shown[-1], lines[-1])
        self.assertEqual(len(shown), 3 + daily.CONTEXT_LINES)

    def test_a_plain_tail_shows_its_end_and_output_without_a_summary_its_last_lines(self) -> None:
        tail = [f"line {number}" for number in range(40)]
        shown = daily.failure_lines(["FAIL exit=1 ran=1s lines=40 log=/q/x.log", "no error line found; the last 40 lines:", *tail])
        self.assertEqual(shown[2:], tail[-daily.CONTEXT_LINES:])
        self.assertEqual(daily.failure_lines(["a", "", *tail]), tail[-daily.CONTEXT_LINES:])


class DailyTests(unittest.TestCase):
    def setUp(self) -> None:
        patched = mock.patch.object(daily.run_queue, "playing_state", return_value=None); patched.start(); self.addCleanup(patched.stop)  # ARC-DEV-2902: never the live owner-playing state (game process, real queue folder)
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.tmp = self.root / "tmp"
        self.tmp.mkdir()
        patched = mock.patch.object(daily.tempfile, "gettempdir", return_value=str(self.tmp)); patched.start(); self.addCleanup(patched.stop)
        environment = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t",
                           GIT_COMMITTER_DATE="2026-10-29T12:00:00Z", GIT_AUTHOR_DATE="2026-10-29T12:00:00Z")

        def git(*arguments: str) -> None:
            subprocess.run(["git", *arguments], cwd=self.repo, check=True, capture_output=True, env=environment)
        git("init", "-q", "-b", "main")
        (self.repo / "tracked.txt").write_text("x\n")
        git("add", "tracked.txt"); git("commit", "-qm", "c")
        git("update-ref", "refs/remotes/origin/main", "HEAD")
        self.commit = subprocess.check_output(["git", "-C", str(self.repo), "rev-parse", "HEAD"], text=True).strip()
        self.output = self.repo / "artifacts" / "packages"
        reports = self.output / "macos" / "reports"
        reports.mkdir(parents=True)
        (reports / "bug-1.txt").write_text("player report\n")
        self.environ = {"HOME": str(self.root), "PATH": "/usr/bin:/bin", "DAT_ROOT": "/game", "ARC_HD_PACK": "/hd",
                        "ARCANUM_EXE": "/exe", "ARCANUM_LOCAL_HOME": "/home/arcanum", "DlgKeep": "1"}
        self.calls: list[tuple[str, tuple[str, ...], Path, dict[str, str], float | None]] = []
        self.codes: dict[str, int] = {}
        self.smoke_text = SMOKE_PASS
        self.texts: dict[str, str] = {}
        quiet = mock.patch.object(daily, "say"); quiet.start(); self.addCleanup(quiet.stop)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def runner(self, command, cwd, environment, log, minutes) -> int:
        name = log.stem
        self.calls.append((name, tuple(command), cwd, environment, minutes))
        self.assertTrue((cwd / "tracked.txt").is_file(), "each step runs in the fresh checkout of the commit")
        log.write_text(self.smoke_text if name == "smoke" else self.texts.get(name, f"{name} output\n"))
        if self.codes.get(name) == -1:
            raise package.PackageError("stopped by signal 15")
        return self.codes.get(name, 0)

    def run_daily(self, **options) -> int:
        settings = {"fetch": False, "force": False, "keep_work": False, "dry_run": False}
        settings.update(options)
        return daily.daily(root=self.repo, output=self.output, runner=self.runner, environ=self.environ,
                           now=lambda: datetime(2026, 10, 29, 4, 0, tzinfo=timezone.utc), **settings)

    def names(self) -> list[str]:
        return [name for name, *_ in self.calls]

    def report(self) -> str:
        return (self.output / "logs" / "daily" / "report.txt").read_text()

    def worktrees(self) -> int:
        listed = subprocess.check_output(["git", "-C", str(self.repo), "worktree", "list", "--porcelain"], text=True)
        return listed.count("worktree ")

    def write_build(self, folder: str, commit: str, map_smoke: str = "PASS (crash-site, main)") -> None:
        (self.output / folder).mkdir(parents=True, exist_ok=True)
        (self.output / folder / "BUILD.txt").write_text(f"commit {commit}\ndate 2026-10-29\nproduct x\nsmoke PASS\n"
                                                        f"map-smoke {map_smoke}\n")

    def test_main_already_packaged_by_daily_on_both_platforms_is_skipped(self) -> None:
        self.write_build("macos", self.commit)
        self.write_build("windows", self.commit)
        self.assertEqual(self.run_daily(), daily.PASS)
        self.assertEqual(self.calls, [])
        self.assertFalse((self.output / "logs" / "daily").exists())

    def test_the_owner_playing_skips_the_day_even_with_force_and_records_nothing(self) -> None:  # ARC-DEV-2272
        with mock.patch.object(daily.run_queue, "playing_state", return_value={"since": 1.0, "until": None}):
            self.assertEqual(self.run_daily(force=True), daily.PASS)
            self.assertEqual(self.calls, [])
            self.assertFalse((self.output / "logs" / "daily").exists())  # no report: a skip is not a result
            self.assertEqual(self.run_daily(dry_run=True), daily.PASS)  # a dry run only lists the steps
        self.assertEqual(self.run_daily(), daily.PASS)  # the mode over: the day runs
        self.assertEqual(len(self.names()), 7)

    def test_one_platform_behind_a_package_made_by_hand_or_force_builds_again(self) -> None:
        self.write_build("macos", self.commit)
        self.write_build("windows", "0" * 40)
        self.assertEqual(self.run_daily(), daily.PASS)
        self.assertEqual(len(self.names()), 7)
        self.calls.clear()
        self.write_build("windows", self.commit, package.MAP_SMOKE_NOT_RUN)
        self.assertEqual(self.run_daily(), daily.PASS)
        self.assertEqual(len(self.names()), 7)
        self.calls.clear()
        self.write_build("windows", self.commit)
        self.assertEqual(self.run_daily(force=True), daily.PASS)
        self.assertEqual(len(self.names()), 7)

    def test_an_order_dependent_group_is_parsed_and_does_not_fail_the_chain(self) -> None:
        text = ("SMOKE summary\n  main          PASS exit=0\n  stages-main   ORDER exit=1\n"
                "  order-dependent: stages-main (fails only after other stages of its process; each of ai passes alone)\n"
                "SMOKE PASS 2 of 2 steps passed (1 order-dependent, each stage passes alone)\n")
        result = daily.parse_smoke(text, 0)
        self.assertEqual((result.passed, result.failed, result.failure), (["main"], [], None))
        self.assertEqual(result.order_steps, ["stages-main"])
        self.assertEqual(result.line(), "PASS (main) order-dependent (stages-main)")
        self.assertEqual(len(result.order_dependent), 1)

    def test_a_passing_day_checks_smokes_and_packages_the_same_commit_in_a_removed_worktree(self) -> None:
        self.assertEqual(self.run_daily(), daily.PASS)
        self.assertEqual(self.names(), ["dotnet", "dat-probe", "oracle", "docs-check", "smoke", "package-mac", "package-win"])
        commands = {name: command for name, command, *_ in self.calls}
        self.assertEqual(commands["dotnet"][:4], ("./arcanum", "run", "--kind", "dotnet"))
        self.assertIn("--locked-mode", commands["dotnet"][-1])
        self.assertIn('--filter "TestCategory!=RealData"', commands["dotnet"][-1])
        self.assertEqual(commands["smoke"], ("./arcanum", "smoke", "complete"))
        self.assertEqual(commands["package-mac"], ("./arcanum", "package", "mac", "--ref", self.commit, "--no-fetch",
                                                   "--map-smoke", "PASS (crash-site, main, town)"))
        self.assertEqual(commands["package-win"][2], "win")
        minutes = {name: limit for name, *_, limit in self.calls}
        self.assertEqual((minutes["dat-probe"], minutes["oracle"], minutes["smoke"]), (daily.CHECK_MINUTES, daily.CHECK_MINUTES, None))
        checkout = {cwd for _, _, cwd, _, _ in self.calls}
        self.assertEqual(len(checkout), 1)
        self.assertFalse(next(iter(checkout)).parent.exists(), "the temporary worktree is removed")
        self.assertEqual(self.worktrees(), 1)
        self.assertEqual(list(self.tmp.iterdir()), [])
        self.assertIn(f"commit {self.commit}", self.report())
        self.assertIn("RESULT PASS packaged", self.report())
        self.assertEqual((self.output / "macos" / "reports" / "bug-1.txt").read_text(), "player report\n")

    def test_the_root_checks_run_without_the_owners_opt_in_variables_and_with_the_oracle_venv(self) -> None:
        self.run_daily()
        environments = {name: environment for name, _, _, environment, _ in self.calls}
        for name in ("dotnet", "dat-probe", "oracle", "docs-check"):
            self.assertNotIn("DAT_ROOT", environments[name])
            self.assertNotIn("ARC_HD_PACK", environments[name])
            self.assertNotIn("ARCANUM_EXE", environments[name])
            self.assertEqual(environments[name]["ARCANUM_LOCAL_HOME"], "/home/arcanum")
        self.assertEqual(environments["oracle"]["ARCANUM_ORACLE_VENV"], str(self.repo / daily.ORACLE_VENV))
        self.assertEqual(environments["smoke"]["DAT_ROOT"], "/game", "the smoke runner drops them for the viewer itself")

    def test_failed_root_checks_still_run_the_smoke_and_block_the_packages(self) -> None:
        self.codes["oracle"] = self.codes["dotnet"] = 1
        self.texts["dotnet"] = ("ARCANUM_QUEUE start ticket=9 kind=dotnet waited=0s\nFAIL exit=1 ran=30s lines=80 log=/q/run-9-dotnet.log\n"
                                "first error at line 70 of 80 (1 error line(s)):\n  error CS1002: ; expected\n")
        self.texts["oracle"] = "oracle: installing the locked Unicorn failed (network or an unsupported platform)\n"
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertEqual(self.names(), ["dotnet", "dat-probe", "oracle", "docs-check", "smoke"])
        report = self.report()
        self.assertIn("RESULT FAIL root checks failed (dotnet, oracle); smoke passed: no package", report)
        self.assertIn("smoke PASS exit=0", report)
        self.assertIn("smoke summary: passed build, crash-site, main, town", report)
        self.assertIn("  FAIL exit=1 ran=30s lines=80 log=/q/run-9-dotnet.log\n", report)
        self.assertIn("    error CS1002: ; expected\n", report)
        self.assertIn("  oracle: installing the locked Unicorn failed", report)
        self.assertNotIn("ARCANUM_QUEUE", report)
        self.assertEqual(report.count(f"failed commit {self.commit}"), 1)
        self.assertEqual(self.worktrees(), 1)

    def test_red_checks_and_a_failed_smoke_are_both_reported_and_no_package_is_made(self) -> None:
        self.codes["dotnet"] = 1
        self.smoke_text, self.codes["smoke"] = SMOKE_FAIL, 1
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertEqual(self.names(), ["dotnet", "dat-probe", "oracle", "docs-check", "smoke"])
        report = self.report()
        self.assertIn("failed smoke town:", report)
        self.assertIn("RESULT FAIL root checks failed (dotnet) and smoke failed: town, ruins: no package", report)
        self.assertEqual(report.count("failed commit "), 1)

    def test_a_dotnet_timeout_does_not_hide_the_smoke_and_says_where_the_time_went(self) -> None:
        self.codes["dotnet"] = run_queue.TIMEOUT_EXIT
        self.texts["dotnet"] = ("ARCANUM_QUEUE start ticket=9 kind=dotnet waited=75s\n"
                                "ARCANUM_QUEUE timeout ticket=9 after 45 min; stopped its process group\n"
                                "ARCANUM_QUEUE end ticket=9 exit=124 ran=2700s\n")
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertIn("smoke", self.names())
        self.assertNotIn("package-mac", self.names())
        report = self.report()
        self.assertIn("dotnet FAIL exit=124", report)
        self.assertIn("  TIMEOUT: run time, the queue's limit of 45 minutes for ticket 9 after it started (it waited 75 s in the queue first)\n", report)
        self.assertIn("RESULT FAIL root checks failed (dotnet); smoke passed: no package", report)
        self.assertEqual(report.count("failed commit "), 1)

    def test_the_timeout_reason_tells_queue_wait_from_run_time(self) -> None:
        self.assertIn("cause unknown", daily.timeout_reason("ARCANUM_QUEUE enqueued ticket=3\n"))
        self.assertIn("the daily's own limit of 30 minutes", daily.timeout_reason("daily: stopped after 30 minutes\n"))
        self.assertIn("started (waited 5 s)", daily.timeout_reason("ARCANUM_QUEUE start ticket=1 kind=dotnet waited=5s\n"))
        # a smoke chain log holds many runs: the last timeout is paired with the start of its own ticket
        chain = ("ARCANUM_QUEUE start ticket=1 kind=window waited=1s\nARCANUM_QUEUE start ticket=2 kind=window waited=40s\n"
                 "ARCANUM_QUEUE timeout ticket=2 after 30 min; stopped its process group\n")
        reason = daily.timeout_reason(chain)
        self.assertIn("30 minutes for ticket 2", reason)
        self.assertIn("waited 40 s", reason)

    def test_red_checks_then_a_stopped_smoke_still_mark_the_commit_once(self) -> None:
        self.codes["dotnet"] = 1
        self.smoke_text, self.codes["smoke"] = SMOKE_FAIL.split("SMOKE step ruins")[0], 143
        self.assertEqual(self.run_daily(), daily.FAIL)
        report = self.report()
        self.assertEqual(report.count(f"failed commit {self.commit}"), 1)
        self.assertIn("RESULT FAIL root checks failed (dotnet) and the smoke chain was stopped (exit 143): no package", report)
        self.assertNotIn("package-mac", self.names())

    def test_an_unqueued_steps_own_timeout_is_explained_in_the_report(self) -> None:
        self.codes["oracle"] = run_queue.TIMEOUT_EXIT
        self.texts["oracle"] = "\ndaily: stopped after 30 minutes\n"
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertIn("  TIMEOUT: run time, the daily's own limit of 30 minutes for this step\n", self.report())
        self.assertEqual(self.report().count("failed commit "), 1)

    def test_a_stopped_check_ends_the_day_without_a_smoke(self) -> None:
        self.codes["dotnet"] = 143
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertEqual(self.names(), ["dotnet", "dat-probe", "oracle", "docs-check"])
        report = self.report()
        self.assertIn("a check was stopped, so no smoke and no package", report)
        self.assertNotIn("failed commit", report)

    def test_a_dry_run_lists_the_smoke_after_the_checks(self) -> None:
        printed = io.StringIO()
        with redirect_stdout(printed):
            self.run_daily(dry_run=True)
        lines = [line.split()[0] for line in printed.getvalue().splitlines() if "environment]" in line]
        self.assertEqual(lines, ["dotnet", "dat-probe", "oracle", "docs-check", "smoke", "package-mac", "package-win"])

    def test_failed_smokes_make_no_package_and_the_report_names_each_with_its_log(self) -> None:
        self.write_build("macos", "0" * 40)
        self.smoke_text, self.codes["smoke"] = SMOKE_FAIL, 1
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertNotIn("package-mac", self.names())
        report = self.report()
        self.assertIn("failed smoke town:\n  FAIL exit=1 ran=60s lines=700 log=/q/run-4-window.log\n", report)
        self.assertIn("stage=Upload code=RenderFailure", report)
        self.assertIn("failed smoke ruins:\n  FAIL exit=125", report)
        self.assertIn("RESULT FAIL smoke failed: town, ruins: no package", report)
        self.assertIn(f"commit {'0' * 40}", (self.output / "macos" / "BUILD.txt").read_text())
        # The same commit is not tried again every day; the fix's commit (or --force) is.
        self.calls.clear()
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertEqual(self.calls, [])
        self.assertEqual(self.run_daily(force=True), daily.FAIL)
        self.assertIn("smoke", self.names())

    def test_a_failed_viewer_build_of_the_chain_makes_no_package(self) -> None:
        self.smoke_text, self.codes["smoke"] = SMOKE_BUILD_FAIL, 1
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertNotIn("package-mac", self.names())
        self.assertIn("viewer build failed: no package", self.report())

    def test_a_failed_package_is_a_failed_day_and_the_other_platform_still_runs(self) -> None:
        self.codes["package-mac"] = 1
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertEqual(self.names()[-2:], ["package-mac", "package-win"])
        self.assertIn("RESULT FAIL package mac failed (installed: win)", self.report())

    def test_a_cut_off_chain_shows_the_running_smoke_and_is_tried_again(self) -> None:
        self.smoke_text = SMOKE_FAIL.split("SMOKE step ruins")[0]
        self.codes["smoke"] = 143
        self.assertEqual(self.run_daily(), daily.FAIL)
        report = self.report()
        self.assertIn("failed smoke town:\n  FAIL exit=1", report)
        self.assertIn("RESULT FAIL the smoke chain was stopped (exit 143)", report)
        self.assertNotIn("failed commit", report)
        self.calls.clear()
        self.smoke_text, self.codes["smoke"] = SMOKE_PASS, 0
        self.assertEqual(self.run_daily(), daily.PASS)

    def test_a_stop_mid_run_or_a_failed_worktree_still_cleans_up_and_reports(self) -> None:
        self.codes["smoke"] = -1
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertIn("RESULT STOPPED stopped by signal 15", self.report())
        self.assertNotIn("failed commit", self.report())
        self.assertEqual(self.worktrees(), 1)
        self.assertEqual(list(self.tmp.iterdir()), [])
        self.calls.clear()
        real = package.git

        def refuse(root, *arguments):
            if arguments[:2] == ("worktree", "add"):
                raise package.PackageError("git worktree add failed: disk full")
            return real(root, *arguments)
        with mock.patch.object(package, "git", side_effect=refuse):
            self.assertEqual(self.run_daily(force=True), daily.FAIL)
        self.assertEqual(self.calls, [])
        self.assertIn("RESULT FAIL git worktree add failed", self.report())
        self.assertEqual(list(self.tmp.iterdir()), [])

    def test_a_kept_or_killed_days_worktree_is_removed_by_the_next_day(self) -> None:
        self.assertEqual(self.run_daily(keep_work=True), daily.PASS)
        self.assertEqual(self.worktrees(), 2)
        (self.tmp / "other-tool").mkdir()
        self.calls.clear()
        self.assertEqual(self.run_daily(force=True), daily.PASS)
        self.assertEqual(self.worktrees(), 1)
        self.assertEqual([path.name for path in self.tmp.iterdir()], ["other-tool"])

    def test_a_dry_run_runs_nothing_and_a_nested_run_is_refused(self) -> None:
        printed = io.StringIO()
        with redirect_stdout(printed):
            self.assertEqual(self.run_daily(dry_run=True), daily.PASS)
        self.assertIn(f"package-mac [full environment] ./arcanum package mac --ref {self.commit}", printed.getvalue())
        self.assertEqual(self.calls, [])
        self.assertFalse((self.output / "logs" / "daily").exists())
        self.environ[run_queue.TICKET_ENV] = "7"
        with self.assertRaisesRegex(package.PackageError, "not inside ./arcanum run"):
            self.run_daily()

    def test_a_second_daily_run_is_refused_while_one_holds_the_lock(self) -> None:
        handle = daily.lock(self.repo)
        try:
            with self.assertRaisesRegex(package.PackageError, "another ./arcanum daily is running"):
                daily.lock(self.repo)
        finally:
            os.close(handle)
        os.close(daily.lock(self.repo))


class LauncherTests(unittest.TestCase):
    def test_package_passes_the_map_smoke_result_to_the_builder(self) -> None:
        project = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")
        args = project["parser"]().parse_args(["package", "mac", "--ref", "abc", "--no-fetch", "--map-smoke", "PASS (main)"])
        with mock.patch.object(run_queue, "run", return_value=0) as queued:
            self.assertEqual(args.func(args), 0)
        command = queued.call_args.args[1]
        self.assertEqual(command[-2:], ["--map-smoke", "PASS (main)"])
        self.assertEqual(queued.call_args.args[0], "export")
        args = project["parser"]().parse_args(["daily", "--dry-run", "--force", "--no-fetch", "--keep-work"])
        self.assertEqual((args.dry_run, args.force, args.no_fetch, args.keep_work), (True, True, True, True))


if __name__ == "__main__":
    unittest.main()
