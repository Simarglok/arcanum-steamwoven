"""Exact per-project/RID committed NuGet inputs and actual MSBuild witnesses."""
from __future__ import annotations
from pathlib import Path
import re
from . import config
from .gamepad_matrix_authority import file_pin

PROJECTS = ('experiments/gamepad-lab/GamepadLab.csproj',
            'experiments/gamepad-lab/Core/GamepadLab.Core.csproj',
            'src/Arcanum.Input/Arcanum.Input.csproj')
RIDS = ('osx-x64', 'osx-arm64')  # Existing macOS preset is universal, not arm64-only.


def pins(mapping: dict) -> list[dict]:
    root = Path(mapping['workspace'])
    rows = []
    for project in PROJECTS:
        for rid in RIDS:
            lock = str(Path(project).parent / ('packages.ExportDebug.' + rid + '.lock.json'))
            pin = mapping['files'].get(lock)
            if (not isinstance(pin, dict) or type(pin.get('bytes')) is not int or pin['bytes'] <= 0
                    or not isinstance(pin.get('sha256'), str) or not re.fullmatch('[0-9a-f]{64}', pin['sha256'])):
                raise ValueError('missing committed export lock input: ' + lock)
            rows.append(dict(project=project, configuration='ExportDebug', runtimeIdentifier=rid,
                             path=str(root / lock), pin=pin, restoreLockedMode=True,
                             witness=Path(project).stem + '.' + rid + '.txt'))
    return rows


def witness_line(mapping: dict, row: dict) -> str:
    return '|'.join(('ARCANUM_EXPORT_LOCK', str(Path(mapping['workspace']) / row['project']),
                     row['configuration'], row['runtimeIdentifier'], row['path'], 'true', row['pin']['sha256'].upper()))


def verify_current(mapping: dict) -> list[dict]:
    rows = pins(mapping)
    for row in rows:
        if file_pin(Path(row['path'])) != row['pin']:
            raise ValueError('export lock pin changed: ' + row['project'])
    return rows


def validate(evidence: Path, mapping: dict, supplied: object, log: bytes) -> set[str]:
    expected = pins(mapping)
    if (supplied != expected or not isinstance(supplied, list)
            or any(not isinstance(row, dict) or row.get('restoreLockedMode') is not True for row in supplied)):
        raise ValueError('missing/altered/cross-project export lock binding')
    required = set()
    for row in expected:
        name = 'export-lock-witnesses/' + row['witness']
        data = config.optional_authority_bytes(evidence / name)
        if data is None or not data or len(data) > 65536:
            raise ValueError('missing or excessive actual MSBuild lock witness')
        try:
            lines = data.decode('utf-8-sig').splitlines()
        except UnicodeError as error:
            raise ValueError('invalid actual MSBuild lock witness') from error
        if not lines or any(line != witness_line(mapping, row) for line in lines):
            raise ValueError('actual MSBuild lock input mismatch')
        if witness_line(mapping, row).encode() not in log:
            raise ValueError('export log lacks actual consumed lock witness')
        required.add('evidence/' + name)
    return required
