"""Closed WORLD003 all-eight screen/adjacent evidence, independent projected algebra."""
from __future__ import annotations


def validate_screen_cases(cases: object, digest: str, epoch: int) -> None:
    offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)]
    signs = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)]
    inversions = [[0, 1, 2, 3, 4, 5, 6, 7], [0, 7, 6, 5, 4, 3, 2, 1],
                  [4, 3, 2, 1, 0, 7, 6, 5], [4, 5, 6, 7, 0, 1, 2, 3]]
    viewports = [[1280, 800], [1440, 900], [800, 600]]
    if not isinstance(cases, list) or len(cases) != 192:
        raise ValueError('grid screen matrix requires all8/four inversions/two devices/three actual viewports')
    seen = set()
    for row in cases:
        if not isinstance(row, dict):
            raise ValueError('invalid grid screen row')
        viewport, device, sector, flags = (row.get(k) for k in ('viewport', 'device', 'sector', 'flags'))
        if (viewport not in viewports or device not in ('keyboard', 'stick')
                or type(sector) is not int or not 0 <= sector < 8 or type(flags) is not int or not 0 <= flags < 4):
            raise ValueError('invalid grid screen row identity')
        identity = (tuple(viewport), device, sector, flags)
        if identity in seen:
            raise ValueError('duplicate grid screen row')
        seen.add(identity)
        facing = inversions[flags][sector]
        dx, dy = offsets[facing]
        projected = [40 * (dy - dx), 20 * (dy + dx)]
        def sign(value): return (value > 0) - (value < 0)
        if tuple(map(sign, projected)) != signs[facing]:
            raise ValueError('independent screen algebra differs from literal signs')
        expected = dict(facing=facing, projected=projected, direction=facing, rendered='PASS', coverage='Supported',
                        disposition='Open', gaps=0, profile='BASE_SECTOR_1452_1290_ADJACENT_V1',
                        evidenceEpoch=epoch, selectedEvidenceSha256=digest, to=[7 + dx, 2 + dy])
        if row.get('from') != [7, 2] or any(row.get(k) != v for k, v in expected.items()):
            raise ValueError('grid screen endpoint/facing/evidence differs from independent oracle')
        edge = f'7,2>{7 + dx},{2 + dy}'
        events = [f'StepStarted@2/1:{edge}']
        if facing != 4:
            events.append(f'FacingChanged@2/1:{edge}')
        events.extend([f'ActionChanged@2/1:{edge}', f'CellCommitted@9/4:{edge}',
                       f'CenterArrived@5/2:{edge}', f'ActionChanged@5/2:{edge}'])
        if row.get('trace') != '|'.join(events):
            raise ValueError('grid all8 callback/frame transcript differs from literal oracle')
        legs = row.get('legs')
        if not isinstance(legs, list) or len(legs) != (4 if facing % 2 == 0 else 0):
            raise ValueError('grid adjacent role-labelled leg closure missing')
        if legs:
            xdir, ydir = (1 if dx < 0 else 5), (7 if dy < 0 else 3)
            destination, hx, hy = [7 + dx, 2 + dy], [7 + dx, 2], [7, 2 + dy]
            for leg, (role, source, target, direction) in zip(legs, [
                    ('XShoulder', [7, 2], hx, xdir), ('XFinal', hx, destination, ydir),
                    ('YShoulder', [7, 2], hy, ydir), ('YFinal', hy, destination, xdir)]):
                if leg != dict(role=role, **{'from': source}, to=target, direction=direction,
                               originalDirection=facing, records=317, gaps=0, blockers=[]):
                    raise ValueError('grid original leg endpoint/census/direction differs from oracle')
