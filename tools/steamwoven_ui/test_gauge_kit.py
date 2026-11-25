"""ARC-UI-2483: tests of the gauge kit tool on synthetic frames (no art)."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from . import fit
from . import gauge_kit as gk
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
            self.assertTrue(extra["ramp_linear"])
            self.assertEqual(ramp.pixel(0, 0)[:3], (146, 0, 0))


if __name__ == "__main__":
    unittest.main()
