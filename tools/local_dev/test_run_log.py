"""ARC-DEV-037: ./arcanum run writes the command's output to a log file and prints only PASS or FAIL with the first
error; --verbose streams it as before."""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys
import tempfile
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

    def test_verbose_streams_as_before(self) -> None:
        result = self.run_wrapper("print('hello')", "--verbose")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "hello\n")
        self.assertNotIn("ARCANUM_QUEUE log", result.stderr)


if __name__ == "__main__":
    unittest.main()
