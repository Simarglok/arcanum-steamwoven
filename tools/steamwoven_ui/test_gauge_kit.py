"""ARC-UI-2483: tests of the gauge kit tool on synthetic frames (no art)."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
from unittest import mock

from . import fit
from . import gauge_kit as gk
from . import kit_check
from . import pngio


def solid(w: int, h: int, rgba: tuple[int, int, int, int]) -> pngio.Image:
    return pngio.Image.blank(w, h, rgba)


class PlanTests(unittest.TestCase):
    def test_every_part_of_the_batch_is_made_exactly_one_way(self) -> None:
        manifest = gk.load_manifest()
        self.assertEqual(set(gk.parts_of_batch(manifest)), set(gk.coverage(manifest)))
        self.assertEqual(len(gk.parts_of_batch(manifest)), 56)

    def test_a_part_is_in_one_collage_at_most(self) -> None:
        seen: dict[str, str] = {}
        for g in gk.GROUPS:
            for row in g.rows:
                for c in row:
                    if c.part:
                        self.assertNotIn(c.part, seen)
                        seen[c.part] = g.gid

    def test_every_group_has_a_prompt_in_the_library(self) -> None:
        from . import prompt_library
        library = prompt_library.load()
        ids = {p["id"] for p in library["prompts"]}
        self.assertTrue({g.gid for g in gk.GROUPS} <= ids)
        self.assertEqual(set(library["batches"][gk.BATCH]["prompts"]), {g.gid for g in gk.GROUPS})

    def test_provenance_ids_and_notes_fit_the_manifest_rules(self) -> None:
        self.assertEqual(gk.provenance_of("gauge.redvial"), "rule:gauge_kit:gauge.redvial")
        self.assertEqual(gk.provenance_of("gauge.skilgauge"), "fit:gauge.skilgauge")
        for part in gk.coverage():
            self.assertLessEqual(len(gk.note_of(part, 15)), 240, part)

    def test_frame_sequences_are_named_in_the_note(self) -> None:
        self.assertIn("frame 0", gk.note_of("gauge.redvial", 15))
        self.assertNotIn("frame 0", gk.note_of("gauge.redvial", 1))
        self.assertNotIn("frame 0", gk.note_of("gauge.item_dam", 9))


class LayoutTests(unittest.TestCase):
    def test_canvas_and_cells_are_on_the_grid_and_apart(self) -> None:
        cells = ((gk.Cell("a", "x", out=(60, 30), rect=(0, 0, 1, 1)),), (gk.Cell("b", "x", out=(30, 30), rect=(0, 0, 1, 1)), gk.Cell("c", "x", out=(30, 12), rect=(0, 0, 1, 1))))
        geometry = gk.layout(gk.Group("t", "t", cells, k=2.0), Path("."), None)
        width, height = geometry["canvas"]
        self.assertEqual((width % 6, height % 6), (0, 0))
        a, b, c = geometry["cells"]
        self.assertGreaterEqual(b["y"] - (a["y"] + a["h"]), gk.GAP - 1)
        self.assertGreaterEqual(c["x"] - (b["x"] + b["w"]), gk.GAP)
        for cell in geometry["cells"]:
            self.assertTrue(0 <= cell["x"] and cell["x"] + cell["w"] <= width and 0 <= cell["y"] and cell["y"] + cell["h"] <= height)

    def test_a_bleed_group_is_its_cell(self) -> None:
        group = gk.Group("t", "t", ((gk.Cell("a", "x", out=(60, 30), rect=(0, 0, 1, 1)),),), k=3.0, bleed=True)
        self.assertEqual(gk.layout(group, Path("."), None)["canvas"], [180, 90])


class PixelTests(unittest.TestCase):
    def test_denoise_makes_a_band_uniform_down_its_columns(self) -> None:
        image = solid(3, 6, (100, 100, 100, 255))
        for y in range(6):
            for x in range(3):
                if (x + y) % 2:
                    image.data[(y * 3 + x) * 4:(y * 3 + x) * 4 + 3] = bytes((103, 103, 103))
        clean = gk.denoise(image, "y")
        for x in range(3):
            self.assertEqual(len({clean.pixel(x, y) for y in range(6)}), 1)

    def test_a_colour_step_is_two_bands(self) -> None:
        image = solid(2, 6, (200, 0, 0, 255))
        for y in range(3, 6):
            for x in range(2):
                image.data[(y * 2 + x) * 4:(y * 2 + x) * 4 + 4] = bytes((20, 0, 0, 255))
        self.assertEqual(gk.bands(image, "y"), [(0, 3), (3, 6)])

    def test_a_band_that_starts_transparent_ends_at_the_first_opaque_line(self) -> None:
        image = solid(2, 7, (0, 0, 0, 0))
        for y in range(2, 7):
            colour = (200, 0, 0, 255) if y < 6 else (20, 0, 0, 255)
            for x in range(2):
                image.data[(y * 2 + x) * 4:(y * 2 + x) * 4 + 4] = bytes(colour)
        self.assertEqual(gk.bands(image, "y"), [(0, 2), (2, 6), (6, 7)])
        clean = gk.denoise(image, "y")
        self.assertEqual(clean.pixel(0, 1)[3], 0)
        self.assertEqual(clean.pixel(0, 6), (20, 0, 0, 255))

    def test_a_violet_of_the_art_is_not_ground_but_a_magenta_fringe_is(self) -> None:
        import shutil
        if shutil.which("magick") is None:
            self.skipTest("ImageMagick is not installed")
        image = solid(24, 24, gk.KEY + (255,))
        for y in range(6, 18):
            for x in range(6, 18):
                image.data[(y * 24 + x) * 4:(y * 24 + x) * 4 + 3] = bytes((120, 60, 140))     # a violet disc of the art
        image.data[(6 * 24 + 5) * 4:(6 * 24 + 5) * 4 + 3] = bytes((192, 48, 153))          # a fringe pixel: art blended into the ground
        image.data[(7 * 24 + 5) * 4:(7 * 24 + 5) * 4 + 3] = bytes((128, 0, 128))           # a dark art blended half and half
        with tempfile.TemporaryDirectory() as t:
            png = Path(t) / "c.png"
            pngio.write(png, image)
            geometry = {"canvas": [24, 24], "bleed": False, "cells": [{"id": "a", "x": 6, "y": 6, "w": 12, "h": 12, "mode": "key", "master": [12, 12]}]}
            found = gk.locate(png, geometry, (24, 24), png)
            cut = gk.cell_image(png, found["a"], geometry["cells"][0])
        self.assertEqual((cut.w, cut.h), (12, 12))
        self.assertTrue(all(cut.pixel(x, y)[3] > 0 for x in range(12) for y in range(12)))
        self.assertTrue(all(cut.pixel(x, y)[:3] != (128, 0, 128) for x in range(12) for y in range(12)))

    def test_aspect_within_six_percent_is_fixed_beyond_it_refused(self) -> None:
        opaque = gk.to_aspect(solid(103, 100, (9, 9, 9, 255)), (100, 100))
        self.assertLessEqual(abs(opaque.w / opaque.h - 1), fit.ASPECT_TOLERANCE)
        keyed = solid(100, 97, (9, 9, 9, 255))
        keyed.data[3] = 0
        fixed = gk.to_aspect(keyed, (100, 100))
        self.assertEqual((fixed.w, fixed.h), (100, 100))
        with self.assertRaises(gk.GaugeError):
            gk.to_aspect(solid(130, 100, (9, 9, 9, 255)), (100, 100))

    def test_foreground_ignores_texture_noise_and_the_largest_group_is_kept(self) -> None:
        base = solid(12, 6, (50, 50, 50, 255))
        image = solid(12, 6, (50, 50, 50, 255))
        image.data[0:3] = bytes((55, 52, 50))                       # noise: below the threshold
        for x in range(2, 6):
            image.data[(2 * 12 + x) * 4:(2 * 12 + x) * 4 + 3] = bytes((200, 200, 200))
        image.data[(5 * 12 + 11) * 4:(5 * 12 + 11) * 4 + 3] = bytes((200, 200, 200))   # a lone speck
        hit = gk.foreground(image, base, 0)
        self.assertNotIn((0, 0), hit)
        self.assertEqual(len(hit), 5)
        self.assertEqual(gk.largest_part(hit), {(x, 2) for x in range(2, 6)})

    def test_over_blends_a_half_transparent_layer_on_an_opaque_base(self) -> None:
        base = solid(2, 1, (0, 0, 0, 255))
        gk.over(base, solid(1, 1, (200, 200, 200, 128)), 1, 0)
        self.assertEqual(base.pixel(0, 0)[:3], (0, 0, 0))
        self.assertEqual(base.pixel(1, 0)[:3], (100, 100, 100))
        self.assertEqual(base.pixel(1, 0)[3], 255)

    def test_over_keeps_a_transparent_base_straight(self) -> None:
        base = solid(2, 1, (0, 0, 0, 0))
        gk.over(base, solid(1, 1, (200, 100, 50, 128)), 0, 0)
        self.assertEqual(base.pixel(0, 0), (200, 100, 50, 128))
        gk.over(base, solid(1, 1, (10, 10, 10, 255)), 0, 0)
        self.assertEqual(base.pixel(0, 0), (10, 10, 10, 255))
        gk.over(base, solid(1, 1, (9, 9, 9, 255)), 5, 5)   # outside: nothing happens

    def test_the_content_box_of_a_collage_skips_the_ground(self) -> None:
        import shutil
        if shutil.which("magick") is None:
            self.skipTest("ImageMagick is not installed")
        image = solid(60, 40, gk.KEY + (255,))
        for y in range(10, 25):
            for x in range(20, 50):
                image.data[(y * 60 + x) * 4:(y * 60 + x) * 4 + 3] = bytes((90, 70, 40))
        with tempfile.TemporaryDirectory() as t:
            path = Path(t) / "c.png"
            pngio.write(path, image)
            self.assertEqual(gk.content_bbox(path), (20, 10, 30, 15))
            geometry = {"canvas": [60, 40], "bleed": False, "cells": [{"id": "a", "x": 20, "y": 10, "w": 30, "h": 15}]}
            found = gk.locate(path, geometry, (60, 40), path)
            self.assertEqual(found["a"]["box"], [20, 10, 30, 15])
            bigger = Path(t) / "big.png"           # the same collage returned at twice the size and squashed a little: the map is by the content boxes
            pngio.write(bigger, fit.resample_image(image, (0, 0, 60, 40), 120, 76))
            found = gk.locate(bigger, geometry, (120, 76), path)
            self.assertLessEqual(abs(found["a"]["box"][0] - 40), 2)
            self.assertLessEqual(abs(found["a"]["box"][2] - 60), 3)


class ComposeTests(unittest.TestCase):
    def test_a_layer_is_placed_by_its_content_box_on_the_originals(self) -> None:
        with tempfile.TemporaryDirectory() as t:
            local = Path(t)
            frames = local / "frames"
            (frames / "base").mkdir(parents=True)
            (frames / "pic").mkdir(parents=True)
            pngio.write(frames / "base" / "f0.png", solid(20, 10, (50, 50, 50, 255)))
            picture = solid(20, 10, (50, 50, 50, 255))
            for y in range(4, 8):
                for x in range(6, 12):
                    picture.data[(y * 20 + x) * 4:(y * 20 + x) * 4 + 4] = bytes((200, 20, 20, 255))
            pngio.write(frames / "pic" / "f0.png", picture)
            (local / "masters").mkdir()
            base = solid(72, 36, (50, 50, 50, 255))
            pngio.write(local / "masters" / "base.part.png", base)
            layer = solid(80, 70, (0, 0, 0, 0))                       # the generated picture, keyed: the content box 40 x 30 at (20, 20) in its region
            for y in range(20, 50):
                for x in range(20, 60):
                    layer.data[(y * 80 + x) * 4:(y * 80 + x) * 4 + 4] = bytes((200, 20, 20, 255))
            cell = {"id": "pic_x", "folder": "pic", "frame": 0, "mask": "diff:base", "mode": "placed"}
            cache = {"grp": {"geometry": {"cells": [cell]}, "images": {"pic_x": layer},
                             "found": {"pic_x": {"box": [30, 25, 40, 30], "region": [10, 5, 80, 70], "expect": [14, 9, 70, 62], "scale": [1.0, 1.0]}},
                             "record": {}, "path": frames / "pic" / "f0.png"}}
            seen = {}

            def fake_fit(part, source, provenance, local_root, dry, halo=None):
                seen["image"] = pngio.read(source)
                return {"gates": "PASS", "size": [72, 36]}

            with mock.patch.object(gk, "fit_part", fake_fit):
                done = gk.compose_parts("base.part", "grp", {"gauge.out": "pic_x"}, frames, cache, local, False)
            self.assertEqual(done, ["gauge.out"])
            out = seen["image"]
            # the original adds x 5..12, y 3..8 (grown by one) = units 5..13 x 3..9 -> master px 18..46.8 x 10.8..32.4; the layer's whole 40 x 30 content box is mapped onto it
            red = [(x, y) for y in range(out.h) for x in range(out.w) if out.pixel(x, y)[0] > 150]
            self.assertTrue(red)
            self.assertLessEqual(abs(min(x for x, _ in red) - 18), 2)
            self.assertLessEqual(abs(max(x for x, _ in red) - 47), 2)
            self.assertLessEqual(abs(min(y for _, y in red) - 11), 2)
            self.assertLessEqual(abs(max(y for _, y in red) - 32), 2)

    def test_a_missing_base_master_is_an_error_but_a_dry_run_only_says_so(self) -> None:
        with tempfile.TemporaryDirectory() as t:
            with self.assertRaises(gk.GaugeError):
                gk.compose_parts("nothing", "grp", {}, Path(t), {"grp": {}}, Path(t), False)
            self.assertEqual(gk.compose_parts("nothing", "grp", {}, Path(t), {"grp": {}}, Path(t), True), [])


class PeelTests(unittest.TestCase):
    def _cell(self, ring: int) -> pngio.Image:
        image = solid(30, 20, (90, 90, 90, 255))
        for y in range(20):
            for x in range(30):
                if min(x, y, 29 - x, 19 - y) < ring:
                    image.data[(y * 30 + x) * 4:(y * 30 + x) * 4 + 3] = bytes((230, 20, 220))
        return image

    def test_the_ground_bleed_at_the_rim_of_an_opaque_cell_is_peeled_with_one_line_more(self) -> None:
        out = gk.peel_ground(self._cell(2))
        self.assertEqual((out.w, out.h), (30 - 6, 20 - 6))
        self.assertEqual(kit_check.ground_pixels(out.data), [])
        self.assertTrue(all(out.pixel(x, y)[:3] == (90, 90, 90) for x in range(out.w) for y in range(out.h)))

    def test_a_clean_cell_is_untouched_and_a_deep_bleed_is_an_error(self) -> None:
        clean = solid(30, 20, (90, 90, 90, 255))
        self.assertIs(gk.peel_ground(clean), clean)
        with self.assertRaises(gk.GaugeError):
            gk.peel_ground(self._cell(gk.PEEL_MAX + 1))
        big = solid(60, 40, (90, 90, 90, 255))
        for y in range(40):
            for x in range(60):
                if min(x, y, 59 - x, 39 - y) < gk.PEEL_MAX:
                    big.data[(y * 60 + x) * 4:(y * 60 + x) * 4 + 3] = bytes((230, 20, 220))
        self.assertEqual((gk.peel_ground(big).w, gk.peel_ground(big).h), (60 - 2 * (gk.PEEL_MAX + 1), 40 - 2 * (gk.PEEL_MAX + 1)))      # exactly PEEL_MAX lines is peeled, not refused


class RuleTests(unittest.TestCase):
    def test_rule_parts_have_the_master_size_of_the_manifest(self) -> None:
        manifest = gk.load_manifest()
        with tempfile.TemporaryDirectory() as t:
            frames = Path(t)
            for part, (folder, frame, axis) in gk.RULE_PARTS.items():
                (frames / folder).mkdir(parents=True)
                for n in range(9 if axis == "flat" else frame + 1):
                    w, h = (34, 3) if axis == "x" else (8, 20)
                    if axis == "flat":
                        w = h = 31
                    colour = (2 + n * 18, 0, 0, 255) if axis == "flat" else (120, 30, 30, 255)
                    pngio.write(frames / folder / f"f{n}.png", solid(w, h, colour))
            for part in gk.RULE_PARTS:
                image, _ = gk.rule_part(part, frames, manifest)
                self.assertEqual((image.w, image.h), gk.master_of(part, manifest), part)
            ramp, extra = gk.rule_part("gauge.item_dam", frames, manifest)
            pngio.write(frames / "256-Item_Dam" / "f4.png", solid(31, 31, (200, 0, 0, 255)))     # one frame off the ramp
            with self.assertRaises(gk.GaugeError):
                gk.rule_part("gauge.item_dam", frames, manifest)
            pngio.write(frames / "256-Item_Dam" / "f4.png", solid(31, 31, (2 + 4 * 18, 0, 0, 255)))
            self.assertTrue(extra["ramp_linear"])
            self.assertEqual(ramp.pixel(0, 0)[:3], (146, 0, 0))


if __name__ == "__main__":
    unittest.main()
