#!/usr/bin/env python3
"""Independent full wire-grid oracle for the opt-in AREA owner seam probe.

Standard-library only, no production imports or rendered-output calibration.
The public AreaOwnerFixture(seamMarkers=True) authors the documented inputs.
Write output to an owned ignored evidence directory, outside app payloads.
"""
import argparse
from fractions import Fraction
import hashlib
import json
from pathlib import Path


def oracle():
    mirrors = {(63, 63), (64, 63), (63, 64), (64, 64)}
    rectangles = [(x, y, 40 * (y - x) + 1, 20 * (x + y))
                  for y in range(128) for x in range(128)]
    points = [(x, y) for y, xs in [
        (2525, [1, 2, 38, 39, 40, 77, 78, 79, 80]),
        (2545, [-39, -38, -2, -1, 0, 37, 38, 39, 40, 41, 42, 77, 78, 79, 80]),
        (2565, [1, 2, 38, 39, 40, 77, 78, 79, 80])] for x in xs]
    views = []
    observed = set()
    for z in (Fraction(1, 2), Fraction(1), Fraction(2)):
        for px, py in ((0, 0), (13, -11), (-17, 9)):
            ox = (Fraction(479, 4) if z == Fraction(1, 2) else Fraction(120)) + px
            oy = (Fraction(401, 4) if z == Fraction(1, 2) else Fraction(100)) + py - 2520 * z
            samples = []
            for wx, wy in points:
                sx, sy = int((ox + wx * z) // 1), int((oy + wy * z) // 1)
                assert 0 <= sx < 480 and 0 <= sy < 400
                cx = (Fraction(sx) + Fraction(1, 2) - ox) / z
                cy = (Fraction(sy) + Fraction(1, 2) - oy) / z
                winner = None
                for x, y, left, top in rectangles:
                    if left <= cx < left + 78 and top <= cy < top + 40:
                        source = int((cx - left) // 1)
                        if (x, y) in mirrors:
                            source = 77 - source
                        winner = (x, y, source)
                assert winner is not None
                x, y, source = winner
                assert 0 <= source < 78
                if (x, y) in mirrors:
                    observed.add((x, y, source < 39))
                samples.append(dict(x=sx, y=sy, r=10 if source < 39 else 240,
                                    g=20 if source < 39 else 100, b=200 if source < 39 else 30,
                                    a=255, cell=[x, y], source_x=source))
            views.append(dict(zoom=float(z), origin_x=float(ox), origin_y=float(oy), samples=samples))
    assert all((x, y, b) in observed for x, y in mirrors for b in (False, True))
    return dict(schema='independent-area-wire-seams-v1', terrain=16384, bodies=5, commands=16389,
                viewport=[480, 400], views=views, samples=sum(len(v['samples']) for v in views),
                oracle='literal wire coordinates; global y/x traversal; half-open78x40; Fraction pixel centers; reflection77-index')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    data = (json.dumps(oracle(), indent=2) + '\n').encode()
    digest = hashlib.sha256(data).hexdigest()
    if digest != '9b8bf682c750628679ceee642351b965d97a865f734e6dd7ea64b812fe946532':
        raise ValueError('frozen oracle changed')
    with args.output.open('xb') as output:
        output.write(data)
    print('independent wire seam oracle:9views/297samples; SHA256=' + digest)


if __name__ == '__main__':
    main()
