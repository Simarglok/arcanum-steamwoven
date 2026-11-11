"""ARC-UI-136: the lens ring repair keeps alpha and the square, and changes only the ring of the window frame.

Synthetic frames only, no art. Needs numpy, scipy and Pillow (the res094 venv); skipped where they are missing."""
from __future__ import annotations

from pathlib import Path
import sys
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

try:
    import numpy as np
    import lens_ring
except ImportError as e:
    np = None
    lens_ring = None
    SKIP = f'needs the res094 venv: {e}'
else:
    SKIP = ''

SIDE = 356            # the cover, 89 legacy px at x4
ORIGIN = (100, 96)    # lens (25, 24) at x4
BRASS = (210, 150, 40, 255)
WOOD = (70, 50, 40, 255)


def _ring_mask(shape, cx, cy, outer, thick):
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]]
    r = np.hypot(xx - cx, yy - cy)
    return (r <= outer) & (r > outer - thick)


def _frames(offset=(3.0, -3.0)):
    cover = np.zeros((SIDE, SIDE, 4), np.uint8)
    cover[:] = WOOD
    cx, cy = ORIGIN[0] + SIDE / 2 - 1, ORIGIN[1] + SIDE / 2 - 4
    yy, xx = np.mgrid[0:SIDE, 0:SIDE]
    r = np.hypot(xx + ORIGIN[0] - cx, yy + ORIGIN[1] - cy)
    cover[r <= 190 - 14] = (0, 0, 0, 0)                  # the round hole of the cover
    cover[(r <= 190) & (r > 190 - 14)] = BRASS
    window = np.zeros((520, 560, 4), np.uint8)
    window[:] = WOOD
    window[ORIGIN[1]:ORIGIN[1] + SIDE, ORIGIN[0]:ORIGIN[0] + SIDE] = (0, 0, 0, 0)   # the square is clear in the window frame
    # the window frame's own ring, drawn a few texels off, opaque outside the square only
    mine = _ring_mask(window.shape[:2], cx + offset[0], cy + offset[1], 194, 14)
    window[mine] = BRASS
    window[ORIGIN[1]:ORIGIN[1] + SIDE, ORIGIN[0]:ORIGIN[0] + SIDE] = (0, 0, 0, 0)
    return window, cover


@unittest.skipIf(lens_ring is None, SKIP)
class RingRepairTests(unittest.TestCase):
    def test_alpha_and_square_stay_and_the_ring_moves_onto_the_cover(self):
        window, cover = _frames()
        out = lens_ring.repair_ring(window, cover, ORIGIN)
        self.assertEqual(out.shape, window.shape)
        self.assertTrue((out[..., 3] == window[..., 3]).all(), "alpha must not change")
        square = (slice(ORIGIN[1], ORIGIN[1] + SIDE), slice(ORIGIN[0], ORIGIN[0] + SIDE))
        self.assertTrue((out[square] == window[square]).all(), "the clear square must not change")
        self.assertTrue((out != window).any(), "the ring must be rebuilt")
        far = np.ones(window.shape[:2], bool)
        far[40:500, 40:520] = False
        self.assertTrue((out[far] == window[far]).all(), "far from the ring nothing changes")

    def test_fit_finds_the_cover_circle(self):
        _, cover = _frames()
        cx, cy, radius = lens_ring.fit_ring(cover.astype(float), ORIGIN)
        self.assertAlmostEqual(radius, 190, delta=1.5)
        self.assertAlmostEqual(cx, ORIGIN[0] + SIDE / 2 - 1, delta=1.5)
        self.assertAlmostEqual(cy, ORIGIN[1] + SIDE / 2 - 4, delta=1.5)

    def test_a_cover_without_a_ring_is_refused(self):
        window, cover = _frames()
        cover[:] = WOOD
        with self.assertRaises(ValueError):
            lens_ring.repair_ring(window, cover, ORIGIN)


if __name__ == '__main__':
    unittest.main()
