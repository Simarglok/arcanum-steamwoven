"""ARC-UI-3278: tests of popup_kit.py on synthetic arts (no original art is read)."""
from __future__ import annotations

import json
import tempfile
import types
import unittest
from pathlib import Path

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

    def test_pack_first_where_a_frame_exists(self):
        by = popup_kit.BY_ID
        self.assertEqual(sorted(s.art for s in popup_kit.SPECS if not s.pack), ["128", "291", "354", "642", "826"])
        self.assertEqual(popup_kit.provenance_of("hardware.mess_rot"), "pack:popup_kit:hardware.mess_rot")
        self.assertEqual(popup_kit.provenance_of("hardware.skill_ro"), "rule:popup_kit:hardware.skill_ro")
        self.assertIn("pack-first", popup_kit.note_of("hardware.trap_rot"))
        self.assertIn("sample icon", popup_kit.note_of("hardware.magickweapon_rot"))
        self.assertEqual(len(by["hardware.magickweapon_rot"].pack_clear), 1)

    def test_pack_master_takes_the_frame_and_the_originals_alpha(self):
        original = synthetic()
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot", pack="x/test")
        frame = pngio.Image.blank(160, 96, (70, 60, 50, 255))            # 4 times the original; a pack frame with its own wrong alpha
        for y in range(4 * 4, 20 * 4):                                    # its brass frame lies where the original's does
            for x in range(4 * 4, 36 * 4):
                frame.data[(y * 160 + x) * 4:(y * 160 + x) * 4 + 4] = bytes((180, 140, 50, 255))
        for y in range(0, 12):
            for x in range(0, 24):
                frame.data[(y * 160 + x) * 4:(y * 160 + x) * 4 + 4] = bytes((255, 0, 255, 255))   # the pack covers the hole with magenta: it must not show
        image, extra = popup_kit.pack_master(spec, frame, original, (144, 84))
        self.assertEqual(image.pixel(1, 1)[3], 0, "the alpha is the original's")
        self.assertEqual(image.pixel(100, 60)[3], 255)
        self.assertEqual(image.pixel(72, 14)[0] > 150, True, "the pack's brass is kept")
        self.assertEqual(extra["pack_scale"], 4)
        self.assertEqual(popup_kit.gates(spec, image, (144, 84), original, rule_build=False), [])
        with self.assertRaises(popup_kit.KitError):
            popup_kit.pack_master(spec, pngio.Image.blank(100, 50, (1, 1, 1, 255)), original, (144, 84))

    def test_a_pack_frame_that_moves_the_window_is_refused(self):
        original = synthetic()                                            # brass frame x 4..36, y 4..20
        frame = pngio.Image.blank(160, 96, (60, 60, 60, 255))
        for y in range(4 * 4 + 24, 20 * 4):                               # the pack's frame lies 6 px lower than the original's: a bigger shift than the tolerance
            for x in range(4 * 4, 36 * 4):
                frame.data[(y * 160 + x) * 4:(y * 160 + x) * 4 + 4] = bytes((180, 140, 50, 255))
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot", pack="x/test")
        with self.assertRaises(popup_kit.KitError):
            popup_kit.pack_master(spec, frame, original, (144, 84))

    def test_pack_master_leaves_the_callers_frame_and_repairs_the_edge(self):
        original = synthetic()
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot", pack="x/test")
        frame = pngio.Image.blank(160, 96, (70, 60, 50, 255))
        for y in range(4 * 4, 20 * 4):
            for x in range(4 * 4, 36 * 4):
                frame.data[(y * 160 + x) * 4:(y * 160 + x) * 4 + 4] = bytes((180, 140, 50, 255))
        before = bytes(frame.data)
        image, _ = popup_kit.pack_master(spec, frame, original, (144, 84))
        self.assertEqual(bytes(frame.data), before)
        for y in range(0, 14):                                   # beside the hole no pixel is opaque black (the colour of a pack frame's open place)
            for x in range(0, 30):
                p = image.pixel(x, y)
                self.assertFalse(p[3] == 255 and sum(p[:3]) < 30, (x, y, p))

    def test_the_stand_in_probe_must_be_dark_window_fill(self):
        original = pngio.Image.blank(40, 24, (60, 60, 60, 255))
        frame = pngio.Image.blank(160, 96, (200, 200, 200, 255))        # light above the box: not a window
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot", pack="x/test", pack_clear=((5, 15, 15, 22),))
        with self.assertRaises(popup_kit.KitError):
            popup_kit.pack_master(spec, frame, original, (144, 84))

    def test_build_dry_run_writes_nothing_and_a_missing_pack_frame_is_an_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            local = Path(tmp)
            spec = popup_kit.BY_ID["hardware.mess_rot"]
            folder = local / "census" / "frames" / f"{spec.art}-{spec.name}"
            folder.mkdir(parents=True)
            original = pngio.Image.blank(410, 107, (60, 60, 60, 255))
            pngio.write(folder / "f0.png", original)
            pack = local / "pack"
            args = types.SimpleNamespace(local_root=local, frames=None, pack=pack, alt_dir=None, only=spec.ident, dry_run=True)
            with self.assertRaises(popup_kit.KitError):          # no pack frame on disk
                popup_kit.cmd_build(args)
            same = types.SimpleNamespace(local_root=local, frames=None, pack=pack, alt_dir=local / "masters", only=spec.ident, dry_run=True)
            with self.assertRaises(popup_kit.KitError):          # the alternative must not overwrite the masters
                popup_kit.cmd_build(same)
            self.assertFalse((local / "masters").exists())

    def test_pack_master_paints_out_a_stand_in(self):
        original = pngio.Image.blank(40, 24, (60, 60, 60, 255))
        frame = pngio.Image.blank(160, 96, (4, 4, 6, 255))
        for y in range(60, 90):
            for x in range(20, 60):
                frame.data[(y * 160 + x) * 4:(y * 160 + x) * 4 + 4] = bytes((20, 20, 220, 255))      # a blue sample icon
        spec = popup_kit.Spec("hardware.test", "0", "test", "rot", pack="x/test", pack_clear=((5, 15, 15, 22),))
        image, _ = popup_kit.pack_master(spec, frame, original, (144, 84))
        self.assertLess(image.pixel(30, 60)[2], 20, "the stand-in is gone")

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
