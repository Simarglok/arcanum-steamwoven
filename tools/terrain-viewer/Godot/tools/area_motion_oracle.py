#!/usr/bin/env python3
"""Independent moving actor/canopy overlap oracle for the opt-in AREA grid probe.

Standard-library only, no production imports or rendered-output calibration.
The public AreaOwnerFixture(motionBodies=True) and SyntheticActorFixture(human)
author the literal inputs below; this file restates the documented placement,
rank and order rules (docs/rendering/crash-site-area-viewer.md) on its own.
TerrainViewerAreaGridTest asserts the same literal rectangles and winners.
"""
import argparse
from fractions import Fraction
import hashlib
import json
import math
from pathlib import Path

# Canopy: local cell (6,1) of the chart-origin sector, object offsets, ART
# 128x160 with hotspot (-20,80); transparent border, columns 1..63 palette 1.
CANOPY = dict(cell=(6, 1), offsets=(-74, 40), size=(128, 160), hotspot=(-20, 80))
CANOPY_RED = (231, 37, 19, 255)
# HumanFixed actor frames: 9x7, hotspot (4,3), opaque (1,1) left and (7,1) right.
ACTOR = dict(size=(9, 7), hotspot=(4, 3))
ACTOR_COLORS = {('Idle', 'left'): (231, 37, 19, 255), ('Idle', 'right'): (24, 218, 236, 255),
                ('Walk', 'left'): (18, 197, 71, 255), ('Walk', 'right'): (237, 58, 184, 255)}
# Literal grid positions of the probe's key script (StepDuration 8 ticks per
# cell): idle 6; outbound 6->7 sampled at 2/8; idle 7; outbound 7->8 at 2/8;
# reverse 7->6 at 2/8, which mirrors the walk frame.
VIEWS = [
    dict(name='Idle-behind', position=(Fraction(6), Fraction(1)), action='Idle', mirrored=False, pixel='right'),
    dict(name='Walk-behind', position=(Fraction(25, 4), Fraction(1)), action='Walk', mirrored=False, pixel='left'),
    dict(name='Idle-front', position=(Fraction(7), Fraction(1)), action='Idle', mirrored=False, pixel='right'),
    dict(name='Walk-front', position=(Fraction(29, 4), Fraction(1)), action='Walk', mirrored=False, pixel='right'),
    dict(name='Walk-return-behind', position=(Fraction(27, 4), Fraction(1)), action='Walk', mirrored=True, pixel='left'),
]
ZOOMS = (1, 2, 3)


def project(x, y):
    return 40 * (y - x), 20 * (x + y)


def trunc_half(value):
    # Toward-zero integer halving, as the documented rank convention requires.
    return int(value / 2)


def ranks(offset_x, offset_y):
    horizontal = trunc_half(offset_x - 40)
    vertical = 2 * trunc_half(offset_y)
    return vertical - horizontal, horizontal + vertical


def round_even(value):
    floor = math.floor(value)
    rest = value - floor
    if rest > Fraction(1, 2) or (rest == Fraction(1, 2) and floor % 2):
        return floor + 1
    return floor


def canopy():
    (cx, cy), (ox, oy), (w, h), (hx, hy) = CANOPY['cell'], CANOPY['offsets'], CANOPY['size'], CANOPY['hotspot']
    px, py = project(cx, cy)
    first, second = ranks(ox, oy)
    return dict(rect=(px + ox + 40 - hx, py + oy + 20 - hy, w, h), map=(cx, cy), first=first, second=second)


def actor(view):
    x, y = view['position']
    mx, my = math.floor(x), math.floor(y)
    rx, ry = project(x - mx, y - my)
    offset = (round_even(rx), round_even(ry))
    # AREA foot projection (ARC-RENDER-011): the source's location-to-screen rule (0x4B8680) from the chart's
    # location origin (+40,0), plus the object (+40,+20): anchor = P(map) +
    # residual + (40,20), the drawn diamond centre like every body.
    px, py = project(mx, my)
    w, h = ACTOR['size']
    hx, hy = ACTOR['hotspot']
    column = 1 if view['pixel'] == 'left' else 7
    if view['mirrored']:
        hx, column = w - hx - 1, w - 1 - column
    left, top = px + offset[0] + 40 - hx, py + offset[1] + 20 - hy
    first, second = ranks(*offset)
    return dict(rect=(left, top, w, h), map=(mx, my), offsets=offset, first=first, second=second,
                witness=(left + column, top + 1), color=ACTOR_COLORS[(view['action'], view['pixel'])])


def oracle():
    body = canopy()
    left, top, width, height = body['rect']
    views = []
    for view in VIEWS:
        a = actor(view)
        wx, wy = a['witness']
        # The witness lies on an opaque palette-1 canopy texel and an opaque
        # actor texel, so each winner is a nonvacuous overlap decision.
        assert 1 <= wx - left <= width // 2 - 1 and 1 <= wy - top <= height - 2
        in_front = (a['map'][1], a['map'][0], a['second'], a['first']) > (
            body['map'][1], body['map'][0], body['second'], body['first'])
        views.append(dict(name=view['name'], actorRect=list(a['rect']), actorMap=list(a['map']),
                          actorOffsets=list(a['offsets']), actorRanks=[a['first'], a['second']],
                          witness=[wx, wy], actorColor=list(a['color']), canopyColor=list(CANOPY_RED),
                          inFront=in_front, winner=list(a['color'] if in_front else CANOPY_RED)))
    assert [v['inFront'] for v in views] == [False, False, True, True, False]
    return dict(schema='independent-area-motion-overlap-v1', canopyRect=list(body['rect']),
                canopyRanks=[body['first'], body['second']], zooms=list(ZOOMS), views=views,
                oracle='project 40(y-x),20(x+y); body P+offset+(40,20)-hotspot; actor P(floor)+half-even residual+(40,20)-hotspot; '
                       'ranks (v-h,h+v) with h=trunc((ox-40)/2), v=2*trunc(oy/2); order mapY,mapX,second,first')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    data = (json.dumps(oracle(), indent=2, sort_keys=True) + '\n').encode()
    if hashlib.sha256(data).hexdigest() != 'f5b26e1784a392e4ed6b299090912e6c7b800a16ad005880c5b95fe018b40f9e':
        raise ValueError('frozen motion oracle changed; update the native probe literals and digest together')
    if args.output:
        with args.output.open('xb') as output:
            output.write(data)
    else:
        print(data.decode(), end='')
    print('independent motion overlap oracle:5views; SHA256=' + hashlib.sha256(data).hexdigest())


if __name__ == '__main__':
    main()
