#!/usr/bin/env python3
"""Closed full-area source/export native observation and independent reconciliation.

Only safe counts/digests reach the export manifest. Original rows, arguments,
pixels and raw child output remain in explicitly owned ignored private evidence.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time

from world_validate import ChildFailure, run_child


def candidate_state(root: Path) -> tuple[str, dict[str, str]]:
    head = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    paths = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z', '-m', '-o', '--exclude-standard']).decode().split('\0')
    return head, {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in paths if name and (root / name).is_file()}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--candidate-root', type=Path, required=True)
    parser.add_argument('--app', type=Path)
    parser.add_argument('--timeout', type=int, default=900)
    # The literal export task: ARC-WORLD-009 and ARC-RENDER-008 builds also load the cave and must pass its transition smoke.
    parser.add_argument('--task', choices=('ARC-RENDER-007', 'ARC-INPUT-001', 'ARC-WORLD-007', 'ARC-WORLD-009', 'ARC-RENDER-008'), default='ARC-RENDER-007')
    args = parser.parse_args()
    host = 'exported' if args.app else 'source'
    summary: dict = dict(schema='arcanum-area-validation-v1', task='ARC-RENDER-007',
                   host=host, outcome='FAIL', exitCode=None,
                   graphical=True, startedAt=datetime.now(timezone.utc).isoformat())
    started = time.monotonic()
    stage = 'input-policy'
    evidence: Path | None = None
    try:
        if not 1 <= args.timeout <= 900:
            raise ValueError('timeout policy')
        root = args.candidate_root.resolve(strict=True)
        sys.path.insert(0, str(root))
        from tools.local_dev import area_validation, config, launch
        before = candidate_state(root)
        head = before[0]
        if re.fullmatch(r'[0-9a-f]{40}', head) is None:
            raise ValueError('head policy')
        summary['candidateCommit'] = head
        oracle = Path(os.environ['ARC_AREA_ORACLE'])
        private = config.canonical_existing(os.environ['ARC_AREA_PRIVATE_EVIDENCE'], directory=True)
        # Private evidence must be task-owned within the ignored workspace, not
        # the sealed export, a global cache or an original-input directory.
        private.relative_to(root / 'local')
        subprocess.run(['git', '-C', str(root), 'check-ignore', '-q', str(private)], check=True)
        evidence = Path(tempfile.mkdtemp(prefix=host + '-area-', dir=private))
        env = dict(os.environ)
        env.update(__GODOT_SHELL_ENV_SET='1', DOTNET_ROOT=str(Path.home() / '.local/share/dotnet'),
                   PATH=str(Path.home() / '.local/share/dotnet') + ':/usr/bin:/bin:/usr/sbin:/sbin',
                   WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH=str(evidence / 'input.json'),
                   WORLD_DIAGNOSTIC_ZOOM_TEST_PATH=str(evidence / 'zoom.json'),
                   ARC_AREA_REPORT=str(evidence / 'coverage.json'))
        prefix = [str(args.app.resolve(strict=True))] if args.app else [
            '/Applications/Godot_mono.app/Contents/MacOS/Godot', '--path', str(root / 'tools/terrain-viewer/Godot')]
        argv = prefix + ['--', '--native-smoke', *launch.terrain_arguments(config.load_config().get('inputs', {}), 'crash-site-area', task=args.task)]
        stage = 'native-observation'
        code, output = run_child(argv, env, args.timeout)
        summary['exitCode'] = code
        (evidence / 'native.log').write_text(output, encoding='utf-8')
        # ARC-RENDER-008: the whole-core original overlay check runs in the same native observation.
        if (code != 0 or 'ARC-RENDER-007 native-smoke source-coverage and sampled-pixels PASS ' not in output
                or 'ARC-RENDER-008 original overlay PASS ' not in output or 'ERROR:' in output):
            raise ValueError('native outcome')
        if args.task in area_validation.CAVE_TASKS:
            if 'ARC-WORLD-009 native-smoke transition PASS ' not in output:
                raise ValueError('cave transition outcome')
            summary['caveTransition'] = 'PASS'
        stage = 'original-reconciliation'
        raw = config.optional_authority_bytes(evidence / 'coverage.json')
        if raw is None:
            raise ValueError('coverage missing')
        result = area_validation.compare_original(json.loads(raw), oracle)
        stage = 'resource-observation'
        resources = config.optional_authority_bytes(evidence / 'coverage.json.resources.json')
        if resources is None:
            raise ValueError('resources missing')
        result = {**result, 'resources': area_validation.resource_summary(json.loads(resources))}
        if candidate_state(root) != before:
            raise ValueError('candidate changed')
        summary.update(result, outcome='PASS', coverageSha256=hashlib.sha256(raw).hexdigest(),
                       originalRuntimeParity='NOT_VERIFIED')
        area_validation.validate_report(summary, head, summary['host'], args.task)
    except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError, ChildFailure) as error:
        if evidence is not None:
            # Exception details belong to ignored private diagnostics, not the
            # safe receipt copied into a sealed export.
            (evidence / 'failure.txt').write_text(type(error).__name__ + ': ' + str(error))
        summary.update(outcome='FAIL', failureStage=stage)
    summary['elapsedSeconds'] = round(time.monotonic() - started, 3)
    print(json.dumps(summary, sort_keys=True))
    return 0 if summary['outcome'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
