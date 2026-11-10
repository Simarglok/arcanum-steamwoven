"""ARC-DEV-341: the owner-local HD scripts in tools/hd_pack/res095 resolve local/ through hd_paths and carry no personal path."""
from __future__ import annotations

import ast
import os
from pathlib import Path
import unittest
from unittest import mock

from tools.hd_pack.res095 import hd_paths

HERE = Path(__file__).resolve().parent
SCRIPTS = ["build_res095.py", "fit_res095.py", "pack_res095.py", "hdlib.py", "metrics.py", "hd_paths.py"]


class HdPathsTest(unittest.TestCase):
    def test_env_override_wins(self) -> None:
        with mock.patch.dict(os.environ, {"ARC_MAIN_CHECKOUT": str(HERE)}):
            self.assertEqual(hd_paths.main_checkout(), HERE)

    def test_main_checkout_is_the_repository_holding_the_git_dir(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=False):
            os.environ.pop("ARC_MAIN_CHECKOUT", None)
            main = hd_paths.main_checkout()
        self.assertTrue((main / ".git").is_dir(), f"{main} is not a main checkout (no .git folder)")

    def test_roots_are_under_local_of_the_main_checkout(self) -> None:
        self.assertEqual(hd_paths.W94, f"{hd_paths.MAIN}/local/res094")
        self.assertEqual(hd_paths.OUT, f"{hd_paths.MAIN}/local/res095")

    def test_scripts_parse_and_hold_no_personal_path(self) -> None:
        for name in SCRIPTS:
            text = (HERE / name).read_text(encoding="utf-8")
            ast.parse(text, filename=name)
            self.assertNotIn("/Users/", text, name)
            self.assertNotIn("simarglok", text.lower(), name)


if __name__ == "__main__":
    unittest.main()
