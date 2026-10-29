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


class DailyTests(unittest.TestCase):
    def setUp(self) -> None:
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
        quiet = mock.patch.object(daily, "say"); quiet.start(); self.addCleanup(quiet.stop)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def runner(self, command, cwd, environment, log, minutes) -> int:
        name = log.stem
        self.calls.append((name, tuple(command), cwd, environment, minutes))
        self.assertTrue((cwd / "tracked.txt").is_file(), "each step runs in the fresh checkout of the commit")
        log.write_text(self.smoke_text if name == "smoke" else f"{name} output\n")
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

    def test_one_platform_behind_a_package_made_by_hand_or_force_builds_again(self) -> None:
        self.write_build("macos", self.commit)
        self.write_build("windows", "0" * 40)
        self.assertEqual(self.run_daily(), daily.PASS)
        self.assertEqual(len(self.names()), 6)
        self.calls.clear()
        self.write_build("windows", self.commit, package.MAP_SMOKE_NOT_RUN)
        self.assertEqual(self.run_daily(), daily.PASS)
        self.assertEqual(len(self.names()), 6)
        self.calls.clear()
        self.write_build("windows", self.commit)
        self.assertEqual(self.run_daily(force=True), daily.PASS)
        self.assertEqual(len(self.names()), 6)

    def test_a_passing_day_checks_smokes_and_packages_the_same_commit_in_a_removed_worktree(self) -> None:
        self.assertEqual(self.run_daily(), daily.PASS)
        self.assertEqual(self.names(), ["dotnet", "dat-probe", "oracle", "smoke", "package-mac", "package-win"])
        commands = {name: command for name, command, *_ in self.calls}
        self.assertEqual(commands["dotnet"][:4], ("./arcanum", "run", "--kind", "dotnet"))
        self.assertIn("--locked-mode", commands["dotnet"][-1])
        self.assertIn('--filter "TestCategory!=RealData"', commands["dotnet"][-1])
        self.assertEqual(commands["smoke"], ("./arcanum", "smoke", "full"))
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
        for name in ("dotnet", "dat-probe", "oracle"):
            self.assertNotIn("DAT_ROOT", environments[name])
            self.assertNotIn("ARC_HD_PACK", environments[name])
            self.assertNotIn("ARCANUM_EXE", environments[name])
            self.assertEqual(environments[name]["ARCANUM_LOCAL_HOME"], "/home/arcanum")
        self.assertEqual(environments["oracle"]["ARCANUM_ORACLE_VENV"], str(self.repo / daily.ORACLE_VENV))
        self.assertEqual(environments["smoke"]["DAT_ROOT"], "/game", "the smoke runner drops them for the viewer itself")

    def test_failed_root_checks_stop_before_the_smoke_and_the_packages(self) -> None:
        self.codes["oracle"] = 1
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertEqual(self.names(), ["dotnet", "dat-probe", "oracle"])
        self.assertIn("RESULT FAIL root checks failed (oracle)", self.report())
        self.assertEqual(self.worktrees(), 1)

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

    def test_a_stop_mid_run_or_a_failed_worktree_still_cleans_up_and_reports(self) -> None:
        self.codes["smoke"] = -1
        self.assertEqual(self.run_daily(), daily.FAIL)
        self.assertIn("RESULT FAIL stopped by signal 15", self.report())
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
