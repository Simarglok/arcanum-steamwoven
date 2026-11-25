"""ARC-UI-3261: tests of the hardware kit tool on synthetic frames (no art)."""
from __future__ import annotations

import math
import unittest

from . import build_kit
from . import hardware_kit as hk
from . import pngio


def lens_art(size: int = 89, wood: tuple[int, int, int] = (60, 45, 30)) -> pngio.Image:
    """A synthetic lens: a transparent circle, a lit ring (brighter to the top left) round it, wood in the corners."""
    image = pngio.Image.blank(size, size)
    c = size / 2
    for y in range(size):
        for x in range(size):
            dx, dy = x + 0.5 - c, y + 0.5 - c
            d = math.hypot(dx, dy) - c
            if d < 0:
                continue
            if d < 3:
                light = 120 + int(100 * (-(dx + dy) / (2 * c)))
                image.data[(y * size + x) * 4:(y * size + x) * 4 + 4] = bytes((clip(light), clip(light - 30), 40, 255))
            else:
                image.data[(y * size + x) * 4:(y * size + x) * 4 + 4] = bytes((*wood, 255))
    return image


def clip(v: int) -> int:
    return max(0, min(255, v))


class PlanTests(unittest.TestCase):
    def test_every_part_has_a_manifest_row_and_a_grid_size(self) -> None:
        manifest = hk.load_manifest()
        rows = {r["id"]: r for r in manifest["parts"]}
        self.assertEqual(len(hk.PARTS), 31)
        for part in hk.PARTS:
            self.assertIn(part.ident, rows)
            w, h = rows[part.ident]["size"]
            self.assertEqual((w % 6, h % 6), (0, 0), part.ident)
            self.assertEqual(rows[part.ident]["batch"], "B-HARDWARE")

    def test_the_manifest_marks_every_part_a_master_with_its_rule_record(self) -> None:
        rows = {r["id"]: r for r in hk.load_manifest()["parts"]}
        for part in hk.PARTS:
            self.assertEqual(rows[part.ident]["status"], "master")
            self.assertEqual(rows[part.ident]["provenance"], hk.provenance_of(part.ident))

    def test_build_kit_covers_exactly_these_parts(self) -> None:
        self.assertEqual(build_kit.HARDWARE_RULE_PARTS, frozenset(p.ident for p in hk.PARTS))

    def test_the_art_numbers_are_the_census_ones(self) -> None:
        rows = {r["id"]: r for r in hk.load_manifest()["parts"]}
        for part in hk.PARTS:
            self.assertEqual(rows[part.ident]["source_arts"], [part.art], part.ident)


class RingTests(unittest.TestCase):
    def test_the_hole_is_found_from_the_alpha(self) -> None:
        cx, cy, r = hk.hole_of(lens_art())
        self.assertAlmostEqual(cx, 44.5, delta=0.1)
        self.assertAlmostEqual(r, 44.5, delta=0.3)

    def test_the_ring_keeps_the_lighting_and_drops_the_corners(self) -> None:
        table = hk.ring_table([lens_art(), lens_art(wood=(90, 70, 50))])
        ring = hk.render_ring(table, (318, 318))
        self.assertEqual((ring.w, ring.h), (318, 318))
        self.assertEqual(ring.pixel(2, 2)[3], 0)       # the corner is open (the old art's wood is not part of the ring)
        self.assertEqual(ring.pixel(159, 159)[3], 0)   # the circle is open
        top_left = max(ring.pixel(x, y) for x in range(40, 80) for y in range(40, 80) if ring.pixel(x, y)[3] > 200)
        bottom_right = max(ring.pixel(x, y) for x in range(240, 280) for y in range(240, 280) if ring.pixel(x, y)[3] > 200)
        self.assertGreater(top_left[0], bottom_right[0])  # the light falls from the top left

    def test_a_fill_makes_the_circle_opaque(self) -> None:
        ring = hk.render_ring(hk.ring_table([lens_art()]), (318, 318), fill=(7, 7, 7))
        self.assertEqual(ring.pixel(159, 159), (7, 7, 7, 255))
        self.assertEqual(ring.pixel(2, 2)[3], 0)

    def test_the_part_passes_its_gates(self) -> None:
        ring = hk.render_ring(hk.ring_table([lens_art()]), (318, 318))
        self.assertEqual(hk.gates(hk.BY_ID["hardware.pcwincvr"], ring, (318, 318)), [])
        self.assertTrue(hk.gates(hk.BY_ID["hardware.pcwincvr"], ring, (324, 324)))


class SlotTests(unittest.TestCase):
    def art(self, tone: int) -> pngio.Image:
        image = pngio.Image.blank(32, 32, (tone, tone - 8, tone - 14, 255))
        for y in range(1, 9):
            for x in range(1, 32):
                image.data[(y * 32 + x) * 4:(y * 32 + x) * 4 + 3] = bytes((tone // 2, tone // 2 - 4, tone // 2 - 7))
        for y in range(1, 32):
            for x in range(1, 8):
                image.data[(y * 32 + x) * 4:(y * 32 + x) * 4 + 3] = bytes((tone // 2, tone // 2 - 4, tone // 2 - 7))
        for y in range(32):
            image.data[(y * 32 + 31) * 4:(y * 32 + 31) * 4 + 4] = bytes((200, 185, 140, 255))
        return image

    def test_the_inset_holds_the_shadow_and_the_line_and_leaves_the_middle_open(self) -> None:
        layer = hk.inset_layer([self.art(48), self.art(40), self.art(56)])
        self.assertEqual(layer.pixel(20, 20)[3], 0)
        shadow = layer.pixel(3, 20)
        self.assertEqual(shadow[:3], (0, 0, 0))
        self.assertGreater(shadow[3], 80)               # about half the wood's light is taken
        line = layer.pixel(31, 20)
        self.assertEqual(line[3], 255)
        self.assertLess(abs(line[0] - 200), 12)   # the line keeps its own colour (scaled by the wood's normalisation)

    def test_the_wood_of_one_tone_is_cut_flipped_and_shaded(self) -> None:
        strip = pngio.Image.blank(2880, 150, (40, 30, 20, 255))
        for i in range(5):
            tile = hk.strip_wood(strip, i, 114)
            self.assertEqual((tile.w, tile.h), (114, 114))
        original = pngio.Image.blank(32, 32, (60, 46, 36, 255))
        wood = hk.shaded_wood(hk.strip_wood(strip, 0, 114), original)
        self.assertEqual(wood.pixel(50, 50)[:3], (60, 46, 36))   # the tone is the original's
        self.assertEqual(wood.pixel(50, 50)[3], 255)

    def test_one_transparent_pixel_anywhere_in_the_wood_is_refused_and_never_filled(self) -> None:
        strip = pngio.Image.blank(2880, 150, (40, 30, 20, 255))
        strip.data[(40 * 2880 + 879) * 4 + 3] = 0   # a column the old every-seventh-pixel sample missed
        with self.assertRaises(hk.KitError):
            hk.strip_wood(strip, 0, 126)
        tile = pngio.Image.blank(8, 8, (40, 30, 20, 255))
        tile.data[3] = 0
        shaded = hk.shaded_wood(tile, pngio.Image.blank(32, 32, (60, 46, 36, 255)))
        self.assertTrue(hk.gates(hk.BY_ID["hardware.splslt1"], shaded, (8, 8)))   # the gate sees the hole (and the size)

    def test_a_fill_keeps_the_ring_colour_at_the_edge(self) -> None:
        table = hk.ring_table([lens_art()])
        plain = hk.render_ring(table, (318, 318))
        filled = hk.render_ring(table, (318, 318), fill=(8, 8, 8))
        edge = [(x, y) for y in range(318) for x in range(318) if 20 < plain.pixel(x, y)[3] < 200 and filled.pixel(x, y)[3] > plain.pixel(x, y)[3]]
        self.assertTrue(edge)
        for x, y in edge[:50]:
            for c in range(3):   # the blend lies between the ring's colour and the fill's
                lo, hi = sorted((plain.pixel(x, y)[c], 8))
                self.assertTrue(lo - 1 <= filled.pixel(x, y)[c] <= hi + 1, (x, y, c))

    def test_a_wood_area_with_an_opening_is_refused(self) -> None:
        strip = pngio.Image.blank(2880, 150, (40, 30, 20, 255))
        for y in range(12, 138):
            for x in range(878, 1004):
                strip.data[(y * 2880 + x) * 4 + 3] = 0
        with self.assertRaises(hk.KitError):
            hk.strip_wood(strip, 0, 126)


class FrameTests(unittest.TestCase):
    def test_blocks_keep_the_flat_colours_and_the_alpha_exact(self) -> None:
        frame = pngio.Image.blank(4, 4, (255, 0, 0, 255))
        for y in range(1, 3):
            for x in range(1, 3):
                frame.data[(y * 4 + x) * 4:(y * 4 + x) * 4 + 4] = bytes((0, 0, 0, 0))
        out = hk.blocks(frame, (24, 24))
        self.assertEqual({out.pixel(x, y) for y in range(24) for x in range(24)}, {(255, 0, 0, 255), (0, 0, 0, 0)})
        self.assertEqual(out.pixel(12, 12)[3], 0)

    def test_the_roller_is_smooth_along_and_crisp_across(self) -> None:
        frame = pngio.Image.blank(20, 20, (170, 171, 171, 255))
        for y in range(20):
            frame.data[(y * 20 + 7) * 4:(y * 20 + 7) * 4 + 3] = bytes((100, 101, 101))
        out = hk.roller(frame, (72, 72))
        self.assertEqual({out.pixel(x, 30)[0] for x in range(72)}, {170, 100})


if __name__ == "__main__":
    unittest.main()
