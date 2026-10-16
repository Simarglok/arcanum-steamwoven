"""Closed, path/pixel-free WORLD003 original grid reports shared by both gates."""
from __future__ import annotations
import json
import re
from pathlib import Path
from . import config
from .screen_grid_validation import validate_keyboard_inert, validate_screen_cases

VIEWPORTS = [[1280, 800], [1440, 900], [800, 600]]
START = 'StepStarted@1/1:6,1>7,1|ActionChanged@1/1:6,1>7,1'
FINISH = '|CellCommitted@5/4:6,1>7,1|CenterArrived@3/2:6,1>7,1|ActionChanged@3/2:6,1>7,1'
# Literal independently translated D=1/2 expectations, not implementation constants.
TRACES = {
    'early': START + '|StepCancelled@9/8:6,1>7,1|FacingChanged@9/8:6,1>7,1|CenterArrived@5/4:6,1>7,1|ActionChanged@5/4:6,1>7,1',
    'equality': START + FINISH,
    'late': START + FINISH,
    'open-reverse': START + FINISH + '|StepStarted@2/1:7,1>6,1|FacingChanged@2/1:7,1>6,1|ActionChanged@2/1:7,1>6,1|CellCommitted@9/4:7,1>6,1|CenterArrived@5/2:7,1>6,1|ActionChanged@5/2:7,1>6,1',
    'blocker': 'StepRejected@1/1:6,1>5,1',
}


# Tasks whose grid evidence must come from the original WORLD keyboard route (ARC-INPUT-001+).
KEYBOARD_ROUTE_TASKS = ('ARC-INPUT-001', 'ARC-WORLD-007')


def validate_report(report: object, head: str, host: str, require_keyboard_route: bool = False) -> None:
    if not isinstance(report, dict) or (
        report.get('schema'), report.get('task'), report.get('candidateCommit'),
        report.get('host'), report.get('kind'), report.get('outcome'), report.get('exitCode')
    ) != ('arcanum-grid-validation-v1', 'ARC-WORLD-003', head, host, 'grid-original', 'PASS', 0):
        raise ValueError('grid task/head/host/kind/outcome binding is invalid')
    if not re.fullmatch(r'[0-9a-f]{40}', head):
        raise ValueError('grid report requires an exact candidate commit')
    if (report.get('originalIdentity') != 'authenticated-original-grid-and-actor'
            or report.get('profile') != 'HumanFixed'
            or report.get('assessmentProfile') != 'BASE_SECTOR_1452_1290_ADJACENT_V1'
            or report.get('context') != 'ISOLATED_PC_CRITTER_ART_NONPOLYMORPH'
            or report.get('duration') != '1/2'
            or report.get('graphical') is not True
            or report.get('actualViewports') != VIEWPORTS
            or report.get('negativeControls') != 'PASS'
            or report.get('renderedPhases') != ['Idle', 'Walk', 'Returning', 'OutboundAfterEntry', 'Blocked']
            or report.get('originalParity') != 'NOT_VERIFIED'):
        raise ValueError('grid original/rendering/phase coverage is incomplete')
    digest = report.get('selectedEvidenceSha256')
    epoch = report.get('evidenceEpoch')
    context_fingerprint = report.get('contextFingerprint')
    if not isinstance(context_fingerprint, str) or not re.fullmatch(r'[0-9a-f]{64}', context_fingerprint):
        raise ValueError('grid context fingerprint is missing')
    if not isinstance(digest, str) or not re.fullmatch(r'[0-9a-f]{64}', digest) or type(epoch) is not int or epoch < 1:
        raise ValueError('grid snapshot evidence binding is invalid')
    if report.get('blocker') != dict(sourceIndex=1, cell=[5, 1], anchor=[-163, 134], rectangle=[-276, -128, 204, 267], visible='PASS', reason='ADMITTED_DESTINATION_SCENERY_BLOCKER'):
        raise ValueError('confirmed admitted visible blocker is missing')
    cases = report.get('cases')
    if not isinstance(cases, list) or len(cases) != 15:
        raise ValueError('grid report requires five complete scenarios at every viewport')
    for viewport in VIEWPORTS:
        rows = [row for row in cases if isinstance(row, dict) and row.get('viewport') == viewport]
        if len(rows) != 5 or {row.get('name') for row in rows} != set(TRACES):
            raise ValueError('missing or duplicate grid scenario')
        for row in rows:
            name = row['name']
            if (row.get('trace') != TRACES[name] or row.get('rendered') != 'PASS'
                    or row.get('cell') != ([7, 1] if name in ('equality', 'late') else [6, 1])
                    or row.get('phase') != 'Settled'):
                raise ValueError('grid production transcript/state/readback differs from literal oracle')
            admissions = row.get('admissions')
            expected_edges = [([6, 1], [5, 1], 1)] if name == 'blocker' else [([6, 1], [7, 1], 5)]
            if name == 'open-reverse':
                expected_edges.append(([7, 1], [6, 1], 1))
            if not isinstance(admissions, list) or len(admissions) != len(expected_edges):
                raise ValueError('grid admission count is incomplete')
            for index, (admission, (source, destination, direction)) in enumerate(zip(admissions, expected_edges)):
                if not isinstance(admission, dict) or (
                    admission.get('from'), admission.get('to'), admission.get('direction'),
                    admission.get('records'), admission.get('gaps'), admission.get('selectedEvidenceSha256'),
                    admission.get('evidenceEpoch'), admission.get('coverage'), admission.get('disposition')
                ) != (source, destination, direction, 317, 0, digest, epoch, 'Supported', 'Blocked' if name == 'blocker' else 'Open'):
                    raise ValueError('grid exact-edge assessment evidence is incomplete or stale')
                key = admission.get('key')
                binding = dict(ChartId='SECTOR_1452_1290_LOCAL_CENTERS', ActorId='HUMAN_FIXED_DIAGNOSTIC',
                    ContextId=context_fingerprint, CapabilitiesProfileId='ISOLATED_PC_HUMAN_FIXED_V1',
                    CapabilitiesFingerprint='KIND15_ART2_FLAGS0_GROUND_NONPOLYMORPH_REGISTRY0',
                    AssessmentProfileId='BASE_SECTOR_1452_1290_ADJACENT_V1', InputFingerprint=digest, EvidenceEpoch=epoch)
                time = dict(Value=dict(Numerator=1 if index == 0 else 2, Denominator=1, IsValid=True), IsValid=True)
                if not isinstance(key, dict) or any(key.get(field) != value for field, value in {
                    'Binding': binding, 'From': dict(X=source[0], Y=source[1]),
                    'To': dict(X=destination[0], Y=destination[1]), 'Direction': direction,
                    'StartTime': time, 'OpportunityId': index + 1, 'ProposedStepId': index + 1,
                    'MaterialIntentKey': dict(Time=time, Sequence=index * 4 + 1), 'EvidenceRevision': 0,
                }.items()):
                    raise ValueError('grid runtime-generated exact key is incomplete or stale')
    # Reports from ARC-INPUT-001 onwards carry the original WORLD keyboard route:
    # the stick drives the screen matrix and the keyboard sector chords are inert.
    # Earlier reports (no route field) keep their keyboard+stick matrix.
    if report.get('keyboardRoute') == 'original-world':
        validate_screen_cases(report.get('screenCases'), digest, epoch, devices=('stick',))
        validate_keyboard_inert(report.get('keyboardInert'))
    elif 'keyboardRoute' in report or 'keyboardInert' in report or require_keyboard_route:
        raise ValueError('grid keyboard route binding is invalid')
    else:
        validate_screen_cases(report.get('screenCases'), digest, epoch)


def validate_reports(evidence: Path, head: str, task: str | None = None) -> None:
    for host in ('source', 'exported'):
        data = config.optional_authority_bytes(evidence / f'{host}-grid-original.log')
        if data is None:
            raise ValueError('missing grid original structured report')
        try:
            report = json.loads(data)
        except (ValueError, UnicodeError) as error:
            raise ValueError('invalid grid original structured report') from error
        validate_report(report, head, host, require_keyboard_route=task in KEYBOARD_ROUTE_TASKS)
