"""ARC-DEV-403: the smoke windows load the focus guard (a background-only Godot process), built per run, never required."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import io
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

from tools.local_dev import focus_guard, smoke

from tools.local_dev import test_smoke
from tools.local_dev.test_smoke import ENVIRON, parse


def done(code: int = 0, err: str = "") -> subprocess.CompletedProcess:
    return subprocess.CompletedProcess([], code, "", err)


class BuildTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.target = Path(self.temp.name) / "focus-guard" / focus_guard.LIBRARY
        self.calls: list[tuple[str, ...]] = []

    def tearDown(self) -> None:
        self.temp.cleanup()

    def run_ok(self, command, **_):
        self.calls.append(tuple(command))
        return done()

    def test_the_source_exists_and_turns_off_the_activation_calls_godot_makes(self) -> None:
        text = focus_guard.SOURCE.read_text(encoding="utf-8")
        for call in ("TransformProcessType", "activateIgnoringOtherApps:", "setActivationPolicy:", "makeKeyAndOrderFront:",
                     "NSApplicationActivationPolicyProhibited", "__interpose"):
            self.assertIn(call, text)

    def test_it_compiles_a_universal_library_and_signs_it(self) -> None:
        library, why = focus_guard.build(self.target, run=self.run_ok, which=lambda name: f"/usr/bin/{name}", platform="darwin")
        self.assertEqual((library, why), (self.target, ""))
        compile_call, sign_call = self.calls
        self.assertEqual(compile_call[:2], ("/usr/bin/clang", "-dynamiclib"))
        self.assertIn("-Werror", compile_call)
        self.assertEqual(compile_call[compile_call.index("-o") + 1], str(self.target))
        self.assertEqual(compile_call[-1], str(focus_guard.SOURCE))
        self.assertEqual([compile_call[i + 1] for i, part in enumerate(compile_call) if part == "-arch"], ["arm64", "x86_64"])
        self.assertEqual(sign_call, ("/usr/bin/codesign", "-s", "-", "-f", str(self.target)))
        self.assertTrue(self.target.parent.is_dir())

    def test_a_system_without_the_guard_or_its_tools_is_a_note_not_an_error(self) -> None:
        library, why = focus_guard.build(self.target, run=self.run_ok, which=lambda name: None, platform="linux")
        self.assertIsNone(library)
        self.assertIn("macOS only", why)
        library, why = focus_guard.build(self.target, run=self.run_ok, which=lambda name: None, platform="darwin")
        self.assertIsNone(library)
        self.assertIn("no clang", why)
        library, why = focus_guard.build(self.target, run=self.run_ok, which=lambda name: "/x", platform="darwin",
                                         source=Path(self.temp.name) / "missing.m")
        self.assertIsNone(library)
        self.assertIn("no guard source", why)
        self.assertEqual(self.calls, [])

    def test_a_failed_compile_or_signature_is_reported_with_its_first_line(self) -> None:
        def failing(command, **_):
            return done(1, "arcanum_nofocus.m:3: error: boom\nmore") if command[1] == "-dynamiclib" else done()
        library, why = focus_guard.build(self.target, run=failing, which=lambda name: f"/usr/bin/{name}", platform="darwin")
        self.assertIsNone(library)
        self.assertEqual(why, "the focus guard build failed: arcanum_nofocus.m:3: error: boom")

        def unsigned(command, **_):
            return done(1, "no identity") if command[1] == "-s" else done()
        self.assertIsNone(focus_guard.build(self.target, run=unsigned, which=lambda name: f"/usr/bin/{name}",
                                            platform="darwin")[0])

        def broken(command, **_):
            raise OSError("exec failed")
        library, why = focus_guard.build(self.target, run=broken, which=lambda name: "/x", platform="darwin")
        self.assertIsNone(library)
        self.assertIn("exec failed", why)


class SmokeWiringTests(unittest.TestCase):
    LIB = Path("/tmp/arcanum-smoke-x/focus-guard") / focus_guard.LIBRARY
    setUp = test_smoke.PlanTests.setUp  # the same fixture roles and settings folder, without its tests
    tearDown = test_smoke.PlanTests.tearDown

    def planned(self, *argv: str, lib: Path | None) -> list[smoke.Step]:
        return smoke.plan(parse(*argv), self.conf, self.prefs, ENVIRON, True, say=self.said.append, focus_lib=lib)

    def test_window_runs_load_the_guard_and_it_is_the_only_new_variable(self) -> None:
        steps = self.planned("pr", "--stages", "cast", "--crash-site", "--no-build", lib=self.LIB)
        self.assertEqual([step.kind for step in steps], ["window", "window"])
        for step in steps:
            command = list(step.command)
            self.assertIn(f"DYLD_INSERT_LIBRARIES={self.LIB}", command)
            self.assertLess(command.index(f"DYLD_INSERT_LIBRARIES={self.LIB}"), command.index(smoke.DEFAULT_GODOT))
            self.assertFalse(any(focus_guard.CARRIER in part for part in command))  # the carrier is not a child variable
        plain = self.planned("pr", "--stages", "cast", "--crash-site", "--no-build", lib=None)
        for guarded, bare in zip(steps, plain):
            rest = [part for part in guarded.command if not part.startswith("DYLD_INSERT_LIBRARIES=")]
            self.assertEqual(rest, list(bare.command))

    def test_an_owners_value_of_the_carrier_never_reaches_a_run(self) -> None:
        self.assertNotIn(focus_guard.CARRIER, focus_guard.carry({focus_guard.CARRIER: "/owner/x.dylib"}, None))
        self.assertEqual(focus_guard.carry({focus_guard.CARRIER: "/owner/x.dylib"}, self.LIB)[focus_guard.CARRIER], str(self.LIB))

    def test_without_a_library_and_in_a_headless_run_nothing_is_loaded(self) -> None:
        for step in self.planned("pr", "--stages", "cast", "--no-build", lib=None):
            self.assertFalse(any(part.startswith("DYLD_") for part in step.command))
        headless = smoke.godot_command("/godot", self.prefs, ["--self-test"], focus_guard.carry(ENVIRON, self.LIB), headless=True)
        self.assertFalse(any(part.startswith("DYLD_") for part in headless))
        windowed = smoke.godot_command("/godot", self.prefs, ["--self-test"], focus_guard.carry(ENVIRON, self.LIB))
        self.assertIn(f"DYLD_INSERT_LIBRARIES={self.LIB}", windowed)

    def test_the_library_is_built_for_window_steps_only_and_a_failure_is_only_a_note(self) -> None:
        window, headless = smoke.Step("w", "window", ("g",)), smoke.Step("h", "headless", ("g",))
        target = self.prefs / "focus-guard" / focus_guard.LIBRARY
        built = mock.Mock(return_value=(self.LIB, ""))
        said: list[str] = []
        self.assertEqual(smoke.focus_guard_library([headless, window], target, built, said.append), self.LIB)
        built.assert_called_once_with(target)
        built.reset_mock()
        self.assertIsNone(smoke.focus_guard_library([headless], target, built, said.append))  # no window run: no build
        self.assertIsNone(smoke.focus_guard_library([window], None, built, said.append))  # --take-focus or off macOS
        built.assert_not_called()
        failed = mock.Mock(return_value=(None, "no clang"))
        self.assertIsNone(smoke.focus_guard_library([window], target, failed, said.append))
        self.assertEqual(said, ["SMOKE note: no clang; the smoke windows may take the focus"])

    def test_the_target_is_in_the_settings_folder_unless_asked_not_to_or_off_macos(self) -> None:
        with mock.patch.object(focus_guard, "supported", return_value=True):
            self.assertEqual(smoke.focus_guard_target(parse("pr", "--stages", "cast"), self.prefs),
                             self.prefs / "focus-guard" / focus_guard.LIBRARY)
            self.assertIsNone(smoke.focus_guard_target(parse("pr", "--stages", "cast", "--take-focus"), self.prefs))
        with mock.patch.object(focus_guard, "supported", return_value=False):
            self.assertIsNone(smoke.focus_guard_target(parse("pr", "--stages", "cast"), self.prefs))

    def test_a_run_plans_with_the_library_and_again_without_it_when_it_cannot_be_built(self) -> None:
        window = smoke.Step("w", "window", ("g",), settings="w")
        plans: list[Path | None] = []

        def fake_plan(args, conf, prefs_root, environ, imported, say=print, focus_lib=None):
            plans.append(focus_lib)
            return [window]
        for built, expected in (((self.LIB, ""), 1), ((None, "no clang"), 2)):
            plans.clear()
            seen: list[object] = []
            with mock.patch.object(focus_guard, "supported", return_value=True), \
                    mock.patch.object(focus_guard, "build", return_value=built), \
                    mock.patch.object(smoke, "plan", fake_plan), mock.patch.object(smoke.config, "load_config", return_value={}), \
                    mock.patch.object(smoke, "execute", lambda steps: seen.append(steps) or 0), \
                    mock.patch.object(smoke.os, "access", return_value=True), mock.patch.object(smoke.os, "chdir"), \
                    mock.patch.dict(smoke.os.environ, {}, clear=False), redirect_stdout(io.StringIO()):
                smoke.os.environ.pop(smoke.run_queue.TICKET_ENV, None)
                self.assertEqual(smoke.run(parse("pr", "--stages", "cast")), 0)
            self.assertEqual(len(plans), expected)
            self.assertIsNotNone(plans[0])
            self.assertEqual(plans[-1] is None, built[0] is None)

    def test_take_focus_is_an_option_of_every_smoke_target(self) -> None:
        for argv in (("pr", "--stages", "cast"), ("full",), ("complete",), ("map", "Arcanum1-024-fixed"), ("crash-site",),
                     ("self-test",)):
            self.assertTrue(parse(*argv, "--take-focus").take_focus, argv)
            self.assertFalse(parse(*argv).take_focus, argv)


if __name__ == "__main__":
    unittest.main()
