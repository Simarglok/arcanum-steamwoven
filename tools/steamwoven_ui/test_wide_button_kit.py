"""ARC-UI-3271: tests of the schematic wide button kit part (`wide_button_kit.py`): the analytic shapes, the tent-filtered alpha, the soft shadow, the resampling and the spec. A synthetic pack frame only."""
from __future__ import annotations

import json
import math
import tempfile
import unittest
from pathlib import Path

try:
    from . import pngio, surface_compose, wide_button_kit as wb
except ImportError:  # run as a script
    import pngio  # type: ignore
    import surface_compose  # type: ignore
    import wide_button_kit as wb  # type: ignore

SPEC = Path(__file__).parent / "zone_specs" / "ctx-schematic.json"


def synthetic_pack() -> pngio.Image:
    """A 3200 x 1600 frame: a flat brass colour with a gradient along x (so a resampling shift shows)."""
    w, h = 3200, 1600
    data = bytearray(w * h * 4)
    for y in range(h):
        for x in range(w):
            i = (y * w + x) * 4
            data[i:i + 4] = bytes((120 + (x // 40) % 60, 90, 40, 255))
    return pngio.Image(w, h, data)


class WideButtonKitTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.image = wb.render(synthetic_pack())

    def pixel(self, x: float, y: float):
        return self.image.pixel(int(round(x - wb.ORIGIN[0] - 0.5)), int(round(y - wb.ORIGIN[1] - 0.5)))

    def test_the_canvas_is_on_the_grid_and_the_gates_pass(self):
        self.assertEqual((self.image.w, self.image.h), wb.CANVAS)
        self.assertEqual(self.image.w % 6 + self.image.h % 6, 0)
        self.assertEqual(wb.gates(self.image), [])

    def test_the_shape_is_the_stadium_and_the_ring_disc_minus_three_holes(self):
        dx, dy, r = wb.MASTER_DOME
        left, right = wb.bar_ends()
        self.assertTrue(wb.in_shape(dx, dy - (r + wb.MASTER_RING) / 2))                 # the ring's brass above the dome
        self.assertFalse(wb.in_shape(dx, dy))                                           # the dome's hole
        for wx, wy in wb.MASTER_WELLS:
            self.assertFalse(wb.in_shape(wx, wy))                                       # the wells' holes
            self.assertTrue(wb.in_shape(wx, wy - wb.MASTER_WELL_R - 5))                 # brass round them
        self.assertTrue(wb.in_shape(left - wb.MASTER_BAR_H + 2, wb.MASTER_BAR_Y))       # the bar's rounded end
        self.assertFalse(wb.in_shape(left - wb.MASTER_BAR_H - 2, wb.MASTER_BAR_Y))
        self.assertFalse(wb.in_shape(left, wb.MASTER_BAR_Y + wb.MASTER_BAR_H + 2))      # below the bar
        self.assertTrue(wb.in_shape((left + dx) / 2 - 30, wb.MASTER_BAR_Y + wb.MASTER_BAR_H * 0.6))
        self.assertGreater(wb.MASTER_RING, wb.MASTER_BAR_H)                              # the ring stands taller than the bar
        self.assertEqual(wb.in_silhouette(dx, dy), True)                                 # the silhouette has no holes

    def test_the_geometry_matches_the_original_scale(self):
        self.assertAlmostEqual(wb.SCALE * 4, 3.6)
        dx, dy, r = wb.MASTER_DOME
        self.assertAlmostEqual(dx, 364.95, places=2)
        self.assertAlmostEqual(dy, 1405.75, places=2)
        self.assertAlmostEqual(wb.MASTER_RING - r, 13.5, places=1)                       # the ring is 15 pack px (13.5 master px) wide
        left, right = wb.bar_ends()
        self.assertAlmostEqual((left + right) / 2, dx, places=6)                         # the bar is symmetric about the dome
        # the surface's disc boxes (the old wood outside the three holes, the original's pixels inside them) hold the part's holes
        spec = surface_compose.load_spec(SPEC)
        discs = [d for d in spec.disc_boxes if d[1] >= 1200]
        self.assertEqual(len(discs), 3)
        centres = sorted(((d[4] + d[6]) / 2, (d[5] + d[7]) / 2, (d[6] - d[4]) / 2) for d in discs)
        for (cx, cy, rad), (hx, hy, hr) in zip(centres, sorted([(w[0], w[1], wb.MASTER_WELL_R) for w in wb.MASTER_WELLS] + [wb.MASTER_DOME])):
            self.assertAlmostEqual(cx, hx, delta=1.0)
            self.assertAlmostEqual(cy, hy, delta=1.0)
            self.assertTrue(hr < rad <= hr + 4, msg=f"disc {rad} for hole {hr}")        # the disc ellipse reaches a little past the hole: the part's anti-aliased edge lies on the original's pixels
        self.assertEqual([p for p in spec.kit_parts], [{"id": wb.PART, "file": f"{wb.PART}.png", "x": wb.ORIGIN[0], "y": wb.ORIGIN[1]}])
        self.assertEqual(spec.part_boxes, ())                                            # the #3248 shape cut is replaced by this part
        for d in discs:                                                                   # the three boxes tile the button's region without overlap
            self.assertTrue(d[0] <= d[4] and d[2] >= d[6] and d[1] <= d[5] and d[3] >= d[7])
        boxes = sorted(d[:4] for d in discs)
        self.assertEqual([b[2] for b in boxes[:2]], [b[0] for b in boxes[1:]])

    def test_the_part_is_opaque_brass_and_its_edges_are_anti_aliased(self):
        dx, dy, r = wb.MASTER_DOME
        top = self.pixel(dx, dy - (r + wb.MASTER_RING) / 2)
        self.assertEqual(top[3], 255)
        self.assertGreater(top[0], top[2] + 40)                                          # brass, the synthetic frame's colour
        edge = [self.pixel(dx + 25, dy - wb.MASTER_RING + k * 0.5)[3] for k in range(-6, 8)]
        self.assertTrue(any(0 < a < 255 for a in edge))
        self.assertTrue(any(0 < self.pixel(dx + r * math.cos(t), dy + r * math.sin(t))[3] < 255 for t in (0.3, 0.9, 1.7, 2.4, 3.3)), msg="the hole's edge is smooth")

    def test_the_holes_are_open_and_the_shadow_falls_outside_down_and_to_the_right(self):
        dx, dy, r = wb.MASTER_DOME
        self.assertEqual(self.pixel(dx, dy)[3], 0)
        for wx, wy in wb.MASTER_WELLS:
            self.assertEqual(self.pixel(wx, wy)[3], 0)                                   # no shadow inside a hole
        below = self.pixel(dx + 30, dy + wb.MASTER_RING + 8)
        above = self.pixel(dx + 30, dy - wb.MASTER_RING - 8)
        self.assertGreater(below[3], above[3])                                           # the shadow is down and to the right
        self.assertLessEqual(below[3], int(255 * wb.SHADOW[3]) + 1)
        self.assertEqual(sum(below[:3]), 0)                                              # black with alpha
        right_of = self.pixel(wb.bar_ends()[1] + wb.MASTER_BAR_H + 10, wb.MASTER_BAR_Y + 8)
        left_of = self.pixel(wb.bar_ends()[0] - wb.MASTER_BAR_H - 10, wb.MASTER_BAR_Y + 8)
        self.assertGreater(right_of[3], left_of[3])
        self.assertEqual(self.pixel(wb.ORIGIN[0] + 2, wb.ORIGIN[1] + 2)[3], 0)           # the canvas' corner: nothing

    def test_the_shadow_field_is_a_blurred_shifted_silhouette(self):
        field = wb.shadow_field()
        self.assertEqual((len(field), len(field[0])), (wb.CANVAS[1], wb.CANVAS[0]))
        dx, dy, _r = wb.MASTER_DOME
        centre = field[int(dy - wb.ORIGIN[1])][int(dx - wb.ORIGIN[0])]
        self.assertAlmostEqual(centre, 1.0, places=2)                                    # deep inside the shifted silhouette
        self.assertLess(field[0][0], 0.01)
        self.assertTrue(all(0.0 <= v <= 1.0 + 1e-9 for row in field for v in row[::17]))

    def test_the_resampling_keeps_the_frame_in_place(self):
        pack = pngio.Image(3200, 1600, bytearray(b"".join(bytes((x * 255 // 3199, 100, 50, 255)) for _y in range(1600) for x in range(3200))))
        out = wb.resample(pack)
        for col in (0, 300, 599):
            expected = (wb.ORIGIN[0] + col + 0.5) / wb.SCALE * 255 / 3199
            self.assertAlmostEqual(out.pixel(col, 100)[0], expected, delta=1.5)          # the master column maps to the pack column / 0.9
        for row in (0, 200):
            self.assertEqual(out.pixel(10, row)[1:3], (100, 50))

    def test_a_missing_frame_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = type("A", (), {"pack": Path(tmp) / "none.png", "local_root": Path(tmp), "dry_run": True, "out": None})()
            with self.assertRaises(wb.KitError):
                wb.cmd_rule(args)
        self.assertEqual(wb.pack_path(Path("x.png")), Path("x.png"))

    def test_the_render_is_deterministic(self):
        self.assertEqual(bytes(wb.render(synthetic_pack()).data), bytes(self.image.data))


if __name__ == "__main__":
    unittest.main()
