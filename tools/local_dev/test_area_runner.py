"""Public synthetic controls for RENDER007 runner orchestration.

The native process and original comparator are explicit mock boundaries. These
checks prove routing/refusal/receipt handling, NOT original coverage or pixels.
"""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
from typing import Any
import unittest
from unittest.mock import patch

from tools.local_dev import area_validation, config, launch


RESOURCES = dict(Outcome='PASS', SweepSteps=120, SweepCommands=37363, MaxVisible=900, MaxGpuFrames=400,
    MaxGpuBytes=9000000, Reuploads=800, Evictions=700, CpuFrames=2444, CpuBytes=81000000,
    LoadMilliseconds=[1, 2], ManagedBytes=[20, 20], ResidentBytes=[20, 21], Handles=[9, 9],
    CancelledGenerationPublished=False, RetiredGenerationCollected=True)


class AreaRunnerTests(unittest.TestCase):
    def setUp(self):
        self.source = Path(__file__).resolve().parents[1] / 'terrain-viewer/Godot/tools/area_validate.py'
        tools = self.source.parent
        with patch.object(sys, 'path', [str(tools), *sys.path]):
            spec = importlib.util.spec_from_file_location('area_runner_control', self.source)
            assert spec is not None and spec.loader is not None
            self.runner = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(self.runner)

    def exercise(self, *, exported=False, code=0, output=None, coverage='valid',
                 comparator_error=None, mutate_candidate=False, timeout=60, unsafe_private=False, task=None):
        marker = 'ARC-RENDER-007 native-smoke source-coverage and sampled-pixels PASS '
        output = output if output is not None else marker + 'PUBLIC-SYNTHETIC\n'
        with tempfile.TemporaryDirectory() as owned:
            root = Path(owned).resolve()
            private = root / ('not-local' if unsafe_private else 'local') / 'evidence'
            private.mkdir(parents=True)
            oracle = root / 'synthetic-oracle.json'
            oracle.write_text('{}')
            app = root / 'synthetic-executable'
            app.write_text('not executable: child process is mocked')
            head = 'a' * 40
            before = (head, {'synthetic.cs': 'b' * 64})
            after = (head, {'synthetic.cs': 'c' * 64}) if mutate_candidate else before
            argv = [str(self.source), '--candidate-root', str(root), '--timeout', str(timeout)]
            if exported:
                argv += ['--app', str(app)]
            if task is not None:
                argv += ['--task', task]
            report = {'publicSynthetic': True}
            result = dict(oracleSha256=area_validation.ORACLE_SHA256,
                originalOccurrenceCoverage='PASS', originalAppearanceCoverage='PASS', drawCoverage='PASS',
                coreTerrain=36864, coreBodies=499, notApplicable=15, submitted=37363)
            invocation = {}

            def child(args, env, limit):
                invocation.update(argv=args, env=env, timeout=limit)
                path = Path(env['ARC_AREA_REPORT'])
                if coverage == 'valid':
                    path.write_text(json.dumps(report))
                    Path(str(path) + '.resources.json').write_text(json.dumps(RESOURCES))
                elif coverage == 'partial':
                    path.write_text('{')
                elif coverage != 'missing':
                    raise AssertionError('unknown fixture')
                return code, output

            captured = io.StringIO()
            with patch.object(sys, 'argv', argv), patch.object(sys, 'path', sys.path.copy()), \
                 patch.dict(os.environ, ARC_AREA_ORACLE=str(oracle), ARC_AREA_PRIVATE_EVIDENCE=str(private)), \
                 patch.object(self.runner, 'candidate_state', side_effect=[before, after]), \
                 patch.object(self.runner.subprocess, 'run', return_value=SimpleNamespace(returncode=0)) as ignored, \
                 patch.object(config, 'load_config', return_value={'inputs': {'synthetic': True}}), \
                 patch.object(launch, 'terrain_arguments', return_value=['--mode', 'crash-site-area']) as arguments, \
                 patch.object(self.runner, 'run_child', side_effect=child) as native, \
                 patch.object(area_validation, 'compare_original', return_value=result, side_effect=comparator_error) as compare, \
                 contextlib.redirect_stdout(captured):
                exit_code = self.runner.main()
            receipt = json.loads(captured.getvalue())
            if invocation:
                evidence = Path(invocation['env']['ARC_AREA_REPORT']).parent
                self.assertTrue(evidence.is_relative_to(root / 'local'))
                self.assertEqual(output, (evidence / 'native.log').read_text())
                failure = (evidence / 'failure.txt').read_text() if (evidence / 'failure.txt').exists() else None
            else:
                failure = None
            return dict(exit=exit_code, receipt=receipt, invocation=invocation, failure=failure,
                comparator_calls=compare.call_count, native_calls=native.call_count,
                arguments_calls=arguments.call_args_list, ignored_calls=ignored.call_args_list)

    def test_source_and_exported_execute_same_closed_route_and_validate_safe_receipt(self):
        for exported in (False, True):
            with self.subTest(exported=exported):
                actual = self.exercise(exported=exported)
                self.assertEqual(0, actual['exit'])
                host = 'exported' if exported else 'source'
                area_validation.validate_report(actual['receipt'], 'a' * 40, host)
                self.assertEqual(1, actual['comparator_calls'])
                invocation = actual['invocation']
                self.assertEqual(60, invocation['timeout'])
                self.assertEqual(['--', '--native-smoke', '--mode', 'crash-site-area'], invocation['argv'][-4:])
                self.assertEqual(1, invocation['argv'].count('--'))
                self.assertEqual({'synthetic': True}, actual['arguments_calls'][0].args[0])
                self.assertEqual('crash-site-area', actual['arguments_calls'][0].args[1])
                self.assertEqual('git', actual['ignored_calls'][0].args[0][0])
                env = invocation['env']
                self.assertNotEqual(env['WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH'], env['WORLD_DIAGNOSTIC_ZOOM_TEST_PATH'])
                self.assertTrue(env['ARC_AREA_REPORT'].endswith('/coverage.json'))
                self.assertEqual(host, actual['receipt']['host'])
                self.assertIsNone(actual['failure'])
                if exported:
                    self.assertEqual(5, len(invocation['argv']))
                else:
                    self.assertEqual('/Applications/Godot_mono.app/Contents/MacOS/Godot', invocation['argv'][0])
                    self.assertIn('--path', invocation['argv'])
                encoded = json.dumps(actual['receipt'])
                self.assertNotIn('PUBLIC-SYNTHETIC', encoded)
                self.assertNotIn('synthetic-oracle', encoded)
                self.assertNotIn('native.log', encoded)

    def test_cave_task_passes_its_task_to_the_launcher_and_requires_the_transition_marker(self):
        marker = 'ARC-RENDER-007 native-smoke source-coverage and sampled-pixels PASS PUBLIC-SYNTHETIC\n'
        missing = self.exercise(task='ARC-WORLD-009', output=marker)
        self.assertEqual((1, 'native-observation', 0), (missing['exit'], missing['receipt']['failureStage'], missing['comparator_calls']))
        self.assertIn('cave transition outcome', missing['failure'])
        self.assertEqual('ARC-WORLD-009', missing['arguments_calls'][0].kwargs['task'])
        passed = self.exercise(task='ARC-WORLD-009', output=marker + 'ARC-WORLD-009 native-smoke transition PASS []\n')
        self.assertEqual((0, 'PASS'), (passed['exit'], passed['receipt']['caveTransition']))
        # The receipt binds the cave smoke to its export task, and only to it.
        area_validation.validate_report(passed['receipt'], 'a' * 40, 'source', 'ARC-WORLD-009')
        with self.assertRaises(ValueError):
            area_validation.validate_report(passed['receipt'], 'a' * 40, 'source')
        with self.assertRaises(ValueError):
            area_validation.validate_report(self.exercise()['receipt'], 'a' * 40, 'source', 'ARC-WORLD-009')
        self.assertEqual('ARC-RENDER-007', self.exercise()['arguments_calls'][0].kwargs['task'])

    def test_failed_timeout_missing_marker_and_engine_errors_refuse_before_comparison(self):
        cases: tuple[dict[str, Any], ...] = ({'code': 1}, {'code': 124}, {'output': 'no marker\n'},
            {'output': 'ARC-RENDER-007 native-smoke source-coverage and sampled-pixels PASS \nERROR: private sentinel\n'})
        for kwargs in cases:
            with self.subTest(kwargs=kwargs):
                actual = self.exercise(**kwargs)
                self.assertEqual(1, actual['exit'])
                self.assertEqual('native-observation', actual['receipt']['failureStage'])
                self.assertEqual(0, actual['comparator_calls'])
                self.assertIn('native outcome', actual['failure'])
                self.assertNotIn('private sentinel', json.dumps(actual['receipt']))

    def test_missing_partial_and_rejected_original_reports_never_pass(self):
        cases: tuple[dict[str, Any], ...] = ({'coverage': 'missing'}, {'coverage': 'partial'},
                 {'comparator_error': ValueError('PRIVATE source-selected association')})
        for kwargs in cases:
            with self.subTest(kwargs=kwargs):
                actual = self.exercise(**kwargs)
                self.assertEqual(1, actual['exit'])
                self.assertEqual('FAIL', actual['receipt']['outcome'])
                self.assertEqual('original-reconciliation', actual['receipt']['failureStage'])
                self.assertIsNotNone(actual['failure'])
                self.assertNotIn('PRIVATE', json.dumps(actual['receipt']))
                self.assertNotIn('coverageSha256', actual['receipt'])

    def test_source_mutation_after_native_success_refuses_safe_pass(self):
        actual = self.exercise(mutate_candidate=True)
        self.assertEqual(1, actual['exit'])
        self.assertEqual(1, actual['comparator_calls'])
        self.assertIn('candidate changed', actual['failure'])
        self.assertEqual('FAIL', actual['receipt']['outcome'])
        self.assertNotIn('drawCoverage', actual['receipt'])

    def test_timeout_and_private_output_policy_fail_before_native_process(self):
        cases: tuple[dict[str, Any], ...] = ({'timeout': 0}, {'timeout': 901}, {'unsafe_private': True})
        for kwargs in cases:
            with self.subTest(kwargs=kwargs):
                actual = self.exercise(**kwargs)
                self.assertEqual(1, actual['exit'])
                self.assertEqual('input-policy', actual['receipt']['failureStage'])
                self.assertEqual(0, actual['native_calls'])
                self.assertEqual(0, actual['comparator_calls'])


if __name__ == '__main__':
    unittest.main()
