"""Closed RENDER006 structured reports, shared by writer and registration.

These reports attest diagnostic rendering, never original runtime parity.
No pixel data or local original input paths belong in these records.
"""
from __future__ import annotations
import json
import re
from pathlib import Path
from . import config

VIEWPORTS = [[1280, 800], [1440, 900], [800, 600]]


def validate_report(report: object, head: str, host: str, kind: str, task: str = 'ARC-RENDER-006') -> None:
    if task not in ('ARC-RENDER-006', 'ARC-WORLD-003'):
        raise ValueError('unsupported human report task')
    if not isinstance(report, dict) or (
        report.get('schema'), report.get('task'), report.get('candidateCommit'),
        report.get('host'), report.get('kind'), report.get('profile'),
        report.get('outcome'), report.get('exitCode')
    ) != ('arcanum-human-validation-v1', task, head, host, kind, 'HumanFixed', 'PASS', 0):
        raise ValueError('human report task/head/host/profile/outcome binding is invalid')
    if not re.fullmatch(r'[0-9a-f]{40}', head):
        raise ValueError('human report requires a full candidate commit')
    if (report.get('originalIdentity') != 'authenticated-actor-role'
            or report.get('physicalRows') != 88 or report.get('distinctKeys') != 55
            or report.get('rgbaBytes') != 457676 or report.get('metadata') != 'PASS'
            or report.get('allUsedRgba') != 'PASS'):
        raise ValueError('human metadata/original identity coverage is incomplete')
    if kind == 'human':
        if (report.get('graphical') is not True or report.get('actualViewports') != VIEWPORTS
                or report.get('renderedSelections') != 264 or report.get('negativeControls') != 'PASS'
                or report.get('defaultZoom') != 2 or report.get('progression') != 'PASS'):
            raise ValueError('human graphical behavior coverage is incomplete')


def validate_reports(evidence: Path, head: str, task: str = 'ARC-RENDER-006') -> None:
    for name, host, kind in (('human-metadata.log', 'source', 'human-metadata'),
                             ('source-human-original.log', 'source', 'human'),
                             ('exported-human-original.log', 'exported', 'human')):
        data = config.optional_authority_bytes(evidence / name)
        if data is None:
            raise ValueError('missing human structured report')
        try:
            report = json.loads(data)
        except (ValueError, UnicodeError) as error:
            raise ValueError('invalid human structured report') from error
        validate_report(report, head, host, kind, task)
