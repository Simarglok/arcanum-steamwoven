"""ARC-UI-3278: tests of popup_kit.py on synthetic arts (no original art is read)."""
from __future__ import annotations

import json
import unittest

from tools.steamwoven_ui import popup_kit, pngio

BRASS = {"tone": [140.0, 106.0, 58.0], "grain_sd": 3.0, "sha256": {}}


def synthetic(w: int = 40, h: int = 24) -> pngio.Image:
    """Iron, a brass frame round a dark window, a transparent corner."""
    img = pngio.Image.blank(w, h, (60, 60, 60, 255))

    def fill(x0, y0, x1, y1, rgba):
        for y in range(y0, y1):
            for x in range(x0, x1):
                img.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes(rgba)
    fill(4, 4, 36, 20, (180, 140, 50, 255))
    fill(8, 8, 32, 16, (0, 0, 6, 255))
    fill(0, 0, 6, 3, (0, 0, 0, 0))
    return img


class PopupKitTests(unittest.TestCase):
    def test_every_spec_has_a_manifest_row_of_a_grid_size(self):
        rows = {r["id"]: r for r in json.loads(popup_kit.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        self.assertEqual(len(popup_kit.SPECS), 17)
        for spec in popup_kit.SPECS:
            row = rows[spec.ident]
            self.assertEqual(row["source_arts"], [spec.art])
            self.assertEqual(row["status"], "master")
            self.assertEqual(row["provenance"], popup_kit.provenance_of(spec.ident))
            self.assertTrue(all(side % popup_kit.GRID == 0 for side in row["size"]), spec.ident)

    def test_the_seventeen_arts_of_the_issue(self):
        self.assertEqual(sorted(int(s.art) for s in popup_kit.SPECS),
                         sorted([7, 128, 200, 274, 288, 290, 291, 354, 564, 642, 842, 822, 298, 826, 292, 565, 328]))

    def test_classes(self):
        c = popup_kit.classify
        self.assertEqual(c(0, 0, 8, 255), popup_kit.WIN)
        self.assertEqual(c(180, 140, 50, 255), popup_kit.BRASS)
        self.assertEqual(c(90, 62, 20, 255), popup_kit.BRASS, "shadowed brass is still brass")
        self.assertEqual(c(32, 32, 0, 255), popup_kit.OLIVE)
        self.assertEqual(c(60, 60, 60, 255), popup_kit.I)
        self.assertEqual(c(9, 9, 9, 0), popup_kit.HOLE)

    def test_small_or_hollow_dark_pieces_are_not_windows(self):
        w, h = 40, 24
        cmap = [popup_kit.I] * (w * h)
        for x in range(2, 38):                       # a one-pixel dark line (an outline): hollow, thin
            cmap[3 * w + x] = popup_kit.WIN
        for y in range(8, 18):                       # a solid field: a window
            for x in range(8, 30):
                cmap[y * w + x] = popup_kit.WIN
        popup_kit.keep_solid_windows(cmap, w, h)
        self.assertEqual(cmap[3 * w + 10], popup_kit.I)
        self.assertEqual(cmap[12 * w + 12], popup_kit.WIN)

    def test_edt(self):
        d = popup_kit.edt([False, True, True, True, False, False, True, True], 8, 1)
        self.assertEqual([round(v, 2) for v in d], [0.0, 1.0, 2.0, 1.0, 0.0, 0.0, 1.0, 2.0])

    def test_render_is_deterministic_and_keeps_the_originals_shapes(self):
        original = synthetic()
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot", wells=((10, 5, 14, 7),))
        a, _ = popup_kit.render(spec, original, (144, 84), BRASS)
        b, _ = popup_kit.render(spec, original, (144, 84), BRASS)
        self.assertEqual(a.data, b.data)
        self.assertEqual(a.pixel(1, 1)[3], 0, "the transparent corner stays transparent")
        self.assertEqual(a.pixel(0, 40)[3], 255)
        r, _, _, alpha = a.pixel(72, 42)
        self.assertEqual(alpha, 255)
        self.assertLess(r, 12, "the window keeps its dark fill")
        r, g, bl, _ = a.pixel(72, 20)
        self.assertTrue(r > g > bl and r > 100, f"the frame is brass: {(r, g, bl)}")
        self.assertEqual(popup_kit.gates(spec, a, (144, 84), original), [])

    def test_the_light_comes_from_the_top_left(self):
        original = synthetic()
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot")
        image, _ = popup_kit.render(spec, original, (144, 84), BRASS)
        top = sum(popup_kit.lum(*image.pixel(x, 15)[:3]) for x in range(40, 100)) / 60      # the top edge band of the frame
        bottom = sum(popup_kit.lum(*image.pixel(x, 71)[:3]) for x in range(40, 100)) / 60    # the bottom edge band
        self.assertGreater(top, bottom)

    def test_no_magenta_and_gates_catch_it(self):
        original = synthetic()
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot")
        image, _ = popup_kit.render(spec, original, (144, 84), BRASS)
        for i in range(100):
            image.data[(40 * 144 + 20 + i % 40) * 4:(40 * 144 + 20 + i % 40) * 4 + 3] = bytes((255, 0, 255))
        self.assertTrue(any("magenta" in f for f in popup_kit.gates(spec, image, (144, 84), original)))

    def test_brass_on_the_border_is_shaded(self):
        original = pngio.Image.blank(20, 12, (60, 60, 60, 255))
        for x in range(20):
            for y in range(0, 4):
                original.data[(y * 20 + x) * 4:(y * 20 + x) * 4 + 4] = bytes((180, 140, 50, 255))
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot")
        image, _ = popup_kit.render(spec, original, (72, 42), BRASS)
        self.assertGreater(sum(image.pixel(30, 0)[:3]), 60, "the brass on the first row is lit, not black")
        self.assertEqual(popup_kit.gates(spec, image, (72, 42), original), [])
        image.data[(0 * 72 + 5) * 4:(0 * 72 + 5) * 4 + 3] = bytes((0, 0, 0))
        self.assertTrue(any("border" in f for f in popup_kit.gates(spec, image, (72, 42), original)))

    def test_only_rejects_unknown_ids(self):
        self.assertEqual(popup_kit.chosen("hardware.mess_rot"), {"hardware.mess_rot"})
        with self.assertRaises(popup_kit.KitError):
            popup_kit.chosen("hardware.nope")

    def test_ring_mask_is_an_annulus(self):
        w = h = 120
        mask = popup_kit.ring_mask(((10.0, 10.0, 6.0),), w, h, 4.0, 4.0)
        centre, band, outside = mask[40 * w + 40], mask[40 * w + 40 + 24], mask[40 * w + 40 + 40]
        self.assertEqual((centre, outside), (0.0, 0.0))
        self.assertEqual(band, 1.0)

    def test_coverage_and_notes(self):
        self.assertEqual(set(popup_kit.coverage()), {s.ident for s in popup_kit.SPECS})
        for spec in popup_kit.SPECS:
            self.assertIn("no Codex", popup_kit.note_of(spec.ident))


if __name__ == "__main__":
    unittest.main()
