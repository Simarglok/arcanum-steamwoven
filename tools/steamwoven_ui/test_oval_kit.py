"""ARC-UI-3271: tests of the inventory oval kit part (`oval_kit.py`): geometry against the committed surface spec, the rim, the plate's corners, the bevel, the baked shadow and the refusals. Synthetic material only."""
from __future__ import annotations

import math
import tempfile
import unittest
from pathlib import Path

try:
    from . import oval_kit, pngio, surface_compose
except ImportError:  # run as a script
    import oval_kit  # type: ignore
    import pngio  # type: ignore
    import surface_compose  # type: ignore

SPEC = Path(__file__).parent / "zone_specs" / "ctx-inventory.json"


def flat_iron(value: int = 60) -> pngio.Image:
    w, h = oval_kit.PLATE[2] - oval_kit.PLATE[0], oval_kit.PLATE[3] - oval_kit.PLATE[1]
    return pngio.Image(w, h, bytearray(bytes((value, value, value, 255)) * (w * h)))


def flat_material() -> dict:
    perimeter = int(2 * math.pi * 130) + 10
    return {"grain": [[0.0] * oval_kit.BRASS_ROWS for _ in range(perimeter)], "tone": [150.0, 110.0, 50.0], "sha256": {}}


class OvalKitTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.image = oval_kit.render(flat_material(), flat_iron())

    def alpha(self, x: int, y: int) -> int:
        return self.image.pixel(x, y)[3]

    def test_the_canvas_is_on_the_grid_and_the_gates_pass(self):
        self.assertEqual((self.image.w, self.image.h), oval_kit.CANVAS)
        self.assertEqual(self.image.w % 6 + self.image.h % 6, 0)
        self.assertEqual(oval_kit.gates(self.image), [])

    def test_both_inventory_ovals_are_laid_by_the_spec_at_the_places_measured_on_the_original(self):
        spec = surface_compose.load_spec(SPEC)
        parts = [p for p in spec.kit_parts if p["id"] == oval_kit.PART]
        self.assertEqual(len(parts), 2)       # the hand oval and the arrow oval: one part laid twice
        self.assertEqual(spec.disc_boxes[1][4:], (2532, 1190, 2814, 1346))
        self.assertEqual(spec.disc_boxes[2][4:], (2531, 1384, 2815, 1540))
        for part, disc in zip(parts, spec.disc_boxes[1:]):
            cx, cy = (disc[4] + disc[6]) / 2, (disc[5] + disc[7]) / 2
            self.assertEqual((part["x"] + oval_kit.CENTRE[0], part["y"] + oval_kit.CENTRE[1]), (cx, cy))         # the rim is concentric with the disc's rim box
            self.assertAlmostEqual(oval_kit.RIM[0], (disc[6] - disc[4]) / 2, delta=1.0)          # the arrow oval's rim box is one px wider each side (284 against 282)
            self.assertEqual(oval_kit.RIM[1], (disc[7] - disc[5]) / 2)
            self.assertEqual(part["file"], f"{oval_kit.PART}.png")
        self.assertEqual([(p["x"] + oval_kit.PLATE[0], p["y"] + oval_kit.PLATE[1]) for p in parts], [(2531, 1191), (2531, 1385)])     # the plate rectangles measured on the original
        self.assertTrue(oval_kit.CORNER[0] > oval_kit.RIM[0] and oval_kit.CORNER[1] > oval_kit.RIM[1])
        self.assertFalse(any(p for p in spec.kit_parts if "plate" in p["id"] and p["id"] != oval_kit.PART))

    def test_the_inside_of_the_rim_is_open_and_the_rim_is_opaque_brass(self):
        cx, cy = oval_kit.CENTRE
        self.assertEqual(self.alpha(int(cx), int(cy)), 0)
        self.assertEqual(self.alpha(int(cx) + 60, int(cy) - 20), 0)
        for x, y in ((int(cx), int(cy - oval_kit.RIM[1] + 4)), (int(cx), int(cy + oval_kit.RIM[1] - 5)), (int(cx - oval_kit.RIM[0] + 4), int(cy)), (int(cx + oval_kit.RIM[0] - 5), int(cy))):
            r, g, b, a = self.image.pixel(x, y)
            self.assertEqual(a, 255, msg=str((x, y)))
            self.assertGreater(r, b + 25, msg=f"brass is warm at {(x, y)}")       # brass, not grey

    def test_the_rim_is_a_constant_width_band_and_is_anti_aliased(self):
        cx, cy = oval_kit.CENTRE
        column = [self.alpha(int(cx), y) for y in range(int(cy - oval_kit.RIM[1]) - 3, int(cy - oval_kit.RIM[1]) + 16)]
        width = sum(a / 255 for a in column)
        self.assertAlmostEqual(width, oval_kit.RING_WIDTH, delta=0.8)             # at the top the band is its width
        row = [self.alpha(x, int(cy)) for x in range(int(cx - oval_kit.RIM[0]) - 3, int(cx - oval_kit.RIM[0]) + 16)]
        self.assertAlmostEqual(sum(a / 255 for a in row), oval_kit.RING_WIDTH, delta=0.8)   # and at the side
        slanted = [self.alpha(int(cx) + 60, y) for y in range(int(cy - oval_kit.RIM[1]) - 3, int(cy - oval_kit.RIM[1]) + 16)]
        self.assertTrue(any(0 < a < 255 for a in slanted), msg="the outer edge is smooth")

    def test_the_light_comes_from_the_upper_left(self):
        def brightness(angle: float) -> float:
            cx, cy = oval_kit.CENTRE
            t = math.radians(angle)
            x = cx + (oval_kit.RIM[0] - oval_kit.RING_WIDTH * 0.55) * math.cos(t)
            y = cy + (oval_kit.RIM[1] - oval_kit.RING_WIDTH * 0.55) * math.sin(t)
            px = self.image.pixel(int(x), int(y))
            return 0.299 * px[0] + 0.587 * px[1] + 0.114 * px[2]
        # the highlight ridge at the surface normal 222 degrees is the brightest part of the rim's upper left, far above the shaded lower left
        ridge = max(brightness(a) for a in range(205, 240, 2))
        self.assertGreater(ridge, brightness(135) + 40)

    def test_the_plate_shows_only_in_its_four_corners(self):
        x0, y0, x1, y1 = oval_kit.PLATE
        cx, cy = oval_kit.CENTRE
        for x, y in ((x0 + 2, y0 + 2), (x1 - 3, y0 + 2), (x0 + 2, y1 - 3), (x1 - 3, y1 - 3)):
            self.assertEqual(self.alpha(x, y), 255, msg=str((x, y)))
        self.assertEqual(self.alpha(x0 + 2, int(cy) - 30), 0)                     # the middle of the plate's sides: no plate (the rim itself reaches the side at its middle row)
        self.assertEqual(self.alpha(int(cx) - 40, y1 - 2), 0)
        self.assertEqual(self.alpha(x0 - 1, y0 + 2), 0)                           # outside the rectangle: nothing
        self.assertEqual(self.alpha(x1, y1 - 3), 0)
        legs = [self.alpha(x, y0 + 3) for x in range(x0, x0 + 60)]               # along the top edge: hard until the arc, then none
        self.assertEqual(legs[0], 255)
        self.assertEqual(legs[-1], 0)
        arc = [self.alpha(x, y) for y in range(y0 + 2, y0 + 30) for x in range(x0, x0 + 60) if 0 < self.alpha(x, y) < 255]
        self.assertGreater(len(arc), 20)                                          # the arc is anti-aliased
        self.assertTrue(all(self.alpha(x0, y) in (0, 255) for y in range(y0, y0 + 30)))       # the straight leg is hard

    def test_the_plate_is_dark_iron_with_a_bevel_lit_from_the_upper_left(self):
        x0, y0, x1, y1 = oval_kit.PLATE
        px = lambda x, y: sum(self.image.pixel(x, y)[:3]) / 3
        deep_tl, deep_br = px(x0 + 10, y0 + 10), px(x1 - 11, y1 - 11)
        self.assertGreater(px(x0 + 12, y0), deep_tl, msg="the top edge faces the light: lit")
        self.assertGreater(px(x0, y0 + 12), deep_tl, msg="the left edge faces the light: lit")
        self.assertLess(px(x1 - 13, y1 - 1), deep_br, msg="the bottom edge faces away: dark")
        self.assertLess(px(x1 - 1, y1 - 13), deep_br, msg="the right edge faces away: dark")
        self.assertLess(deep_tl, 70)                                              # dark iron, darker than the wood round it
        r, g, b, _a = self.image.pixel(x0 + 10, y0 + 10)
        self.assertTrue(r >= g >= b and r - b < 15)                               # a little warm, still grey iron

    def test_the_rims_shadow_is_baked_into_the_plate_down_and_to_the_right(self):
        field = oval_kit.shadow_field(range(0, 300), range(0, 168))
        self.assertGreater(field[(260, 150)], 0.25)                               # the lower right, under the shift
        self.assertLess(field[(20, 20)], 0.01)                                    # the upper left: none
        self.assertGreater(field[(260, 150)], field[(40, 150)])
        x0, y0, x1, y1 = oval_kit.PLATE
        darker = self.image.pixel(x1 - 12, y1 - 6)
        lighter = self.image.pixel(x0 + 12, y0 + 6)
        self.assertLess(sum(darker[:3]), sum(lighter[:3]))

    def test_the_arc_table_and_the_edge_distance_are_exact_on_a_circle(self):
        self.assertAlmostEqual(oval_kit.arc_table(50.0, 50.0)[-1], 2 * math.pi * 50, delta=0.05)
        self.assertAlmostEqual(oval_kit.arc_at(oval_kit.arc_table(50.0, 50.0), math.pi), math.pi * 50, delta=0.05)
        d, nx, ny = oval_kit.signed_edge(60.0, 0.0, 50.0, 50.0)
        self.assertAlmostEqual(d, 10.0, delta=0.01)
        self.assertEqual((round(nx, 6), round(ny, 6)), (1.0, 0.0))
        self.assertLess(oval_kit.signed_edge(0.0, -30.0, 141.0, 78.0)[0], 0)

    def test_the_render_is_deterministic(self):
        again = oval_kit.render(flat_material(), flat_iron())
        self.assertEqual(bytes(again.data), bytes(self.image.data))

    def test_a_grain_shorter_than_the_rim_is_refused(self):
        short = {"grain": [[0.0] * oval_kit.BRASS_ROWS for _ in range(50)], "tone": [150.0, 110.0, 50.0], "sha256": {}}
        with self.assertRaises(oval_kit.KitError):
            oval_kit.render(short, flat_iron())

    def test_a_missing_or_open_kit_master_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(oval_kit.KitError):
                oval_kit.brass_material(Path(tmp))
            with self.assertRaises(oval_kit.KitError):
                oval_kit.iron_window(Path(tmp))
            hole = pngio.Image.blank(966, 150)               # a master with no opaque pixels
            pngio.write(Path(tmp) / f"{oval_kit.IRON_SOURCE[0]}.png", hole)
            with self.assertRaises(oval_kit.KitError):
                oval_kit.iron_window(Path(tmp))

    def test_the_iron_window_mirrors_rows_past_the_master_and_skips_its_soft_edge(self):
        with tempfile.TemporaryDirectory() as tmp:
            src = pngio.Image(966, 150, bytearray(b"".join(bytes((y, y, y, 255)) * 966 for y in range(150))))
            pngio.write(Path(tmp) / f"{oval_kit.IRON_SOURCE[0]}.png", src)
            window, digest = oval_kit.iron_window(Path(tmp))
        self.assertEqual(len(digest), 64)
        self.assertEqual((window.w, window.h), (286, 156))
        self.assertEqual(window.pixel(0, 0)[0], oval_kit.IRON_EDGE)                # the first row used is past the master's soft first rows
        self.assertEqual(window.pixel(0, 146)[0], window.pixel(0, 145)[0])         # the mirror turns here


if __name__ == "__main__":
    unittest.main()
