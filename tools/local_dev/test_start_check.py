"""ARC-DEV-059: ./arcanum run waits for its command's start line and fails a run that did not start."""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

from tools.local_dev import start_check

REPO = Path(__file__).resolve().parents[2]
ARCANUM = REPO / "arcanum"
VIEWER_START = ("ARC-DEV-059 viewer start mode=area map=elven ruins main-menu=False smoke=True")
USAGE = "ARC-DEV-059 viewer failure Diagnostic mode unresolved | failure stage=Arguments code=Usage | terrain 0/0/0"


class PatternTests(unittest.TestCase):
    def test_the_default_start_lines_of_each_program(self) -> None:
        check = start_check.for_run("window", None, None)
        assert check is not None
        for line in (VIEWER_START, "ARC-RENDER-004 load stage=begin smoke=True",
                     "ARC-DEV-043 self-test stage AreaRaster begin t=3ms can-draw=False focused=True forced-draws=0",
                     "export-endpoint: validate ARC-RENDER-007 abc123 evidence=/x", "validate-macos: step build",
                     "package: fetch origin main"):
            self.assertEqual(start_check.scan(check, [line]), ("started", line))
        # Godot's banner and the build line also come from the project manager or a start that then fails.
        self.assertIsNone(start_check.scan(check, ["Godot Engine v4.7.2.stable.mono.official", "ARC-DEV-047 build x"]))
        # Import progress is a start only for an import: a first import also shows it before the viewer's arguments.
        progress = "[   0% ] first_scan_filesystem | Started Project initialization"
        self.assertIsNone(start_check.scan(check, [progress]))
        importing = start_check.for_run("headless", None, None, ["Godot", "--headless", "--import"])
        assert importing is not None
        self.assertEqual(start_check.scan(importing, [progress]), ("started", progress))
        # The phase lines' own failures are not starts.
        self.assertIsNone(start_check.scan(check, ["export-endpoint: validate FAILED exit=1", "package: could not start x",
                                                   "package: stopped after 3s", "validate-macos: step build FAILED exit=2 log=/x"]))

    def test_start_failures(self) -> None:
        check = start_check.for_run("headless", None, None)
        assert check is not None
        for line in (USAGE, "usage: arcanum run [-h] --kind {window,headless,export,dotnet}",
                     "arcanum: error: unrecognized arguments: --game-root", "sh: Godot: command not found",
                     "ARC-DEV-059 viewer failure Diagnostic mode unresolved | failure stage=GameFolder code=StartFailed |",
                     "python3: can't open file '/x/smoke.py': [Errno 2] No such file or directory",
                     "zsh: command not found: Godot", "zsh:1: command not found: Godot", "/bin/sh: line 1: ./run.sh: No such file or directory",
                     "env: godot: No such file or directory"):
            self.assertEqual(start_check.scan(check, ["Godot Engine v4.7.2", line, VIEWER_START]), ("failed", line))
        # After the start line a failure is the run's result, not a failed start.
        self.assertEqual(start_check.scan(check, [VIEWER_START, USAGE]), ("started", VIEWER_START))
        # An engine line about some missing file is not the shell's or Python's own message.
        self.assertIsNone(start_check.scan(check, ["ERROR: cannot open /x/cache.bin: No such file or directory",
                                                   "WARNING: command not found in the editor settings"]))
        # A load failure (not an argument failure) is not a start failure.
        self.assertIsNone(start_check.scan(check, ["ARC-DEV-059 viewer failure Diagnostic mode area | "
                                                   "failure stage=Terrain code=Missing |"]))

    def test_choices(self) -> None:
        self.assertIsNone(start_check.for_run("dotnet", None, None))  # a build ends by itself
        self.assertIsNone(start_check.for_run("window", None, 0))
        custom = start_check.for_run("dotnet", "^Passed!", 30)
        assert custom is not None
        self.assertEqual((custom.start.pattern, custom.seconds), ("^Passed!", 30))
        with self.assertRaises(ValueError):
            start_check.for_run("window", "(", None)
        with self.assertRaises(ValueError):
            start_check.for_run("window", None, -1)

    def test_the_programs_still_print_the_default_start_lines(self) -> None:
        # The patterns follow the programs' own prints; a renamed line would fail every run of that program.
        sources = {"tools/terrain-viewer/Godot/TerrainViewerApp.cs": ('"ARC-DEV-059 viewer start mode=', "ARC-DEV-059 viewer failure "),
                   "tools/terrain-viewer/Godot/TerrainViewerSelfTest.cs": ("ARC-DEV-043 self-test stage ",),
                   "tools/local_dev/export_endpoint.py": ("export-endpoint: ",),
                   "tools/terrain-viewer/Godot/tools/validate-macos.sh": ("validate-macos: step ",),
                   "tools/local_dev/package.py": ('print(f"package: {message}"',)}
        for path, texts in sources.items():
            text = (REPO / path).read_text()
            for item in texts:
                self.assertIn(item, text, path)
        for item in ("ARC-DEV-059 viewer start ", "ARC-DEV-043 self-test stage ", "export-endpoint: ",
                     "validate-macos: step ", "package: "):
            self.assertIn(item, start_check.GODOT_START, item)


class WrapperStartTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.env = {**os.environ, "ARCANUM_LOCAL_HOME": str(Path(self.temp.name) / "home"),
                    "ARCANUM_QUEUE_BUILD_SERVER_SHUTDOWN": "0"}
        self.env.pop("ARCANUM_QUEUE_TICKET", None)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def run_wrapper(self, code: str, *extra: str, kind: str = "headless",
                    args: tuple[str, ...] = ()) -> tuple[subprocess.CompletedProcess, float]:
        began = time.monotonic()
        result = subprocess.run([sys.executable, "-B", str(ARCANUM), "run", "--kind", kind, *extra, "--",
                                 sys.executable, "-c", code, *args], capture_output=True, text=True, env=self.env,
                                timeout=90)
        return result, time.monotonic() - began

    def test_a_run_that_started_runs_to_its_end(self) -> None:
        result, _ = self.run_wrapper(f"import time; print({VIEWER_START!r}, flush=True); time.sleep(1.5); print('done')",
                                     "--start-within", "1")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(len(result.stdout.splitlines()), 1)
        self.assertTrue(result.stdout.startswith("PASS exit=0 "))
        self.assertRegex(result.stderr, r"ARCANUM_QUEUE started ticket=\d+ after=\d+s line=ARC-DEV-059 viewer start mode=")

    def test_no_start_line_in_time_is_stopped(self) -> None:
        result, took = self.run_wrapper("import time; print('Godot Engine v4.7.2', flush=True); time.sleep(60)",
                                        "--start-within", "1")
        self.assertEqual(result.returncode, start_check.FAILED_START_EXIT, result.stdout)
        self.assertLess(took, 30)
        out = result.stdout.splitlines()
        self.assertTrue(out[0].startswith("FAIL exit=125 "), out)
        self.assertTrue(out[1].startswith("start check: no start line within 1s ("), out)
        self.assertIn("--expect", out[1])
        self.assertIn("Godot Engine v4.7.2", result.stdout)  # the log's tail
        self.assertIn("ARCANUM_QUEUE start-failed ticket=", result.stderr)

    def test_a_usage_error_that_stays_open_is_stopped_after_the_grace(self) -> None:
        # The viewer draws its usage failure and keeps its window open.
        result, took = self.run_wrapper(f"import time; print({USAGE!r}, flush=True); time.sleep(60)")
        self.assertEqual(result.returncode, start_check.FAILED_START_EXIT, result.stdout)
        self.assertLess(took, start_check.GRACE_SECONDS + 20)
        out = result.stdout.splitlines()
        self.assertTrue(out[1].startswith("start check: the command did not start: ARC-DEV-059 viewer failure"), out)
        self.assertTrue(out[1].endswith("stopped it"), out)

    def test_a_usage_error_that_exits_keeps_its_exit_code(self) -> None:
        code = "import sys; print('usage: smoke.py MAP', file=sys.stderr); print('smoke.py: error: argument MAP', file=sys.stderr); sys.exit(2)"
        result, _ = self.run_wrapper(code, kind="window")
        self.assertEqual(result.returncode, 2, result.stdout)
        self.assertEqual(result.stdout.splitlines()[1], "start check: the command did not start: usage: smoke.py MAP")

    def test_an_end_before_the_start_line(self) -> None:
        # Exit 0 (an import, the WORLD settings test) passes with a note that its mode was not checked.
        result, _ = self.run_wrapper("print('exit=0')")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("note: ticket=", result.stderr)
        self.assertIn("ended with 0 before a start line", result.stderr)
        result, _ = self.run_wrapper("import sys; print('broken'); sys.exit(4)")
        self.assertEqual(result.returncode, 4, result.stdout)
        self.assertIn("start check: the command ended before a start line", result.stdout)

    def test_a_start_line_within_the_grace_cancels_a_failure_line(self) -> None:
        code = (f"import time; print('sh: helper: command not found', flush=True); time.sleep(1); "
                f"print({VIEWER_START!r}, flush=True); print('done')")
        result, _ = self.run_wrapper(code)
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("ARCANUM_QUEUE started ticket=", result.stderr)

    def test_a_start_line_in_a_progress_line_or_without_a_newline_counts(self) -> None:
        for code in (f"import sys, time; sys.stdout.write('[  1% ] scan\\r'); sys.stdout.flush(); time.sleep(3)",
                     f"import sys; sys.stdout.write({VIEWER_START!r})"):
            result, _ = self.run_wrapper(code, "--start-within", "2", args=("--import",))
            self.assertEqual(result.returncode, 0, (code, result.stdout))
            self.assertIn("ARCANUM_QUEUE started ticket=", result.stderr, code)

    def test_a_limit_inside_the_window_is_a_timeout_not_a_failed_start(self) -> None:
        result, _ = self.run_wrapper("import time; time.sleep(60)", "--timeout", "0.03")
        self.assertEqual(result.returncode, 124, result.stdout)
        self.assertNotIn("start check:", result.stdout)

    def test_expect_replaces_the_default_and_checks_dotnet(self) -> None:
        result, _ = self.run_wrapper("print('Passed!  - Failed: 0, Passed: 3')", "--expect", "^Passed!", kind="dotnet")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("ARCANUM_QUEUE started ticket=", result.stderr)
        result, _ = self.run_wrapper("print('nothing')", "--expect", "^Passed!", kind="dotnet")
        self.assertEqual(result.returncode, start_check.FAILED_START_EXIT, result.stdout)  # a given start line is required
        result, _ = self.run_wrapper(f"import time; print({VIEWER_START!r}, flush=True); time.sleep(30)", "--expect",
                                     "^SMOKE ", "--start-within", "1")
        self.assertEqual(result.returncode, start_check.FAILED_START_EXIT, result.stdout)

    def test_dotnet_and_start_within_zero_are_not_checked(self) -> None:
        for extra, kind in (((), "dotnet"), (("--start-within", "0"), "window")):
            result, _ = self.run_wrapper("print('no start line')", *extra, kind=kind)
            self.assertEqual(result.returncode, 0, (kind, result.stdout))
            self.assertNotIn("start", result.stderr.replace("ARCANUM_QUEUE start ticket=", ""), kind)

    def test_verbose_is_not_checked_and_says_so(self) -> None:
        result, _ = self.run_wrapper("print('no start line')", "--verbose")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("note: a --verbose run has no start check", result.stderr)

    def test_a_bad_expect_is_a_usage_error_before_queueing(self) -> None:
        result, _ = self.run_wrapper("print('x')", "--expect", "(")
        self.assertEqual(result.returncode, 2)
        self.assertIn("--expect is not a valid regular expression", result.stderr)
        self.assertEqual(list((Path(self.env["ARCANUM_LOCAL_HOME"]) / "run-queue").glob("t-*.json")), [])


if __name__ == "__main__":
    unittest.main()
