"""ARC-DEV-1344: when the queue's menu-bar app starts, how it is built, and that a test never opens one."""
from __future__ import annotations

import fcntl
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from tools.local_dev import menubar


class EnabledTests(unittest.TestCase):
    def test_on_for_the_real_home_off_for_a_test_home_and_by_the_switch(self) -> None:
        self.assertTrue(menubar.enabled({}, "darwin"))
        self.assertFalse(menubar.enabled({}, "linux"))
        self.assertFalse(menubar.enabled({"ARCANUM_LOCAL_HOME": "/tmp/x"}, "darwin"))
        self.assertTrue(menubar.enabled({"ARCANUM_LOCAL_HOME": "/tmp/x", menubar.ENV: "1"}, "darwin"))
        for value in ("0", "off", "No", "false"):
            self.assertFalse(menubar.enabled({menubar.ENV: value}, "darwin"), value)


class AppTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(self.root / "home"), menubar.ENV: "1"})
        self.env.start()
        self.source = self.root / "App.swift"
        self.source.write_text("// one\n")

    def tearDown(self) -> None:
        self.env.stop()
        self.temp.cleanup()

    def test_running_follows_the_apps_lock(self) -> None:
        self.assertFalse(menubar.running())
        menubar.app_dir().mkdir(parents=True)
        fd = os.open(menubar.lock_path(), os.O_RDWR | os.O_CREAT, 0o600)
        try:
            self.assertFalse(menubar.running())
            fcntl.flock(fd, fcntl.LOCK_EX)
            self.assertTrue(menubar.running())
        finally:
            os.close(fd)  # the kernel frees the lock of a dead app
        self.assertFalse(menubar.running())

    def test_ensure_running_starts_one_detached_helper_only_when_the_app_is_not_running(self) -> None:
        calls: list = []
        self.assertTrue(menubar.ensure_running(spawn=lambda argv, **options: calls.append((argv, options))))
        argv, options = calls[0]
        self.assertEqual(argv[-3:], ["-m", "tools.local_dev.menubar", "start"])
        self.assertTrue(options["start_new_session"])
        menubar.lock_path().touch()
        fd = os.open(menubar.lock_path(), os.O_RDWR)
        try:
            fcntl.flock(fd, fcntl.LOCK_EX)
            self.assertFalse(menubar.ensure_running(spawn=lambda *a, **k: calls.append("again")))
        finally:
            os.close(fd)
        self.assertEqual(len(calls), 1)

    def test_a_test_home_never_starts_the_app(self) -> None:
        calls: list = []
        with patch.dict(os.environ):
            os.environ.pop(menubar.ENV)
            self.assertFalse(menubar.ensure_running(spawn=lambda *a, **k: calls.append(1)))
        self.assertEqual(calls, [])

    def test_a_failed_spawn_never_raises(self) -> None:
        def broken(*args, **options):
            raise OSError("no")
        self.assertFalse(menubar.ensure_running(spawn=broken))

    def test_build_compiles_once_per_source_and_drops_older_builds(self) -> None:
        seen: list = []

        def compiler(argv, **options):
            seen.append(argv)
            Path(argv[-1]).write_text("binary")
            return subprocess.CompletedProcess(argv, 0, "", "")
        target, why = menubar.build(self.source, run=compiler, which=lambda name: "/usr/bin/swiftc")
        self.assertEqual((why, target.exists(), len(seen)), ("", True, 1))
        self.assertEqual(seen[0][:3], ("/usr/bin/swiftc", "-O", "-warnings-as-errors"))
        again, _ = menubar.build(self.source, run=compiler, which=lambda name: "/usr/bin/swiftc")
        self.assertEqual((again, len(seen)), (target, 1))  # cached by the source's hash
        self.source.write_text("// two\n")
        newer, _ = menubar.build(self.source, run=compiler, which=lambda name: "/usr/bin/swiftc")
        self.assertNotEqual(newer, target)
        self.assertFalse(target.exists())
        self.assertEqual(len(seen), 2)

    def test_build_reports_what_is_missing_or_broken(self) -> None:
        self.assertIn("no swiftc", menubar.build(self.source, which=lambda name: None)[1])
        self.assertIn("no source", menubar.build(self.root / "missing.swift")[1])

        def failing(argv, **options):
            return subprocess.CompletedProcess(argv, 1, "", "error: boom")
        target, why = menubar.build(self.source, run=failing, which=lambda name: "/usr/bin/swiftc")
        self.assertIsNone(target)
        self.assertIn("boom", why)
        self.assertEqual(list(menubar.app_dir().glob(".build-*")), [])


if __name__ == "__main__":
    unittest.main()
