"""Synthetic policy fixtures; never export or GUI acceptance evidence."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from . import gamepad_export_gates as gates

HEAD = 'a' * 40


class GamepadGateTests(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.TemporaryDirectory(prefix='gamepad-gate-unit-', dir=os.environ.get('TMPDIR'))
        self.addCleanup(self.work.cleanup)
        self.evidence = Path(self.work.name)

    def fixtures(self):
        for gate, name in gates.GATES.items():
            log = b'UNIT FIXTURE, NOT EXPORT EVIDENCE\n'
            (self.evidence / name).write_bytes(log)
            value = dict(schema='arcanum-gamepad-gate-result-v1', task='ARC-GPAD-005', candidateCommit=HEAD,
                         gate=gate, log=name, outcome='PASS', exitCode=0,
                         startedAt='2026-01-01T00:00:00+00:00', finishedAt='2026-01-01T00:00:01+00:00',
                         logBytes=len(log), logSha256=hashlib.sha256(log).hexdigest())
            (self.evidence / (name + '.result.json')).write_text(json.dumps(value))

    def mutate(self, key, value):
        self.fixtures()
        path = self.evidence / 'sdk-info.log.result.json'
        receipt = json.loads(path.read_text()); receipt[key] = value
        path.write_text(json.dumps(receipt))
        with self.assertRaises(ValueError):
            gates.validate_exit_receipts(self.evidence, HEAD)

    def test_complete_fixture_receipts(self):
        self.fixtures()
        self.assertEqual(len(gates.validate_exit_receipts(self.evidence, HEAD)), len(gates.GATES) * 2)

    def test_failure_rejected(self): self.mutate('exitCode', 1)
    def test_boolean_exit_rejected(self): self.mutate('exitCode', False)
    def test_other_head_rejected(self): self.mutate('candidateCommit', 'b' * 40)
    def test_other_task_rejected(self): self.mutate('task', 'ARC-GPAD-003')
    def test_other_gate_rejected(self): self.mutate('gate', 'export')
    def test_reversed_time_rejected(self): self.mutate('finishedAt', '2025-01-01T00:00:00+00:00')
    def test_naive_time_rejected(self): self.mutate('startedAt', '2026-01-01T00:00:00')
    def test_changed_digest_rejected(self): self.mutate('logSha256', '0' * 64)
    def test_wrong_byte_count_rejected(self): self.mutate('logBytes', 1)

    def test_missing_and_symlink_rejected(self):
        self.fixtures()
        path = self.evidence / 'export.log'; path.unlink()
        with self.assertRaises(ValueError): gates.validate_exit_receipts(self.evidence, HEAD)
        path.symlink_to(self.evidence / 'sdk-info.log')
        with self.assertRaises(ValueError): gates.validate_exit_receipts(self.evidence, HEAD)

    def test_actual_command_receipt(self):
        self.assertEqual(gates.run_gate(self.evidence, HEAD, 'sdk-info.log', [sys.executable, '-c', 'print("actual fixture command")']), 0)
        row = json.loads((self.evidence / 'sdk-info.log.result.json').read_text())
        self.assertEqual(row['logSha256'], hashlib.sha256((self.evidence / 'sdk-info.log').read_bytes()).hexdigest())
        with self.assertRaises(ValueError): gates.run_gate(self.evidence, HEAD, 'sdk-info.log', [sys.executable, '-c', 'print("overwrite")'])

    def test_actual_failure_receipt(self):
        self.assertEqual(gates.run_gate(self.evidence, HEAD, 'export.log', [sys.executable, '-c', 'print("actual fixture failure");raise SystemExit(7)']), 7)
        row = json.loads((self.evidence / 'export.log.result.json').read_text())
        self.assertEqual((row['outcome'], row['exitCode']), ('FAIL', 7))

    def test_engine_error_zero_exit_fails_closed(self):
        self.assertEqual(gates.run_gate(self.evidence, HEAD, 'godot-self-test.log',
            [sys.executable, '-c', 'print("ERROR: native failure despite process zero")']), 1)
        row = json.loads((self.evidence / 'godot-self-test.log.result.json').read_text())
        self.assertEqual((row['outcome'], row['exitCode'], row['engineError']), ('FAIL', 0, True))

    def test_rehashed_engine_error_pass_receipt_rejected(self):
        self.fixtures()
        path = self.evidence / 'exported-smoke.log'
        path.write_bytes(b'ERROR: native failure despite claimed PASS\n')
        receipt_path = path.with_name(path.name + '.result.json')
        row = json.loads(receipt_path.read_text())
        row.update(logBytes=path.stat().st_size, logSha256=hashlib.sha256(path.read_bytes()).hexdigest())
        receipt_path.write_text(json.dumps(row))
        with self.assertRaisesRegex(ValueError, 'failed or unbound'):
            gates.validate_exit_receipts(self.evidence, HEAD)

    def test_evidence_report_limit_and_malformed(self):
        path = self.evidence / 'report.json'
        path.write_text('{')
        with self.assertRaisesRegex(ValueError, 'malformed'): gates.json_authority(path)
        path.write_text('[]')
        with self.assertRaisesRegex(ValueError, 'object'): gates.json_authority(path)
        from unittest.mock import patch
        with patch.object(gates.config, 'optional_authority_bytes', return_value=b' ' * (128 * 1024 * 1024 + 1)):
            with self.assertRaisesRegex(ValueError, 'excessive'): gates.json_authority(path)

    def test_unknown_gate_and_head(self):
        for name, head in [('other.log', HEAD), ('export.log', HEAD + '0')]:
            with self.assertRaises(ValueError): gates.run_gate(self.evidence, head, name, [sys.executable, '-c', 'print(1)'])
        self.assertEqual(list(self.evidence.iterdir()), [])

    def witnesses(self) -> dict:
        data = json.dumps({'row': 'literal adverse geometry', 'width': -1, 'frame': 22})
        digest = hashlib.sha256(data.encode()).hexdigest()
        return dict(witnessSchema='arcanum-gamepad-geometry-witnesses-v1', witnesses={digest: data},
                    tuples=[dict(labelGeometry={'$witness': digest}, settlement=[], scrollObservations=[])])

    def test_lossless_witness_digest_binding(self):
        row = self.witnesses()
        gates.validate_witnesses(row)
        digest = next(iter(row['witnesses']))
        row['witnesses'][digest] += ' '
        with self.assertRaisesRegex(ValueError, 'digest'): gates.validate_witnesses(row)

    def test_missing_witness_and_observation_rejected(self):
        row = self.witnesses(); row['tuples'][0]['labelGeometry']['$witness'] = '0' * 64
        with self.assertRaisesRegex(ValueError, 'missing'): gates.validate_witnesses(row)
        row = self.witnesses(); del row['tuples'][0]['settlement']
        with self.assertRaisesRegex(ValueError, 'mandatory'): gates.validate_witnesses(row)

    def test_orphan_or_malformed_witness_rejected(self):
        row = self.witnesses(); row['tuples'][0]['labelGeometry'] = {}
        with self.assertRaisesRegex(ValueError, 'orphan'): gates.validate_witnesses(row)
        row = self.witnesses(); data = '{'; digest = hashlib.sha256(data.encode()).hexdigest()
        row['witnesses'] = {digest: data}
        with self.assertRaisesRegex(ValueError, 'malformed'): gates.validate_witnesses(row)

    def test_incomplete_or_headless_matrix(self):
        row: dict = dict(schema='arcanum-gamepad-lab-matrix-v1', task='ARC-GPAD-005', status='passed', candidateCommit=HEAD,
                   origin='source', isolatedTestStorage=True, backend='macos', coverage='full-tuples/O5-coverage-audit-still-required')
        path = self.evidence / 'source-preset-matrix.json'
        path.write_text(json.dumps(row))
        with self.assertRaisesRegex(ValueError, 'incomplete'): gates.validate_matrix(self.evidence, 'source', HEAD)
        row.update(coverage='full-contract-o5', backend='headless'); path.write_text(json.dumps(row))
        with self.assertRaises(ValueError): gates.validate_matrix(self.evidence, 'source', HEAD)
        row.update(backend='macos', tuples=[]); path.write_text(json.dumps(row))
        with self.assertRaisesRegex(ValueError, 'tuple'): gates.validate_matrix(self.evidence, 'source', HEAD)

    def test_capture_traversal_rejected(self):
        for name in ('../bad.png', '/bad.png', 'x/../bad.png', 'bad.txt'):
            with self.assertRaises(ValueError): gates.capture(self.evidence, 'source-images', name, '0' * 64, [1280, 800])

    def test_persistence_missing_restart_rejected(self):
        row = dict(schema='arcanum-gamepad-persistence-suite-v1', task='ARC-GPAD-005', candidateCommit=HEAD,
                   status='passed', isolatedTestStorage=True, legacyUnchanged=True, operations=[])
        (self.evidence / 'source-preset-persistence.json').write_text(json.dumps(row))
        with self.assertRaisesRegex(ValueError, 'restart'): gates.validate_persistence(self.evidence, 'source', HEAD)


if __name__ == '__main__': unittest.main()
