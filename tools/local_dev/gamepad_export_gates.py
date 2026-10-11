"""ARC-GPAD-005 export receipts and production-scene evidence, fail closed."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import zlib
from . import config
from . import gamepad_matrix_authority as matrix_authority

TASK = 'ARC-GPAD-005'
GATES = {
    'sdk-info': 'sdk-info.log', 'locked-restore': 'locked-restore.log',
    'locked-tests-restore': 'locked-tests-restore.log', 'release-build': 'release-build.log',
    'core-harness': 'core-harness.log', 'editor-import': 'editor-import.log',
    'debug-build': 'debug-build.log', 'godot-self-test': 'godot-self-test.log',
    'sdk-editor-bridge': 'sdk-editor-bridge.log', 'source-preset-matrix': 'source-preset-matrix.log',
    'source-preset-persistence': 'source-preset-persistence.log',
    'shared-input-regressions': 'shared-input-regressions.log', 'local-lifecycle-tests': 'local-lifecycle-tests.log',
    'export': 'export.log', 'exported-smoke': 'exported-smoke.log',
    'exported-preset-matrix': 'exported-preset-matrix.log', 'exported-preset-persistence': 'exported-preset-persistence.log',
    'plist-lint': 'plist-lint.log', 'codesign-deep-strict': 'codesign.log',
}


def json_authority(path: Path) -> dict:
    if path.exists() and config.regular_file(path).stat().st_size > 128 * 1024 * 1024:
        raise ValueError('missing or excessive evidence report')
    data = config.optional_authority_bytes(path)
    if data is None or len(data) > 128 * 1024 * 1024:
        raise ValueError('missing or excessive evidence report')
    try:
        value = json.loads(data)
    except (ValueError, UnicodeError) as error:
        raise ValueError('malformed evidence JSON') from error
    if not isinstance(value, dict):
        raise ValueError('evidence must be an object')
    return value


def engine_error(log: bytes) -> bool:
    """A successful child exit must not conceal a Godot/C# runtime error."""
    return any(marker in log for marker in (b'ERROR:', b'SCRIPT ERROR:', b'Unhandled exception'))


def validate_exit_receipts(evidence: Path, head: str) -> set[str]:
    if not re.fullmatch(r'[0-9a-f]{40}', head):
        raise ValueError('exact candidate head required')
    required = set()
    for gate, name in GATES.items():
        receipt = json_authority(evidence / (name + '.result.json'))
        log = config.optional_authority_bytes(evidence / name)
        if (log is None or not log or engine_error(log) or receipt.get('schema') != 'arcanum-gamepad-gate-result-v1'
                or receipt.get('task') != TASK or receipt.get('candidateCommit') != head
                or receipt.get('gate') != gate or receipt.get('log') != name
                or receipt.get('outcome') != 'PASS' or type(receipt.get('exitCode')) is not int or receipt['exitCode'] != 0
                or type(receipt.get('logBytes')) is not int or receipt['logBytes'] != len(log)
                or receipt.get('logSha256') != hashlib.sha256(log).hexdigest()):
            raise ValueError('failed or unbound gamepad gate: ' + gate)
        try:
            start = datetime.fromisoformat(receipt['startedAt']); end = datetime.fromisoformat(receipt['finishedAt'])
            if start.utcoffset() != timezone.utc.utcoffset(start) or end.utcoffset() != timezone.utc.utcoffset(end) or end < start:
                raise ValueError('ordered UTC observations required')
        except (KeyError, TypeError, ValueError) as error:
            raise ValueError('invalid gate observation time') from error
        required.update(('evidence/' + name, 'evidence/' + name + '.result.json'))
    return required


def capture(evidence: Path, directory: str, name: object, digest: object, size: list) -> str:
    if directory not in ('source-images', 'exported-images'):
        raise ValueError('unsafe capture directory')
    if not isinstance(name, str) or not re.fullmatch(r'[a-zA-Z0-9_.-]+\.png', name):
        raise ValueError('unsafe capture identity')
    data = config.optional_authority_bytes(evidence / directory / name)
    if (data is None or len(data) < 33 or data[:8] != b'\x89PNG\r\n\x1a\n'
            or hashlib.sha256(data).hexdigest() != digest or list(struct.unpack('>II', data[16:24])) != size):
        raise ValueError('capture dimensions/digest mismatch')
    offset = 8; kinds = []
    while offset < len(data):
        if offset + 12 > len(data): raise ValueError('truncated PNG chunk')
        length = struct.unpack('>I', data[offset:offset+4])[0]; end = offset + 8 + length
        if end + 4 > len(data) or zlib.crc32(data[offset+4:end]) & 0xffffffff != struct.unpack('>I', data[end:end+4])[0]:
            raise ValueError('PNG chunk integrity mismatch')
        kinds.append(data[offset+4:offset+8]); offset = end + 4
    if not kinds or kinds[0] != b'IHDR' or kinds[-1] != b'IEND' or b'IDAT' not in kinds:
        raise ValueError('incomplete PNG payload')
    return 'evidence/' + directory + '/' + name


def validate_witnesses(report: dict) -> None:
    """Validate every lossless geometry DAG node, without expanding repeated data."""
    table = report.get('witnesses')
    if report.get('witnessSchema') != 'arcanum-gamepad-geometry-witnesses-v1' or not isinstance(table, dict) or not table:
        raise ValueError('missing normalized geometry witnesses')
    decoded = {}
    for digest, data in table.items():
        if (not isinstance(digest, str) or not re.fullmatch(r'[0-9a-f]{64}', digest)
                or not isinstance(data, str) or hashlib.sha256(data.encode('utf-8')).hexdigest() != digest):
            raise ValueError('changed geometry witness digest')
        try:
            value = json.loads(data)
        except (ValueError, RecursionError) as error:
            raise ValueError('malformed geometry witness') from error
        if not isinstance(value, (dict, list)):
            raise ValueError('geometry witness must be structured')
        decoded[digest] = value
    visited = set()
    def walk(value, ancestors=frozenset(), depth=0):
        if depth > 128:
            raise ValueError('excessive geometry witness depth')
        if isinstance(value, dict):
            if '$witness' in value:
                digest = value['$witness']
                if set(value) != {'$witness'} or not isinstance(digest, str) or digest not in decoded or digest in ancestors:
                    raise ValueError('missing or cyclic geometry witness')
                if digest not in visited:
                    walk(decoded[digest], ancestors | {digest}, depth + 1)
                    visited.add(digest)
            else:
                for child in value.values(): walk(child, ancestors, depth + 1)
        elif isinstance(value, list):
            for child in value: walk(child, ancestors, depth + 1)
    for row in report.get('tuples', []):
        for key in ('labelGeometry', 'settlement', 'scrollObservations'):
            if key not in row:
                raise ValueError('missing mandatory geometry observation')
            walk(row[key])
    if visited != set(table):
        raise ValueError('orphan geometry witness')


def validate_matrix(evidence: Path, origin: str, head: str, *, stable: bool = True, probe: bool = False) -> set[str]:
    if origin not in ('source', 'exported') or probe and origin != 'source':
        raise ValueError('unsupported matrix origin/scope')
    name = origin + '-preset-matrix.json'; report = json_authority(evidence / name)
    if (report.get('schema') != 'arcanum-gamepad-lab-matrix-v1' or report.get('task') != TASK
            or report.get('status') != 'passed' or report.get('candidateCommit') != head or report.get('origin') != origin
            or report.get('isolatedTestStorage') is not True or report.get('backend') in (None, '', 'headless')
            or report.get('coverage') != ('discriminatory-binding-only' if probe else 'full-contract-o5')):
        raise ValueError('incomplete or unbound graphical matrix')
    expected = set(itertools.product(('contextual','quick-slots','radial'), ((1280,800),(2560,1440),(3440,1440)), (100,150,200), (.5,2.)))
    if probe: expected = {('contextual', (1280,800), 100, z) for z in (.5,2.)}
    tuples = report.get('tuples'); required = {'evidence/' + name}
    if not isinstance(tuples, list) or len(tuples) != len(expected):
        raise ValueError('complete graphical tuple set required')
    validate_witnesses(report)
    observed = set()
    for row in tuples:
        size = [row.get('width'), row.get('height')]
        observed.add((row.get('preset'), tuple(size), row.get('uiScalePercent'), row.get('zoom')))
        if row.get('status') != 'passed' or any(row.get(k) != size for k in ('actualViewport','actualRaster','imageDimensions')):
            raise ValueError('requested versus actual GUI dimensions mismatch')
        required.add(capture(evidence, origin + '-images', row.get('capture'), row.get('captureSha256'), size))
        scenarios = row.get('scenarios')
        if not isinstance(scenarios, list):
            raise ValueError('missing same-frame GUI observations')
        ids = {s.get('scenario') for s in scenarios if isinstance(s, dict)}
        needed = {'bank-0','bank-2','full-ten-modal-override','target-lock-journal','pending-ground','grid-preview','projected-labels','settings-last-row','lifecycle','projection-zoom'}
        needed |= {'wheel-no-selection','wheel-up01-latched','wheel-09-10'} if row['preset'] == 'radial' else {'bank-1','bank-3'}
        if not needed <= ids:
            raise ValueError('missing production O5 scenario')
        for scenario in scenarios:
            if scenario.get('raster') != size or not scenario.get('controls') or not isinstance(scenario.get('frame'), int):
                raise ValueError('missing observed widget/raster/frame binding')
            required.add(capture(evidence, origin + '-images', scenario.get('capture'), scenario.get('sha256'), size))
    if observed != expected:
        raise ValueError('duplicate/missing graphical tuple')
    lifetime = report.get('lifetimes')
    if not isinstance(lifetime, list) or len(lifetime) != len(expected) or any(x.get('nodes') != x.get('baselineNodes') or x.get('resources', float('inf')) > x.get('baselineResources', 0) + 4 for x in lifetime):
        raise ValueError('native resource plateau missing')
    baseline = report.get('nativeBaseline')
    if not isinstance(baseline, dict): raise ValueError('native baseline missing')
    required.add(capture(evidence, origin + '-images', baseline.get('capture'), baseline.get('sha256'), baseline.get('actualViewport')))
    required |= matrix_authority.validate_binding(evidence, origin, head, report, json_authority, stable=stable)
    try:
        matrix_authority.validate_observations(report)
    except (KeyError, TypeError, IndexError) as error:
        raise ValueError('missing/malformed O5 observations') from error
    return required


def stage_matrix(source: Path, destination: Path, origin: str, head: str, *, stable: bool = True) -> set[str]:
    """Copy exact immutable records/captures; never symlink or mutate the raw report.

    Copying avoids a writable hardlink alias. Source is validated first and the
    destination again after staging, using exactly the writer/registration API.
    """
    source = config.canonical_existing(str(source), directory=True)
    destination = config.canonical_existing(str(destination), directory=True)
    required = validate_matrix(source, origin, head, stable=stable)
    build_log = 'debug-build.log' if origin == 'source' else 'export.log'
    paths = required | {'evidence/' + origin + '-preset-matrix.log', 'evidence/' + build_log}
    for item in sorted(paths):
        relative = Path(item).relative_to('evidence')
        data = config.optional_authority_bytes(source / relative)
        if data is None: raise ValueError('missing staged source record')
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        config.canonical_existing(str(target.parent), directory=True)
        with target.open('xb') as stream: stream.write(data)
        if config.optional_authority_bytes(target) != data: raise ValueError('staged bytes changed')
    return validate_matrix(destination, origin, head, stable=stable)


def validate_persistence(evidence: Path, origin: str, head: str) -> set[str]:
    name = origin + '-preset-persistence.json'; report = json_authority(evidence / name)
    if (report.get('schema') != 'arcanum-gamepad-persistence-suite-v1' or report.get('task') != TASK
            or report.get('status') != 'passed' or report.get('candidateCommit') != head
            or report.get('isolatedTestStorage') is not True or report.get('legacyUnchanged') is not True):
        raise ValueError('failed or unbound persistence suite')
    needed = {'verify-migration','verify-migrated-v2','write-contextual','verify-contextual','write-quick-slots','verify-quick-slots',
              'write-radial','verify-radial','write-copy','verify-copy','reject-copy','reset-copy','verify-reset-copy','verify-corrupt','save-io-failure','write','verify-nondefault','verify-default'}
    rows = report.get('operations')
    if not isinstance(rows, list) or not needed <= {r.get('operation') for r in rows}:
        raise ValueError('missing restart operation')
    required = {'evidence/' + name}
    for row in rows:
        log_name = row.get('log'); report_name = row.get('report')
        if not isinstance(log_name, str) or not re.fullmatch(r'[a-z0-9-]+\.log', log_name) or not isinstance(report_name, str) or not re.fullmatch(r'[a-z0-9-]+\.json', report_name):
            raise ValueError('unsafe restart report path')
        log = config.optional_authority_bytes(evidence / (origin + '-persistence') / log_name)
        result = json_authority(evidence / (origin + '-persistence') / report_name)
        if log is None or engine_error(log) or hashlib.sha256(log).hexdigest() != row.get('logSha256') or type(row.get('exitCode')) is not int or row['exitCode'] != 0 or result.get('status') != 'passed' or result.get('isolatedTestStorage') is not True:
            raise ValueError('failed or changed fresh process receipt')
        if row['operation'] not in ('write','verify-nondefault','verify-default') and (result.get('candidateCommit') != head or result.get('origin') != origin):
            raise ValueError('restart executable binding mismatch')
        required.update(('evidence/' + origin + '-persistence/' + log_name, 'evidence/' + origin + '-persistence/' + report_name))
    return required


def validate_reports(evidence: Path, head: str) -> set[str]:
    required = validate_exit_receipts(evidence, head)
    for origin in ('source','exported'):
        required |= validate_matrix(evidence, origin, head) | validate_persistence(evidence, origin, head)
    return required


def run_gate(evidence: Path, head: str, name: str, command: list[str]) -> int:
    if not re.fullmatch(r'[0-9a-f]{40}', head) or name not in GATES.values() or not command:
        raise ValueError('unsupported exact-head gamepad gate')
    evidence = config.canonical_existing(str(evidence), directory=True)
    log_path = evidence / name; receipt_path = evidence / (name + '.result.json')
    if config.optional_authority_bytes(log_path) is not None or config.optional_authority_bytes(receipt_path) is not None:
        raise ValueError('immutable gate evidence already exists')
    started = datetime.now(timezone.utc).isoformat()
    root = Path.cwd().resolve()
    build_map = matrix_authority.input_map(root) if name in ('debug-build.log', 'export.log') else None
    origin = name.split('-preset-matrix.log')[0] if name in ('source-preset-matrix.log', 'exported-preset-matrix.log') else None
    context = matrix_authority.prepare(root, evidence, origin, head, command, json_authority) if origin else None
    environment = os.environ.copy()
    if context:
        binding = context['binding']
        environment.update(ARCANUM_GAMEPAD_MATRIX_NONCE=binding['nonce'], ARCANUM_GAMEPAD_INPUT_MAP_SHA256=binding['inputMapSha256'],
            ARCANUM_GAMEPAD_BUILD_RECEIPT_SHA256=binding['buildReceiptSha256'], ARCANUM_GAMEPAD_RUNTIME_SHA256=binding['runtimeInventorySha256'])
    budget = 420 if name in ('source-preset-matrix.log', 'exported-preset-matrix.log') else 180
    with log_path.open('xb') as stream:
        try:
            # The full54-tuple O5 sweep includes native scroll/lifecycle/scenario
            # raster observations. Measured source sweep reached44 tuples in240s;
            # retain a finite420s matrix budget,180s for every other gate.
            result = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, timeout=budget, check=False, env=environment)
            code = result.returncode
        except subprocess.TimeoutExpired:
            stream.write(('\nGAMEPAD_GATE_TIMEOUT_%ds\n' % budget).encode()); code = 124
    log = config.optional_authority_bytes(log_path)
    if log is None:
        raise ValueError('gate log disappeared before receipt')
    failed_log = engine_error(log)
    build_evidence = None
    if build_map is not None and code == 0 and not failed_log:
        if matrix_authority.input_map(root) != build_map:
            raise ValueError('complete source inputs changed during build')
        output_dir = root / 'experiments/gamepad-lab/.godot/mono/temp/bin/Debug' if name == 'debug-build.log' else Path(command[-1])
        build_evidence = dict(inputMap=build_map, inputMapSha256=matrix_authority.digest(build_map), outputs=matrix_authority.inventory(output_dir))
    receipt = dict(schema='arcanum-gamepad-gate-result-v1', task=TASK, candidateCommit=head,
        command=command, workspace=str(Path.cwd()), nativeTask=os.environ.get('HERMES_KANBAN_TASK'),
        gate=next(g for g,f in GATES.items() if f==name), log=name, outcome='PASS' if code==0 and not failed_log else 'FAIL', exitCode=code,
        engineError=failed_log,
        startedAt=started, finishedAt=datetime.now(timezone.utc).isoformat(), logBytes=len(log), logSha256=hashlib.sha256(log).hexdigest())
    if build_evidence is not None: receipt['matrixBuild'] = build_evidence
    if context:
        receipt['matrixBinding'] = context['binding']
        receipt['matrixEnvironment'] = {k: environment.get(k) for k in ('ARCANUM_GAMEPAD_CANDIDATE', 'GAMEPAD_LAB_SETTINGS_TEST_PATH',
            'ARCANUM_GAMEPAD_MATRIX_NONCE', 'ARCANUM_GAMEPAD_INPUT_MAP_SHA256', 'ARCANUM_GAMEPAD_BUILD_RECEIPT_SHA256', 'ARCANUM_GAMEPAD_RUNTIME_SHA256',
            'DOTNET_ROOT', 'PATH', '__GODOT_SHELL_ENV_SET', 'TMPDIR')}
    with receipt_path.open('x', encoding='utf-8') as stream:
        json.dump(receipt, stream, indent=2); stream.write('\n')
    if context and code == 0 and not failed_log:
        matrix_authority.finish(evidence, origin, head, command, context, receipt, json_authority)
    return code if code != 0 else int(failed_log)


def main() -> int:
    p=argparse.ArgumentParser();p.add_argument('--evidence',type=Path,required=True);p.add_argument('--head',required=True);p.add_argument('--log',required=True);p.add_argument('command',nargs=argparse.REMAINDER);a=p.parse_args()
    return run_gate(a.evidence,a.head,a.log,a.command[1:] if a.command[:1]==['--'] else a.command)

if __name__=='__main__':raise SystemExit(main())
