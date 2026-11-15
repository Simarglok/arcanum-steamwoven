"""ARC-DEV-2225: the parallel runner of the tools/local_dev unit tests."""
from __future__ import annotations

from contextlib import redirect_stdout
import io
import unittest

from tools.local_dev import run_tests


class RunTestsTests(unittest.TestCase):
    def run_main(self, *argv: str) -> tuple[int, str]:
        output = io.StringIO()
        with redirect_stdout(output):
            return run_tests.main(list(argv)), output.getvalue()

    def test_every_test_module_is_listed_and_the_runner_itself_too(self) -> None:
        names = run_tests.modules()
        self.assertIn("tools.local_dev.test_run_queue", names)
        self.assertIn("tools.local_dev.test_run_tests", names)
        self.assertEqual(names, sorted(set(names)))

    def test_a_passing_module_gives_exit_0_and_a_summary_line(self) -> None:
        code, text = self.run_main("--jobs", "2", "test_world_clock_sync", "tools.local_dev.test_menubar")
        self.assertEqual(code, 0, text)
        self.assertIn("LOCAL_DEV TESTS PASS: 2/2", text)

    def test_a_module_that_does_not_load_fails_the_run_with_its_output(self) -> None:
        code, text = self.run_main("test_world_clock_sync", "test_no_such_module")
        self.assertEqual(code, 1)
        self.assertIn("FAIL tools.local_dev.test_no_such_module", text)
        self.assertIn("LOCAL_DEV TESTS FAIL: 1/2", text)


if __name__ == "__main__":
    unittest.main()
