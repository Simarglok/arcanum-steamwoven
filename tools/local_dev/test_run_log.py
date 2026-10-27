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
                     "ARC-RENDER-004 native-smoke failure mode=area stage=Arguments code=Usage", "[FAIL] probe"):
            self.assertTrue(run_log.is_error(line), line)

    def test_passing_summaries_and_ordinary_lines_are_not_errors(self) -> None:
        for line in ("Build succeeded.", "    0 Error(s)", "Passed!  - Failed:     0, Passed:    10, Skipped: 0",
                     "ARC-UI-004 native-smoke inventory PASS {}", "errors=0 warnings=0", "the error budget is fine",
                     "WALL_PASSWALL_UNOBSERVED is not a failure code"):
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
        code = ("import sys\nfor n in range(500): print('noise', n)\n"
                "print('ERROR: broken thing', file=sys.stderr)\nsys.exit(3)")
        result = self.run_wrapper(code)
        self.assertEqual(result.returncode, 3)
        self.assertNotIn("noise 1\n", result.stdout)
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

    def test_verbose_streams_as_before(self) -> None:
        result = self.run_wrapper("print('hello')", "--verbose")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "hello\n")
        self.assertNotIn("ARCANUM_QUEUE log", result.stderr)


if __name__ == "__main__":
    unittest.main()
