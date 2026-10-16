"""WORLD003 exit receipts: existence of a log is not a successful gate."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess

from . import config

GATES = {
    'locked-build': 'locked-build.log', 'source-headless': 'source-headless.log',
    'source-graphical': 'source-graphical.log', 'export': 'export.log',
    'exported-headless': 'exported-headless.log', 'exported-graphical': 'exported-graphical.log',
    'codesign-deep-strict': 'codesign.log', 'focused-tests': 'tests.log',
    'source-world-original': 'source-world-original.log', 'exported-world-original': 'exported-world-original.log',
    'source-world-persistence': 'source-world-persistence.log', 'exported-world-persistence': 'exported-world-persistence.log',
    'dual-hostfxr': 'hostfxr.log', 'human-metadata': 'human-metadata.log',
    'source-human-original': 'source-human-original.log', 'exported-human-original': 'exported-human-original.log',
    'source-grid-original': 'source-grid-original.log', 'exported-grid-original': 'exported-grid-original.log',
}


def gates_for(task: str) -> dict[str, str]:
    if task == 'ARC-WORLD-003':
        return dict(GATES)
    if task in ('ARC-RENDER-007', 'ARC-INPUT-001', 'ARC-WORLD-007', 'ARC-WORLD-009', 'ARC-RENDER-008'):
        return {**GATES, **{f'{host}-{name}': f'{host}-{name}.log' for host in ('source', 'exported')
                            for name in ('area-original', 'area-grid', 'area-seams', 'area-nearest')}}
    raise ValueError('unsupported exact export task')


def validate_reports(evidence: Path, head: str, task: str = 'ARC-WORLD-003') -> None:
    if not re.fullmatch(r'[0-9a-f]{40}', head):
        raise ValueError('gate receipts require an exact head')
    for gate, name in gates_for(task).items():
        encoded = config.optional_authority_bytes(evidence / (name + '.result.json'))
        log = config.optional_authority_bytes(evidence / name)
        try:
            receipt = json.loads(encoded) if encoded is not None else None
        except (ValueError, UnicodeError) as error:
            raise ValueError('invalid gate exit receipt') from error
        if (not isinstance(receipt, dict) or log is None or not log
                or receipt.get('schema') != 'arcanum-grid-gate-result-v1'
                or receipt.get('task') != task or receipt.get('candidateCommit') != head
                or receipt.get('gate') != gate or receipt.get('log') != name
                or receipt.get('outcome') != 'PASS' or type(receipt.get('exitCode')) is not int
                or receipt['exitCode'] != 0 or type(receipt.get('logBytes')) is not int
                or receipt['logBytes'] != len(log)
                or receipt.get('logSha256') != hashlib.sha256(log).hexdigest()):
            raise ValueError(f'missing, failed or unbound gate exit receipt: {gate}')
        try:
            start = datetime.fromisoformat(receipt['startedAt'])
            end = datetime.fromisoformat(receipt['finishedAt'])
            if start.utcoffset() != timezone.utc.utcoffset(start) or end.utcoffset() != timezone.utc.utcoffset(end) or end < start:
                raise ValueError('timestamps must be ordered UTC observations')
        except (KeyError, TypeError, ValueError) as error:
            raise ValueError(f'invalid gate observation times: {gate}') from error


def run_gate(evidence: Path, head: str, name: str, command: list[str], task: str = 'ARC-WORLD-003') -> int:
    """Run one command, retaining failure facts; never rerun or overwrite evidence."""
    gates = gates_for(task)
    if not re.fullmatch(r'[0-9a-f]{40}', head) or name not in (*gates.values(), 'locked-restore.log') or not command:
        raise ValueError('unsupported grid export gate command')
    evidence = config.canonical_existing(str(evidence), directory=True)
    log_path = evidence / name
    receipt_path = evidence / (name + '.result.json')
    if config.optional_authority_bytes(log_path) is not None or config.optional_authority_bytes(receipt_path) is not None:
        raise ValueError('immutable gate evidence already exists')
    started = datetime.now(timezone.utc).isoformat()
    with log_path.open('xb') as stream:
        result = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, check=False)
    log = config.optional_authority_bytes(log_path)
    assert log is not None
    receipt = dict(schema='arcanum-grid-gate-result-v1', task=task, candidateCommit=head,
        gate=next((gate for gate, file in gates.items() if file == name), 'locked-restore'), log=name,
        outcome='PASS' if result.returncode == 0 else 'FAIL', exitCode=result.returncode,
        startedAt=started, finishedAt=datetime.now(timezone.utc).isoformat(),
        logBytes=len(log), logSha256=hashlib.sha256(log).hexdigest())
    # No command paths, original payload or oracle bytes in sealed receipts.
    with receipt_path.open('x', encoding='utf-8') as stream:
        json.dump(receipt, stream, indent=2); stream.write('\n')
    return result.returncode


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--head', required=True)
    parser.add_argument('--log', required=True)
    parser.add_argument('--task', choices=('ARC-WORLD-003', 'ARC-RENDER-007', 'ARC-INPUT-001', 'ARC-WORLD-007', 'ARC-WORLD-009', 'ARC-RENDER-008'), default='ARC-WORLD-003')
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    return run_gate(args.evidence, args.head, args.log, command, args.task)


if __name__ == '__main__':
    raise SystemExit(main())
