"""Public negative controls for preserving a native grid child's failed outcome."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from tools.local_dev import config, launch
from tools.local_dev import test_grid_dev as grid_fixtures


class WorldRunnerFailureTests(unittest.TestCase):
    def test_failed_missing_or_timed_out_grid_never_becomes_input_error_or_pass(self):
        source = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/world_validate.py"
        spec = importlib.util.spec_from_file_location("world_runner_failure_control", source)
        assert spec is not None and spec.loader is not None
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        with tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR")) as owned:
            root = Path(owned)
            oracle = root / "public-reference.json"
            oracle.write_text("{}", encoding="utf-8")
            for child_exit in (0, 1, 124):
                with self.subTest(child_exit=child_exit):
                    output = io.StringIO()
                    argv = [str(source), "--candidate-root", str(root), "--task", "ARC-WORLD-003", "--kind", "grid-original"]
                    with patch.object(sys, "argv", argv), patch.object(sys, "path", sys.path.copy()), \
                         patch.dict(os.environ, {"TMPDIR": owned, "RENDER006_HUMAN_ORACLE": str(oracle)}), \
                         patch.object(config, "load_config", return_value={"inputs": {}}), \
                         patch.object(launch, "terrain_arguments", return_value=["--mode", "world-diagnostic"]), \
                         patch.object(runner.subprocess, "run", return_value=SimpleNamespace(stdout="b" * 40)), \
                         patch.object(runner, "run_child", return_value=(child_exit,
                             "PRIVATE-SENTINEL /not/a/public/path\n"
                             "ARC-WORLD-003 observation-failure=GRID_OBSERVATION_COMPLETE_CLOCK\n")), \
                         contextlib.redirect_stdout(output):
                        result = runner.main()
                    self.assertEqual(1, result)
                    report = json.loads(output.getvalue())
                    self.assertEqual("FAIL", report["outcome"])
                    self.assertEqual(child_exit, report["exitCode"])
                    self.assertEqual("child-timeout" if child_exit == 124 else "child-outcome", report["failureStage"])
                    self.assertEqual("grid-original", report["kind"])
                    self.assertEqual("source", report["host"])
                    self.assertEqual("b" * 40, report["candidateCommit"])
                    self.assertEqual(["ARC-WORLD-003 observation-failure"], report["diagnostics"])
                    self.assertNotIn("PRIVATE-SENTINEL", output.getvalue())
                    self.assertNotIn("environment-or-input", output.getvalue())

    def test_grid_report_stage_is_fail_closed_and_never_echoes_untrusted_report(self):
        source = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/world_validate.py"
        spec = importlib.util.spec_from_file_location("world_runner_report_control", source)
        assert spec is not None and spec.loader is not None
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        good = grid_fixtures.GridReportTests().sample()
        good["candidateCommit"] = "b" * 40
        marker = "ARC-WORLD-001 native-smoke PASS fingerprints=immutable rendered=PASS\nARC-RENDER-008 original overlay PASS {}\n"
        encoded = "ARC-WORLD-003 grid-readback " + json.dumps(good) + "\n"
        controls = [(marker, "report-count"), (marker + encoded + encoded, "report-count"),
                    (marker + 'ARC-WORLD-003 grid-readback {PRIVATE-SENTINEL\n', "report-json"),
                    (marker + 'ARC-WORLD-003 grid-readback {"private":"PRIVATE-SENTINEL /not/public"}\n', "report-contract"),
                    (marker + encoded, None)]
        controls.extend((marker + 'ARC-WORLD-003 grid-readback ' + json.dumps(value) + '\n', 'report-contract')
                        for value in (None, [], 1, 'PRIVATE-SENTINEL', {**good, 'cases': [{"name": []}] * 15}))
        with tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR")) as owned:
            oracle = Path(owned) / "public-reference.json"
            oracle.write_text("{}", encoding="utf-8")
            for child_output, expected in controls:
                with self.subTest(stage=expected):
                    output = io.StringIO()
                    argv = [str(source), "--candidate-root", owned, "--task", "ARC-WORLD-003", "--kind", "grid-original"]
                    with patch.object(sys, "argv", argv), patch.object(sys, "path", sys.path.copy()), \
                         patch.dict(os.environ, {"TMPDIR": owned, "RENDER006_HUMAN_ORACLE": str(oracle)}), \
                         patch.object(config, "load_config", return_value={"inputs": {}}), \
                         patch.object(launch, "terrain_arguments", return_value=["--mode", "world-diagnostic"]), \
                         patch.object(runner.subprocess, "run", return_value=SimpleNamespace(stdout="b" * 40)), \
                         patch.object(runner, "run_child", return_value=(0, child_output)), \
                         contextlib.redirect_stdout(output):
                        result = runner.main()
                    report = json.loads(output.getvalue())
                    self.assertEqual(1 if expected else 0, result)
                    self.assertEqual("FAIL" if expected else "PASS", report["outcome"])
                    self.assertEqual(0, report["exitCode"])
                    self.assertEqual(expected, report.get("failureStage"))
                    self.assertNotIn("PRIVATE-SENTINEL", output.getvalue())
                    self.assertNotIn("environment-or-input", output.getvalue())

    def test_real_timed_out_child_retains_output_but_never_returns_success(self):
        source = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/world_validate.py"
        spec = importlib.util.spec_from_file_location("world_runner_timeout_control", source)
        assert spec is not None and spec.loader is not None
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        with tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR")) as owned:
            child = Path(owned) / "child.py"
            child.write_text('import time\nprint("ARC-WORLD-003 observation-failure=PUBLIC_TIMEOUT_CONTROL", flush=True)\ntime.sleep(10)\n', encoding="utf-8")
            code, output = runner.run_child([sys.executable, str(child)], dict(os.environ), 1)
            self.assertEqual(124, code)
            self.assertIn("ARC-WORLD-003 observation-failure=PUBLIC_TIMEOUT_CONTROL", output)


class WorldRunnerStageTests(unittest.TestCase):
    def setUp(self):
        self.owned = tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR"))
        self.addCleanup(self.owned.cleanup)
        self.root = Path(self.owned.name)
        self.oracle = self.root / "public-reference.json"
        self.oracle.write_text("{}", encoding="utf-8")
        self.app = self.root / "synthetic-app"
        self.app.write_text("public", encoding="utf-8")
        source = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/world_validate.py"
        spec = importlib.util.spec_from_file_location("world_runner_stage_control", source)
        assert spec and spec.loader
        self.runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runner)
        good = grid_fixtures.GridReportTests().sample()
        good["candidateCommit"] = "b" * 40
        self.good = good

    def invoke(self, change=None, exported=False, child_exit=0, child_output=None):
        output = io.StringIO()
        argv = ["world_validate.py", "--candidate-root", str(self.root), "--task", "ARC-WORLD-003", "--kind", "grid-original"]
        good = {**self.good, "host": "exported" if exported else "source"}
        if exported:
            argv += ["--app", str(self.app)]
        if child_output is None:
            child_output = ("ARC-WORLD-001 native-smoke PASS fingerprints=immutable rendered=PASS\n"
                            "ARC-RENDER-008 original overlay PASS {}\n"
                            "ARC-WORLD-003 grid-readback " + json.dumps(good) + "\n")
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(sys, "argv", argv))
            stack.enter_context(patch.object(sys, "path", sys.path.copy()))
            stack.enter_context(patch.dict(os.environ, {"TMPDIR": self.owned.name, "RENDER006_HUMAN_ORACLE": str(self.oracle)}))
            stack.enter_context(patch.object(config, "load_config", return_value={"inputs": {}}))
            stack.enter_context(patch.object(launch, "terrain_arguments", return_value=["--mode", "world-diagnostic"]))
            stack.enter_context(patch.object(self.runner.subprocess, "run", return_value=SimpleNamespace(stdout="b" * 40)))
            stack.enter_context(patch.object(self.runner, "run_child", return_value=(child_exit, child_output)))
            if change:
                change(stack)
            stack.enter_context(contextlib.redirect_stdout(output))
            result = self.runner.main()
        self.assertNotIn("PRIVATE-SENTINEL", output.getvalue())
        self.assertNotIn("/private/input", output.getvalue())
        self.assertEqual(1, len(output.getvalue().splitlines()))
        self.assertEqual("{}", self.oracle.read_text())
        return result, json.loads(output.getvalue())

    def test_setup_and_launch_failures_are_staged_without_inventing_child_exit(self):
        r = self.runner
        controls = [
            ("root-resolution", lambda s: s.enter_context(patch.object(Path, "resolve", side_effect=OSError(2, "PRIVATE-SENTINEL")))),
            ("owned-temp", lambda s: s.enter_context(patch.object(r.tempfile, "TemporaryDirectory", side_effect=OSError(13, "PRIVATE-SENTINEL")))),
            ("git-head", lambda s: s.enter_context(patch.object(r.subprocess, "run", side_effect=r.subprocess.CalledProcessError(7, ["PRIVATE-SENTINEL"], output="/private/input")))),
            ("private-reference-admission", lambda s: s.enter_context(patch.object(config, "regular_file", side_effect=ValueError("PRIVATE-SENTINEL")))),
            ("config-roles", lambda s: s.enter_context(patch.object(config, "load_config", side_effect=ValueError("malformed PRIVATE-SENTINEL")))),
            ("config-roles", lambda s: s.enter_context(patch.object(launch, "terrain_arguments", side_effect=ValueError("missing/unsafe PRIVATE-SENTINEL")))),
            ("child-launch", lambda s: s.enter_context(patch.object(r, "run_child", side_effect=OSError(2, "PRIVATE-SENTINEL")))),
            ("child-spawn", lambda s: s.enter_context(patch.object(r, "run_child", side_effect=r.ChildFailure("child-spawn", OSError(2, "PRIVATE-SENTINEL"), None)))),
            ("child-communication", lambda s: s.enter_context(patch.object(r, "run_child", side_effect=r.ChildFailure("child-communication", OSError(5, "PRIVATE-SENTINEL"), -9)))),
            ("child-termination", lambda s: s.enter_context(patch.object(r, "run_child", side_effect=r.ChildFailure("child-termination", OSError(3, "PRIVATE-SENTINEL"), -15)))),
        ]
        for stage, change in controls:
            with self.subTest(stage=stage):
                code, report = self.invoke(change)
                self.assertEqual(2, code)
                self.assertEqual("FAIL", report["outcome"])
                self.assertEqual(stage, report["failureStage"])
                self.assertEqual(-9 if stage == "child-communication" else -15 if stage == "child-termination" else None, report["exitCode"])
        code, report = self.invoke(lambda s: s.enter_context(patch.object(sys, "argv", ["runner", "--PRIVATE-SENTINEL", "/private/input"])))
        self.assertEqual((2, "argument-policy"), (code, report["failureStage"]))
        self.app.unlink()
        code, report = self.invoke(exported=True)
        self.assertEqual((2, "executable-resolution", None), (code, report["failureStage"], report["exitCode"]))
        self.oracle.unlink()
        # Invoke owns no reference mutation; restore the fixture after missing-input check.
        with patch.object(Path, "read_text", return_value="{}"):
            code, report = self.invoke()
        self.assertEqual((2, "private-reference-admission"), (code, report["failureStage"]))

    def test_cleanup_rejects_pass_and_preserves_primary_fail_for_both_hosts(self):
        for exported in (False, True):
            for child_exit in (0, 7):
                with self.subTest(exported=exported, child_exit=child_exit):
                    original = self.runner.tempfile.TemporaryDirectory.cleanup
                    def cleanup(owned):
                        original(owned)
                        raise OSError(13, "PRIVATE-SENTINEL /private/input")
                    code, report = self.invoke(lambda s: s.enter_context(patch.object(self.runner.tempfile.TemporaryDirectory, "cleanup", cleanup)), exported, child_exit)
                    self.assertEqual(2 if child_exit == 0 else 1, code)
                    self.assertEqual("FAIL", report["outcome"])
                    self.assertEqual(child_exit, report["exitCode"])
                    self.assertEqual("cleanup" if child_exit == 0 else "child-outcome", report["failureStage"])
                    self.assertEqual([{"exceptionClass": "OSError", "errno": 13}], report["cleanupFailures"])
                    self.assertEqual("exported" if exported else "source", report["host"])

    def test_native_categories_project_no_arbitrary_tail_or_report(self):
        code, report = self.invoke(child_exit=1, child_output=(
            "ARC-WORLD-001 native GUI assertion FAIL PRIVATE-SENTINEL /private/input\n"
            "ARC-RENDER-004 native-smoke failure mode=PRIVATE-SENTINEL\n"
            "ARC-WORLD-003 observation-failure=PRIVATE-SENTINEL\n"
            "ARC-RENDER-006 human sample PRIVATE-SENTINEL pixels=/private/input\n"
            "ARC-WORLD-003 grid-readback PRIVATE-SENTINEL\nPRIVATE-SENTINEL\n"))
        self.assertEqual(1, code)
        self.assertEqual(["ARC-WORLD-001 native GUI assertion FAIL", "ARC-RENDER-004 native-smoke failure",
                          "ARC-WORLD-003 observation-failure", "ARC-RENDER-006 human sample"], report["diagnostics"])

    def test_actual_child_spawn_communication_kill_and_signal_error_paths(self):
        r = self.runner
        with patch.object(r.subprocess, "Popen", side_effect=OSError(2, "PRIVATE-SENTINEL")):
            with self.assertRaises(r.ChildFailure) as caught:
                r.run_child(["public"], {}, 1)
        self.assertEqual(("child-spawn", None), (caught.exception.stage, caught.exception.child_exit))
        child = Mock(pid=42, returncode=-9)
        child.poll.return_value = None
        for mode in ("communication", "kill", "signal-error"):
            with self.subTest(mode=mode):
                child.reset_mock()
                if mode == "communication":
                    child.communicate.side_effect = [OSError(5, "PRIVATE-SENTINEL"), ("public", None)]
                else:
                    child.communicate.side_effect = [r.subprocess.TimeoutExpired("PRIVATE-SENTINEL", 1),
                        r.subprocess.TimeoutExpired("PRIVATE-SENTINEL", 5), ("flushed public", None)]
                with patch.object(r.subprocess, "Popen", return_value=child), \
                     patch.object(r.os, "killpg", side_effect=OSError(3, "PRIVATE-SENTINEL") if mode == "signal-error" else None) as kill:
                    if mode == "kill":
                        self.assertEqual((124, "flushed public"), r.run_child(["public"], {}, 1))
                        self.assertEqual([((42, r.signal.SIGTERM),), ((42, r.signal.SIGKILL),)], [(c.args,) for c in kill.call_args_list])
                    else:
                        with self.assertRaises(r.ChildFailure) as caught:
                            r.run_child(["public"], {}, 1)
                        self.assertEqual("child-communication" if mode == "communication" else "child-termination", caught.exception.stage)
                        self.assertEqual(-9, caught.exception.child_exit)
                        if mode == "signal-error":
                            self.assertEqual({"exceptionClass": "OSError", "errno": 3}, caught.exception.reap_failure)


class WorldRunnerRawLogTests(unittest.TestCase):
    """ARC-DEV-043: the raw Godot output of a no-original-data validation lands in an owner-only evidence file."""

    def setUp(self):
        self.owned = tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR"))
        self.addCleanup(self.owned.cleanup)
        self.root = Path(self.owned.name)
        source = Path(__file__).resolve().parents[1] / "terrain-viewer/Godot/tools/world_validate.py"
        spec = importlib.util.spec_from_file_location("world_runner_raw_log_control", source)
        assert spec and spec.loader
        self.runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runner)

    def invoke(self, kind, raw_log, child):
        output = io.StringIO()
        argv = ["world_validate.py", "--candidate-root", str(self.root), "--kind", kind, "--raw-log", str(raw_log)]
        with patch.object(sys, "argv", argv), patch.object(sys, "path", sys.path.copy()), \
             patch.dict(os.environ, {"TMPDIR": self.owned.name}), \
             patch.object(self.runner.subprocess, "run", return_value=SimpleNamespace(stdout="b" * 40)), \
             patch.object(self.runner, "run_child", side_effect=child) as run, contextlib.redirect_stdout(output):
            code = self.runner.main()
        return code, json.loads(output.getvalue()), run

    def test_synthetic_child_output_is_framed_in_an_owner_only_file_and_summary_stays_closed(self):
        raw = self.root / "source-graphical.godot.log"
        stage = "ARC-DEV-043 self-test stage AreaHover begin t=9000ms can-draw=False focused=False"
        code, report, _ = self.invoke("synthetic", raw, lambda argv, env, timeout: (124, f"PRIVATE-SENTINEL\n{stage}"))
        self.assertEqual((1, "FAIL", 124), (code, report["outcome"], report["exitCode"]))
        self.assertNotIn("PRIVATE-SENTINEL", json.dumps(report))
        lines = raw.read_text(encoding="utf-8").splitlines()
        self.assertEqual("world-validate child self-test start timeout=60s", lines[0])
        self.assertEqual(["PRIVATE-SENTINEL", stage], lines[1:3])
        self.assertRegex(lines[3], r"^world-validate child self-test timeout code=124 elapsed=\d+\.\d{3}s$")
        self.assertEqual(4, len(lines))
        self.assertEqual(0o600, raw.stat().st_mode & 0o777)

    def test_persistence_frames_every_settings_child_in_order(self):
        raw = self.root / "source-world-persistence.godot.log"

        def child(argv, env, timeout):
            stage = argv[-1]
            prefix = "ARC-GPAD-004" if stage.startswith("zoom-") else "ARC-WORLD-001"
            return 0, f"{prefix} settings-test PASS {stage}\n"
        code, report, run = self.invoke("persistence", raw, child)
        self.assertEqual((0, "PASS"), (code, report["outcome"]))
        starts = [line for line in raw.read_text(encoding="utf-8").splitlines() if line.endswith(" start timeout=30s")]
        self.assertEqual(run.call_count, len(starts))
        self.assertEqual("world-validate child settings-test first-launch start timeout=30s", starts[0])
        self.assertIn("world-validate child settings-test zoom-malformed-11 start timeout=30s", starts)
        self.assertEqual("world-validate child settings-test zoom-failure start timeout=30s", starts[-1])

    def test_original_kinds_and_an_existing_file_are_refused_before_any_child(self):
        for kind in ("original", "human", "human-metadata", "grid-original"):
            with self.subTest(kind=kind):
                raw = self.root / f"{kind}.godot.log"
                code, report, run = self.invoke(kind, raw, lambda argv, env, timeout: (0, ""))
                self.assertEqual((2, "argument-policy", "ValueError"), (code, report["failureStage"], report["exceptionClass"]))
                self.assertFalse(raw.exists())
                run.assert_not_called()
        raw = self.root / "kept.godot.log"
        raw.write_text("earlier evidence\n", encoding="utf-8")
        code, report, run = self.invoke("synthetic", raw, lambda argv, env, timeout: (0, ""))
        self.assertEqual((2, "raw-log", "OSError"), (code, report["failureStage"], report["exceptionClass"]))
        self.assertEqual("earlier evidence\n", raw.read_text(encoding="utf-8"))
        run.assert_not_called()

    def test_without_the_flag_no_file_is_written(self):
        output = io.StringIO()
        argv = ["world_validate.py", "--candidate-root", str(self.root), "--kind", "synthetic"]
        before = set(self.root.iterdir())
        with patch.object(sys, "argv", argv), patch.object(sys, "path", sys.path.copy()), \
             patch.dict(os.environ, {"TMPDIR": self.owned.name}), \
             patch.object(self.runner.subprocess, "run", return_value=SimpleNamespace(stdout="b" * 40)), \
             patch.object(self.runner, "run_child", return_value=(1, "")), contextlib.redirect_stdout(output):
            self.assertEqual(1, self.runner.main())
        self.assertEqual(before, set(self.root.iterdir()))


if __name__ == "__main__":
    unittest.main()
