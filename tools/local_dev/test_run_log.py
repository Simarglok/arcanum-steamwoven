"""ARC-DEV-037: ./arcanum run writes the command's output to a log file and prints only PASS or FAIL with the first
error; --verbose streams it as before."""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

from tools.local_dev import run_log

REPO = Path(__file__).resolve().parents[2]
ARCANUM = REPO / "arcanum"


class ErrorLineTests(unittest.TestCase):
    def test_godot_dotnet_python_and_smoke_errors_are_found(self) -> None:
        for line in ("ERROR: Cannot open file 'res://x.tscn'.", "SCRIPT ERROR: Invalid call.", "USER ERROR: bad",
                     "src/A.cs(3,5): error CS0103: The name 'x' does not exist", "  Failed SomeTest [12 ms]",
                     "Failed!  - Failed:     2, Passed:    10, Skipped: 0", "Traceback (most recent call last):",
                     "Unhandled exception. System.InvalidOperationException: nope",
                     "ARC-RENDER-003 self-test FAIL RenderFailure", "result=AREA_CAPTURE_FAILED map=x",
                     "ARC-RENDER-004 native-smoke failure mode=area stage=Arguments code=Usage", "[FAIL] probe",
                     "Failed: 0, Errors: 3", "    3 Error(s)", "ValueError: bad value", "fatal: not a git repository",
                     "error: pathspec 'x' did not match", "sh: godot: command not found", "Segmentation fault: 11"):
            self.assertTrue(run_log.is_error(line), line)

    def test_passing_summaries_and_ordinary_lines_are_not_errors(self) -> None:
        for line in ("Build succeeded.", "    0 Error(s)", "Passed!  - Failed:     0, Passed:    10, Skipped: 0",
                     "ARC-UI-004 native-smoke inventory PASS {}", "errors=0 warnings=0", "the error budget is fine",
                     "WALL_PASSWALL_UNOBSERVED is not a failure code", "Errors: 0, Failed: 0",
                     "    0 Warning(s)", "Time Elapsed 00:00:03.21"):
            self.assertFalse(run_log.is_error(line), line)

    def test_first_error_keeps_three_lines_before_and_forty_in_all(self) -> None:
        lines = [f"line {number}" for number in range(100)]
        lines[50] = "ERROR: first"
        lines[70] = "ERROR: second"
        index, context = run_log.first_error(lines)
        self.assertEqual(index, 50)
        self.assertEqual(len(context), run_log.CONTEXT_LINES)
        self.assertEqual(context[:4], ["line 47", "line 48", "line 49", "ERROR: first"])

    def test_long_lines_are_cut(self) -> None:
        _, context = run_log.first_error(["ERROR: " + "x" * 1000])
        self.assertEqual(len(context[0]), run_log.MAX_WIDTH + len(" ..."))


class SummaryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.log = Path(self.temp.name) / "run.log"

    def tearDown(self) -> None:
        self.temp.cleanup()

    def summary(self, text: str, code: int) -> list[str]:
        self.log.write_text(text)
        return run_log.summary(self.log, code, 3.0)

    def test_a_clean_pass_is_one_line(self) -> None:
        self.assertEqual(self.summary("a\nb\n", 0), [f"PASS exit=0 ran=3s lines=2 log={self.log}"])

    def test_a_pass_with_logged_errors_names_the_first(self) -> None:
        lines = self.summary("ok\nSCRIPT ERROR: one\nERROR: two\n", 0)
        self.assertEqual(len(lines), 2)
        self.assertEqual(lines[1], "note: 2 error line(s) in the log; first (line 2): SCRIPT ERROR: one")

    def test_a_failure_shows_the_first_error_with_context(self) -> None:
        lines = self.summary("start\nstage 1\nERROR: boom\n   at: x.cs:3\n", 1)
        self.assertEqual(lines[0], f"FAIL exit=1 ran=3s lines=4 log={self.log}")
        self.assertEqual(lines[1], "first error at line 3 of 4 (1 error line(s)):")
        self.assertEqual(lines[2:], ["start", "stage 1", "ERROR: boom", "   at: x.cs:3"])

    def test_a_failure_also_names_a_last_error_beyond_the_context(self) -> None:
        text = "ERROR: harmless startup noise\n" + "".join(f"line {number}\n" for number in range(100)) + \
            "ARC-RENDER-004 native-smoke failure mode=area stage=Load code=X\n"
        lines = self.summary(text, 1)
        self.assertEqual(lines[1], "first error at line 1 of 102 (2 error line(s)):")
        self.assertEqual(lines[-1], "last error (line 102): ARC-RENDER-004 native-smoke failure mode=area stage=Load code=X")
        self.assertEqual(len(lines), 2 + run_log.CONTEXT_LINES + 1)
        near = self.summary("ERROR: one\nERROR: two\n", 1)
        self.assertEqual(near[2:], ["ERROR: one", "ERROR: two"])  # the last error is already shown

    def test_a_failure_without_an_error_line_shows_the_tail(self) -> None:
        lines = self.summary("".join(f"line {number}\n" for number in range(60)), 124)
        self.assertEqual(lines[1], "no error line found; the last 40 lines:")
        self.assertEqual(lines[2], "line 20")
        self.assertEqual(len(lines), 42)
        self.assertEqual(self.summary("", 2)[1:], ["the command printed nothing"])


class LogFolderTests(unittest.TestCase):
    def test_only_the_newest_logs_are_kept(self) -> None:
        with tempfile.TemporaryDirectory() as temp, patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": temp}), \
                patch.object(run_log, "KEEP_LOGS", 3):
            for seq in range(1, 6):
                run_log.open_log(seq, "dotnet").write_text("x")
            names = sorted(path.name for path in run_log.folder().iterdir())
            self.assertEqual(names, [f"run-{seq:012d}-dotnet.log" for seq in (3, 4, 5)])


class WrapperTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.env = {**os.environ, "ARCANUM_LOCAL_HOME": str(Path(self.temp.name) / "home"),
                    "ARCANUM_QUEUE_LEGACY_LOCK": str(Path(self.temp.name) / "legacy.lock")}
        self.env.pop("ARCANUM_QUEUE_TICKET", None)
        self.env.pop("PYTHONUNBUFFERED", None)  # a suite run inside the queue must still test that the queue sets it

    def tearDown(self) -> None:
        self.temp.cleanup()

    def run_wrapper(self, code: str, *extra: str) -> subprocess.CompletedProcess:
        return subprocess.run([sys.executable, "-B", str(ARCANUM), "run", "--kind", "headless", *extra, "--",
                               sys.executable, "-c", code], capture_output=True, text=True, env=self.env, timeout=60)

    def test_quiet_by_default_the_output_goes_to_the_log(self) -> None:
        code = ("import sys\nfor n in range(500): print('noise', n)\nsys.stdout.flush()\n"
                "print('ERROR: broken thing', file=sys.stderr)\nsys.exit(3)")
        result = self.run_wrapper(code)
        self.assertEqual(result.returncode, 3)
        self.assertNotIn("noise 1\n", result.stdout)
        self.assertIn("noise 499", result.stdout)  # the context before the error
        out = result.stdout.splitlines()
        self.assertTrue(out[0].startswith("FAIL exit=3 "), out)
        self.assertIn("ERROR: broken thing", out)
        self.assertLessEqual(len(out), 2 + run_log.CONTEXT_LINES)
        log = Path(out[0].split("log=", 1)[1])
        text = log.read_text()
        self.assertIn("noise 499", text)
        self.assertIn("ERROR: broken thing", text)
        self.assertIn(f"ARCANUM_QUEUE log ticket=", result.stderr)
        self.assertIn("ARCANUM_QUEUE start ticket=", result.stderr)
        self.assertIn("ARCANUM_QUEUE end ticket=", result.stderr)

    def test_a_pass_prints_one_line(self) -> None:
        result = self.run_wrapper("print('hello')")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(len(result.stdout.splitlines()), 1)
        self.assertTrue(result.stdout.startswith("PASS exit=0 "))

    def test_a_run_out_of_time_fails_with_124(self) -> None:
        result = self.run_wrapper("import time; print('waiting', flush=True); time.sleep(60)", "--timeout", "0.03")
        self.assertEqual(result.returncode, 124)
        out = result.stdout.splitlines()
        self.assertTrue(out[0].startswith("FAIL exit=124 "), out)
        self.assertIn("waiting", out)

    def test_a_nested_run_writes_into_its_parents_log_with_one_summary(self) -> None:
        inner = f"{sys.executable} -B {ARCANUM} run --kind headless -- {sys.executable} -c \"print('inner says hi')\""
        result = subprocess.run([sys.executable, "-B", str(ARCANUM), "run", "--kind", "headless", "--", "sh", "-c", inner],
                                capture_output=True, text=True, env=self.env, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        out = result.stdout.splitlines()
        self.assertEqual(len(out), 1, out)
        self.assertIn("inner says hi", Path(out[0].split("log=", 1)[1]).read_text())

    def test_a_log_folder_that_cannot_be_used_leaves_no_ticket(self) -> None:
        home = Path(self.env["ARCANUM_LOCAL_HOME"])
        home.mkdir(parents=True)
        (home / "elsewhere").mkdir()
        (home / "run-logs").symlink_to(home / "elsewhere")
        result = self.run_wrapper("print('never')")
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("never", result.stdout)
        self.assertEqual(list((home / "run-queue").glob("t-*.json")), [])

    def test_the_log_fills_while_the_command_runs(self) -> None:
        # ARC-DEV-041: a Python command (the export endpoint) wrote nothing to the log until it ended; the queue now runs
        # it unbuffered, so the start check can read the log while the command still runs.
        release = Path(self.temp.name) / "release"
        code = (f"import os, time\nprint('first line')\ndeadline = time.time() + 30\n"
                f"while not os.path.exists({str(release)!r}) and time.time() < deadline: time.sleep(0.05)\n"
                f"print('second line')")
        process = subprocess.Popen([sys.executable, "-B", str(ARCANUM), "run", "--kind", "headless", "--",
                                    sys.executable, "-c", code], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   text=True, env=self.env)
        try:
            logs = Path(self.env["ARCANUM_LOCAL_HOME"]) / "run-logs"
            deadline = time.monotonic() + 20
            while not any("first line" in item.read_text() for item in logs.glob("run-*.log")):
                self.assertIsNone(process.poll(), "the command ended before its first line reached the log")
                self.assertLess(time.monotonic(), deadline, "the first line did not reach the log while the run went")
                time.sleep(0.05)
        finally:
            release.touch()
            out, err = process.communicate(timeout=60)
        self.assertEqual(process.returncode, 0, err)
        self.assertIn("second line", Path(out.splitlines()[0].split("log=", 1)[1]).read_text())

    def dotnet_run(self, root: Path) -> tuple[subprocess.CompletedProcess, str]:
        other = Path(self.temp.name) / "homebrew"
        other.mkdir()
        (other / "dotnet").write_text("#!/bin/sh\necho homebrew-sdk\n")
        (other / "dotnet").chmod(0o755)
        env = {**self.env, "DOTNET_ROOT": str(root), "PATH": f"{other}:/usr/bin:/bin"}
        result = subprocess.run([sys.executable, "-B", str(ARCANUM), "run", "--kind", "dotnet", "--", "sh", "-c",
                                 'dotnet; echo "root=$DOTNET_ROOT"'], capture_output=True, text=True, env=env, timeout=60)
        return result, Path(result.stdout.splitlines()[0].split("log=", 1)[1]).read_text()

    def test_dotnet_is_the_pinned_sdk_not_the_first_on_path(self) -> None:
        # ARC-DEV-041: `dotnet build-server shutdown` in the queue found Homebrew's 8.0.131 first on PATH.
        root = Path(self.temp.name) / "pinned"
        root.mkdir()
        (root / "dotnet").write_text("#!/bin/sh\necho pinned-sdk\n")
        (root / "dotnet").chmod(0o755)
        result, text = self.dotnet_run(root)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("pinned-sdk", text)
        self.assertNotIn("homebrew-sdk", text)
        self.assertIn(f"root={root}", text)

    def test_a_missing_pinned_sdk_is_named_and_leaves_path(self) -> None:
        result, text = self.dotnet_run(Path(self.temp.name) / "no-sdk")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("homebrew-sdk", text)
        self.assertIn("ARCANUM_QUEUE note: no pinned SDK at", result.stderr)

    def test_verbose_streams_as_before(self) -> None:
        result = self.run_wrapper("print('hello')", "--verbose")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "hello\n")
        self.assertNotIn("ARCANUM_QUEUE log", result.stderr)


class ExportStepTests(unittest.TestCase):
    """ARC-DEV-041: the terrain export names each step in its own output (the queue's run log) and, when one fails,
    its exit code, evidence log and last lines, so the run log is not empty while the steps log elsewhere."""

    def test_steps_are_named_and_a_failure_shows_its_log_tail(self) -> None:
        source = (REPO / "tools/terrain-viewer/Godot/tools/validate-macos.sh").read_text()
        function = "run_log() {" + source.split("\nrun_log() {", 1)[1].split("\n}\n", 1)[0] + "\n}\n"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "evidence").mkdir()
            script = root / "steps.sh"
            script.write_text("set -eu\n" + function + "run_log ok.log sh -c 'echo fine'\n"
                              "run_log bad.log sh -c 'echo first; echo why it broke; exit 7'\necho after\n")
            result = subprocess.run(["/bin/sh", str(script)], capture_output=True, text=True, timeout=30,
                                    env={**os.environ, "task": "ARC-DEV-015", "area_task": "0", "build_root": str(root)})
            self.assertEqual(result.returncode, 7, result.stderr)
            evidence = root / "evidence"
            self.assertEqual(result.stdout.splitlines(), [
                "validate-macos: step ok.log", "validate-macos: step bad.log",
                f"validate-macos: step bad.log FAILED exit=7 log={evidence / 'bad.log'}", "first", "why it broke"])
            self.assertEqual((evidence / "ok.log").read_text(), "fine\n")
            self.assertTrue(run_log.is_error(result.stdout.splitlines()[2]))
            self.assertFalse(run_log.is_error(result.stdout.splitlines()[0]))


if __name__ == "__main__":
    unittest.main()
