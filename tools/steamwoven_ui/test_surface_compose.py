"""ARC-RES-2457: tests of the pure parts of the method A composition (dot rows, components, tone fit numbers) on synthetic data; no ImageMagick, no art."""
from __future__ import annotations

import datetime
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

from . import pngio, surface_compose


class SurfaceComposeTests(unittest.TestCase):
    def test_components_are_eight_connected(self):
        width = 10
        pts = {1 * width + 1, 2 * width + 2, 5 * width + 5, 5 * width + 6}
        comps = surface_compose.components(pts, width)
        self.assertEqual(sorted(len(c) for c in comps), [2, 2])

    def test_dot_rows_keep_a_regular_row_and_drop_scattered_dots(self):
        row = [(100.0, 10.0 + 20 * i) for i in range(7)]            # seven dots on one line, 20 px apart
        scattered = [(40.0, 5.0), (73.0, 300.0), (150.0, 90.0), (151.0, 400.0), (210.0, 123.0)]
        kept = surface_compose.dot_rows(row + scattered)
        self.assertEqual(kept, set(range(7)))

    def test_dot_rows_need_enough_dots_and_a_small_gap(self):
        few = [(100.0, 10.0 + 20 * i) for i in range(5)]             # five dots: below the minimum of six
        self.assertEqual(surface_compose.dot_rows(few), set())
        far = [(100.0, 10.0 + 100 * i) for i in range(8)]            # eight dots, 100 px apart: no chain
        self.assertEqual(surface_compose.dot_rows(far), set())
        broken = [(100.0, 10.0 + 20 * i) for i in range(6)] + [(100.0, 400.0 + 20 * i) for i in range(6)]
        self.assertEqual(surface_compose.dot_rows(broken), set(range(12)))   # two chains, each long enough

    def test_dot_rows_a_stray_dot_does_not_split_a_row(self):
        row = [(99.5 if i % 2 == 0 else 101.0, 10.0 + 20 * i) for i in range(10)]
        self.assertEqual(surface_compose.dot_rows(row), set(range(10)))
        self.assertEqual(surface_compose.dot_rows(row + [(97.2, 500.0)]), set(range(10)))

    def test_components_do_not_wrap_across_the_row_end(self):
        self.assertEqual(len(surface_compose.components({9, 10}, 10)), 2)    # (x=9, y=0) and (x=0, y=1) are not neighbours

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_speck_map_marks_dark_specks_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            image = tmp_path / "in.png"
            subprocess.run(["magick", "-size", "40x10", "xc:gray40", "-fill", "gray28", "-draw", "point 10,3", "-fill", "gray52", "-draw", "point 30,6", str(image)], check=True)
            data = surface_compose.speck_map(image, 12, 40, 10, tmp_path, "t")
        marked = {(i % 40, i // 40) for i, b in enumerate(data) if b}
        self.assertEqual(marked, {(10, 3)})                                   # the dark point; the light one is no speck

    def test_fit_numbers_match_the_original_and_are_clamped(self):
        numbers = surface_compose.fit_numbers({"mean_l": 20.0, "std_l": 4.0, "mean_chroma": 10.0}, {"mean_l": 17.0, "std_l": 3.0, "mean_chroma": 9.0})
        self.assertAlmostEqual(numbers["contrast_scale"], 4 / 3)
        self.assertAlmostEqual(numbers["chroma_scale"], 10 / 9)
        self.assertEqual((numbers["mean_original"], numbers["mean_result"]), (20.0, 17.0))
        wild = surface_compose.fit_numbers({"mean_l": 20.0, "std_l": 40.0, "mean_chroma": 100.0}, {"mean_l": 17.0, "std_l": 1.0, "mean_chroma": 1.0})
        self.assertEqual((wild["contrast_scale"], wild["chroma_scale"]), (2.0, 4.0))

    def test_apply_fit_moves_the_mean_and_leaves_zero_weight_alone(self):
        image = pngio.Image(2, 1, bytearray(bytes((40, 30, 20, 255)) * 2))
        weight = pngio.Image(2, 1, bytearray(bytes((255, 255, 255, 255)) + bytes((0, 0, 0, 255))))
        mean = surface_compose.tone_check._pixel_lab(40, 30, 20)[0]
        out = surface_compose.apply_fit(image, weight, {"mean_original": mean + 8, "mean_result": mean, "contrast_scale": 1.0, "chroma_scale": 1.0})
        self.assertEqual(bytes(out.data[4:8]), bytes((40, 30, 20, 255)))                 # weight 0: untouched
        self.assertGreater(surface_compose.tone_check._pixel_lab(*out.data[0:3])[0], mean + 7)   # weight 1: lighter by about the shift

    def test_join_rows_are_the_zone_edges_of_the_study(self):
        from . import surface_study
        zones = list(surface_study.ZONES.values())
        self.assertEqual(surface_compose.JOINS, (zones[1][1], zones[2][1]))

    # ARC-UI-3104: pointwise specks, the chip, the notch fill and the bevel lines

    @staticmethod
    def _flat(w: int, h: int, v: int) -> "pngio.Image":
        return pngio.Image(w, h, bytearray(bytes((v, v, v, 255)) * (w * h)))

    def test_speck_pixels_take_compact_components_only(self):
        width = 40
        single = {5 * width + 10}                                            # one pixel: a speck
        stroke = {8 * width + x for x in range(20, 30)}                      # ten pixels along a row: a grain stroke, kept
        blob = {(12 + dy) * width + 5 + dx for dy in range(3) for dx in range(3)}   # nine pixels: above SPECK_MAX_PIXELS
        data = bytes(255 if i in single | stroke | blob else 0 for i in range(width * 16))
        pixels, count = surface_compose.speck_pixels(data, width)
        self.assertEqual(count, 1)
        self.assertTrue(single <= pixels)
        self.assertFalse(pixels & stroke)
        self.assertFalse(pixels & blob)
        self.assertEqual(len(pixels), 9)                                     # the speck grown by 1 px

    def test_fill_row_pixels_takes_the_row_neighbours_and_keeps_the_grain(self):
        image = self._flat(12, 3, 100)
        for x in range(12):
            image.data[(1 * 12 + x) * 4] = 100 + 10 * (x % 2)                # row 1: 100/110 grain
        q = 1 * 12 + 6
        image.data[q * 4:q * 4 + 3] = bytes((20, 20, 20))                    # the dark speck
        self.assertEqual(surface_compose.fill_row_pixels(image, {q}), 1)
        self.assertEqual(image.data[q * 4], (110 + 110) // 2)                # mean of the two neighbours (5 and 7)
        self.assertEqual(image.data[(1 * 12 + 4) * 4], 100)                  # the grain beside it is untouched

    def test_fill_row_pixels_leaves_a_pixel_beside_a_bright_line(self):
        image = self._flat(12, 1, 60)
        for c in range(3):
            image.data[3 * 4 + c] = 200                                      # a bright line at x = 3
        q = 4                                                                # right beside it: darker than the line, not than the wood on its right
        before = bytes(image.data)
        self.assertEqual(surface_compose.fill_row_pixels(image, {q}), 0)
        self.assertEqual(bytes(image.data), before)

    def test_fill_row_pixels_leaves_a_dark_pixel_at_an_edge(self):
        image = self._flat(12, 1, 60)
        for x in range(6, 12):
            for c in range(3):
                image.data[x * 4 + c] = 200                                  # wood on the left, a bright line on the right
        for c in range(3):
            image.data[5 * 4 + c] = 20                                       # a dark pixel at the edge: darker than both sides, but the sides differ
        before = bytes(image.data)
        self.assertEqual(surface_compose.fill_row_pixels(image, {5}), 0)
        self.assertEqual(bytes(image.data), before)

    def test_remove_chips_fills_the_longest_bright_run_with_the_darkest_neighbours(self):
        w = 30
        image = self._flat(w, 4, 30)
        for y in range(4):
            for x in range(10, 14):                                          # the white chip
                image.data[(y * w + x) * 4:(y * w + x) * 4 + 3] = bytes((200, 200, 200))
            image.data[(y * w + 20) * 4:(y * w + 20) * 4 + 3] = bytes((150, 150, 150))   # an unrelated bright pixel outside the run: left alone
        surface_compose.CHIP_BOXES, saved = ((5, 0, 25, 4),), surface_compose.CHIP_BOXES
        surface_compose.CHIP_MASTER, master = (w, 4), surface_compose.CHIP_MASTER
        try:
            changed = surface_compose.remove_chips(image)
        finally:
            surface_compose.CHIP_BOXES = saved
            surface_compose.CHIP_MASTER = master
        self.assertEqual(changed, 24)                                  # 4 rows x (4 px run + 2 px grown to the left)
        self.assertEqual(image.pixel(11, 2)[:3], (30, 30, 30))
        self.assertEqual(image.pixel(20, 2)[:3], (150, 150, 150))

    def test_remove_chips_ignores_other_sizes_and_a_bright_fill(self):
        w = 30
        image = self._flat(w, 4, 30)
        for y in range(4):
            for x in range(10, 14):
                image.data[(y * w + x) * 4:(y * w + x) * 4 + 3] = bytes((200, 200, 200))
        before = bytes(image.data)
        saved = surface_compose.CHIP_BOXES
        surface_compose.CHIP_BOXES = ((5, 0, 25, 4),)
        try:
            self.assertEqual(surface_compose.remove_chips(image), 0)              # not the master size
            master = surface_compose.CHIP_MASTER
            surface_compose.CHIP_MASTER = (w, 4)
            for y in range(4):
                for x in range(14, 30):                                          # bright to the image edge: no neighbour to fill from, nothing is removed
                    image.data[(y * w + x) * 4:(y * w + x) * 4 + 3] = bytes((180, 180, 180))
            self.assertEqual(surface_compose.remove_chips(image), 0)
        finally:
            surface_compose.CHIP_BOXES = saved
            surface_compose.CHIP_MASTER = master

    def test_compose_refuses_an_unknown_bevel_mode(self):
        with self.assertRaises(surface_compose.ComposeError):
            surface_compose.compose(Path("o.png"), Path("a.png"), Path("out.png"), Path("work"), "foo")

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_fill_notches_closes_a_narrow_non_wood_notch_and_keeps_a_wide_gap(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            src, out = tmp_path / "w.png", tmp_path / "w2.png"
            subprocess.run(["magick", "-size", "120x80", "xc:white", "-fill", "black", "-draw", "rectangle 20,0 24,30", "-draw", "rectangle 60,0 100,30", str(src)], check=True)
            surface_compose.fill_notches(src, out)
            data = surface_compose.gray_bytes(out)
        self.assertGreater(data[10 * 120 + 22], 200)                         # a 5 px notch: filled
        self.assertLess(data[10 * 120 + 80], 50)                             # a 40 px gap: kept

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_bevel_weight_takes_a_tall_thin_light_line_and_not_a_horizontal_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            original, solid = tmp_path / "o.png", tmp_path / "s.png"
            subprocess.run(["magick", "-size", "200x140", "xc:gray30", "-fill", "gray80", "-draw", "rectangle 100,10 103,120", "-draw", "rectangle 20,125 90,128", str(original)], check=True)
            subprocess.run(["magick", "-size", "200x140", "xc:white", str(solid)], check=True)
            weight, lines = surface_compose.bevel_weight(original, solid, tmp_path, 200, 140)
            data = surface_compose.gray_bytes(weight)
        self.assertEqual(lines, 1)
        self.assertGreater(data[60 * 200 + 101], 200)                        # on the vertical line
        self.assertGreater(data[60 * 200 + 101 - surface_compose.BEVEL_GROW], 100)   # and the grown band
        self.assertLess(data[126 * 200 + 50], 20)                            # the horizontal line is not taken


    # ARC-UI-3100: the pipeline step (zone spec, erase boxes, kit parts, gates, provenance)

    def test_load_spec_reads_a_family_and_refuses_bad_ones(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.json"
            path.write_text(json.dumps({"id": "ctx-logbook", "screen": "ctx-logbook", "joins": [148], "bevel_band": None, "erase_boxes": [[10, 20, 30, 40]],
                                        "kit_parts": [{"id": "plate.x", "file": "x.png", "x": 5, "y": 6}]}), encoding="utf-8")
            spec = surface_compose.load_spec(path)
            self.assertEqual((spec.id, spec.screen, spec.joins, spec.bevel_band, spec.chip_boxes), ("ctx-logbook", "ctx-logbook", (148,), None, ()))
            self.assertEqual(spec.erase_boxes, ((10, 20, 30, 40),))
            self.assertEqual(spec.kit_parts[0]["id"], "plate.x")
            for bad in ({"id": "a"}, {"id": "a", "screen": "s", "extra": 1}, {"id": "a", "screen": "s", "erase_boxes": [[5, 5, 5, 9]]},
                        {"id": "a", "screen": "s", "kit_parts": [{"id": "p", "file": "f"}]}, {"id": "a", "screen": "s", "joins": [0]},
                        {"id": "a", "screen": "s", "bevel_band": [9, 3]}, {"id": "../../x", "screen": "s"}, {"id": ".hidden", "screen": "s"}, {"id": "a/b", "screen": "s"},
                        {"id": "a", "screen": "s", "joins": []}, {"id": "a", "screen": "s", "erase_boxes": None}, {"id": "a", "screen": "s", "bevel_band": ["a", "b"]},
                        {"id": "a", "screen": "s", "kit_parts": [{"id": "p", "file": 5, "x": 0, "y": 0}]}, {"id": "a", "screen": "s", "erase_boxes": [[True, 0, 3, 3]]},
                        {"id": "a", "screen": "s", "joins": [True]}, {"id": "a", "screen": "s", "kit_parts": None}, None, 5, [1]):
                path.write_text(json.dumps(bad), encoding="utf-8")
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.load_spec(path)

    def test_load_spec_reads_the_island_fill_and_dot_band_fields(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.json"
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "island_min": 4000, "fill_boxes": [[1, 2, 30, 40]], "dot_bands": [[0, 148], [1588, 2160]]}), encoding="utf-8")
            spec = surface_compose.load_spec(path)
            self.assertEqual((spec.island_min, spec.fill_boxes, spec.dot_bands), (4000, ((1, 2, 30, 40),), ((0, 148), (1588, 2160))))
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x"}), encoding="utf-8")
            spec = surface_compose.load_spec(path)
            self.assertEqual((spec.island_min, spec.fill_boxes, spec.dot_bands), (0, (), None))        # defaults: the inventory behaves as before
            for bad in ({"island_min": -1}, {"island_min": True}, {"island_min": "9"}, {"dot_bands": [[5, 5]]}, {"dot_bands": [[0]]}, {"dot_bands": 3}, {"dot_bands": [["a", "b"]]},
                        {"fill_boxes": [[5, 5, 5, 9]]}):
                path.write_text(json.dumps({"id": "a", "screen": "s", **bad}), encoding="utf-8")
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.load_spec(path)

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_halo_box_gives_the_dark_fringe_round_a_light_part_to_the_wood_and_keeps_the_part(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, solid, out = tmp_path / "o.png", tmp_path / "s.png", tmp_path / "f.png"
            # dark wood (luma 30), a dark ring (the old shadow, luma 5, under the dark-item cut of the fill rule) round a light disc (luma 150) with a dark spot inside; the weight is black (no wood) in the whole ring and disc
            subprocess.run(["magick", "-size", "240x240", "xc:gray(30)", "-fill", "gray(5)", "-draw", "circle 120,120 120,60", "-fill", "gray(150)", "-draw", "circle 120,120 120,90",
                            "-fill", "gray(25)", "-draw", "circle 120,120 120,112", str(orig)], check=True)
            subprocess.run(["magick", "-size", "240x240", "xc:black", "-fill", "white", "-draw", "rectangle 0,0 20,239", str(solid)], check=True)
            surface_compose.fill_between_items(orig, solid, ((30, 30, 210, 210),), out, 240, 240, surface_compose.HALO_BRIGHT, None, surface_compose.HALO_DILATE, surface_compose.HALO_CLOSE)
            data = surface_compose.gray_bytes(out)
        at = lambda x, y: data[y * 240 + x]
        self.assertGreater(at(120, 65), 200)   # the dark ring outside the disc: the Codex wood now
        self.assertLess(at(120, 100), 60)      # the disc: the original's
        self.assertLess(at(120, 120), 60)      # the dark spot inside the disc is part of it (closed and filled), not wood
        self.assertLess(at(120, 20), 60)       # outside the box: untouched
        self.assertGreater(at(10, 120), 200)   # the weight that was already 1 stays 1

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_apply_box_fills_runs_the_halo_boxes_of_the_spec_and_nothing_without_them(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, solid = tmp_path / "o.png", tmp_path / "s.png"
            subprocess.run(["magick", "-size", "240x240", "xc:gray(30)", "-fill", "gray(5)", "-draw", "circle 120,120 120,60", "-fill", "gray(150)", "-draw", "circle 120,120 120,90", str(orig)], check=True)
            subprocess.run(["magick", "-size", "240x240", "xc:black", str(solid)], check=True)
            surface_compose.apply_box_fills(orig, solid, surface_compose.ZoneSpec(id="z"), 240, 240)
            self.assertLess(surface_compose.gray_bytes(solid)[65 * 240 + 120], 60)      # no halo box: untouched
            surface_compose.apply_box_fills(orig, solid, surface_compose.ZoneSpec(id="z", halo_boxes=((30, 30, 210, 210),)), 240, 240)
            data = surface_compose.gray_bytes(solid)
        self.assertGreater(data[65 * 240 + 120], 200)   # the ring is wood now
        self.assertLess(data[100 * 240 + 120], 60)      # the part stays

    def test_load_spec_reads_halo_boxes_and_refuses_a_bad_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.json"
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "halo_boxes": [[1, 2, 30, 40]]}), encoding="utf-8")
            self.assertEqual(surface_compose.load_spec(path).halo_boxes, ((1, 2, 30, 40),))
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x"}), encoding="utf-8")
            self.assertEqual(surface_compose.load_spec(path).halo_boxes, ())
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "halo_boxes": [[5, 5, 5, 9]]}), encoding="utf-8")
            with self.assertRaises(surface_compose.ComposeError):
                surface_compose.load_spec(path)

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_part_box_gives_the_fringe_to_the_wood_and_the_whole_part_to_the_original(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, solid = tmp_path / "o.png", tmp_path / "s.png"
            # dark neutral wood, a dark fringe round a gold frame (a plate: gold rim, dark field; the part is its gold, not its light) as a rectangle; the weight leaks (white) into the frame's left tip and its dark field
            subprocess.run(["magick", "-size", "240x240", "xc:rgb(40,36,32)", "-fill", "rgb(15,12,10)", "-draw", "rectangle 50,70 190,170", "-fill", "rgb(190,150,50)", "-draw", "rectangle 60,80 180,160",
                            "-fill", "rgb(20,18,16)", "-draw", "rectangle 70,90 170,150", str(orig)], check=True)
            subprocess.run(["magick", str(orig), "-fill", "rgb(120,90,40)", "-draw", "rectangle 45,121 47,123", str(orig)], check=True)   # a warm speck of old wood (a 3 x 3 px group) 3 px beside the rim
            subprocess.run(["magick", "-size", "240x240", "xc:black", "-fill", "white", "-draw", "rectangle 0,0 20,239", "-draw", "rectangle 60,100 75,120", "-draw", "rectangle 100,100 120,120", str(solid)], check=True)
            surface_compose.apply_box_fills(orig, solid, surface_compose.ZoneSpec(id="z", part_boxes=((30, 30, 210, 210),)), 240, 240)
            data = surface_compose.gray_bytes(solid)
        at = lambda x, y: data[y * 240 + x]
        self.assertGreater(at(55, 120), 200)    # the old fringe outside the rim is the Codex wood
        self.assertLess(at(66, 110), 60)        # the leak inside the rim is the original's again (a halo box would leave it wood)
        self.assertLess(at(110, 110), 60)       # and so is the leak in the dark field
        self.assertGreater(at(10, 120), 200)    # the weight outside the box stays
        self.assertGreater(at(46, 122), 200)    # a warm speck of the old wood beside the part is no part: the speck filter drops it before the growing and the closing

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_part_box_takes_its_own_closing_radius(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, solid = tmp_path / "o.png", tmp_path / "s.png"
            # two light bars 30 px apart: closed by 10 they are two parts, closed by 20 one part with the gap between them inside it
            subprocess.run(["magick", "-size", "240x120", "xc:rgb(40,36,32)", "-fill", "rgb(190,150,50)", "-draw", "rectangle 40,40 100,80", "-draw", "rectangle 130,40 190,80", str(orig)], check=True)
            for close, expect_wood in ((10, True), (20, False)):
                subprocess.run(["magick", "-size", "240x120", "xc:black", str(solid)], check=True)
                surface_compose.apply_box_fills(orig, solid, surface_compose.ZoneSpec(id="z", part_boxes=((20, 20, 220, 100, close),)), 240, 120)
                self.assertEqual(surface_compose.gray_bytes(solid)[60 * 240 + 115] > 200, expect_wood, msg=f"close {close}")

    def test_load_spec_reads_part_boxes_with_an_optional_closing_radius_and_refuses_a_bad_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.json"
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "part_boxes": [[1, 2, 30, 40], [5, 6, 50, 60, 18]]}), encoding="utf-8")
            self.assertEqual(surface_compose.load_spec(path).part_boxes, ((1, 2, 30, 40), (5, 6, 50, 60, 18)))
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x"}), encoding="utf-8")
            self.assertEqual(surface_compose.load_spec(path).part_boxes, ())
            for bad in ([[5, 5, 5, 9]], [[1, 2, 30, 40, 0]], [[1, 2, 30, 40, 100000]], [[1, 2, 30]], [[1, 2, 30, 40, 5, 6]], "x"):
                path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "part_boxes": bad}), encoding="utf-8")
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.load_spec(path)

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_disc_box_gives_the_corners_round_a_disc_to_the_wood_and_keeps_the_disc_whole(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            solid, out = tmp_path / "s.png", tmp_path / "f.png"
            # the weight is black (no wood) except a leak of wood inside the disc (a 20 x 20 square at its centre) and a column of wood outside the box
            subprocess.run(["magick", "-size", "240x240", "xc:black", "-fill", "white", "-draw", "rectangle 110,110 129,129", "-draw", "rectangle 230,0 239,239", str(solid)], check=True)
            surface_compose.fill_outside_discs(solid, ((20, 20, 220, 220, 40, 40, 200, 200),), out, 240, 240)
            data = surface_compose.gray_bytes(out)
        at = lambda x, y: data[y * 240 + x]
        self.assertGreater(at(25, 25), 200)     # the corner of the box, outside the ellipse: wood
        self.assertGreater(at(215, 120), 200)   # between the disc and the box's edge: wood
        self.assertLess(at(120, 120), 60)       # the leak inside the disc is the original's again
        self.assertLess(at(120, 60), 60)        # the disc itself: the original's
        self.assertLess(at(10, 10), 60)         # outside the box: untouched
        self.assertGreater(at(235, 120), 200)   # outside the box, the weight that was 1 stays 1

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_disc_casts_a_soft_shadow_down_and_right_on_the_wood_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            image, solid = tmp_path / "i.png", tmp_path / "s.png"
            subprocess.run(["magick", "-size", "300x300", "xc:gray(200)", str(image)], check=True)
            # the wood weight: 1 everywhere except the disc (centre 120,120, radius 40) and a neighbour at the lower right (x 200..300, y 150..300) that keeps the original's pixels
            subprocess.run(["magick", "-size", "300x300", "xc:white", "-fill", "black", "-draw", "circle 120,120 120,80", "-draw", "rectangle 200,150 299,299", str(solid)], check=True)
            surface_compose.cast_disc_shadows(image, solid, ((60, 60, 180, 180, 80, 80, 160, 160),), 300, 300)
            data = surface_compose.gray_bytes(image)
            surface_compose.cast_disc_shadows(image, solid, (), 300, 300)     # no disc: untouched
            self.assertEqual(surface_compose.gray_bytes(image), data)
        at = lambda x, y: data[y * 300 + x]
        self.assertLess(at(150, 155), 120)                     # just outside the disc's lower right: dark
        self.assertGreater(at(85, 85), 190)                    # just outside its upper left: no shadow
        self.assertEqual(at(120, 120), 200)                    # the disc itself
        self.assertEqual(at(250, 200), 200)                    # the neighbour keeps its pixels
        self.assertGreater(at(10, 290), 195)                   # far away: untouched

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_disc_box_may_be_an_oval_and_the_spec_runs_it_after_the_halo_boxes(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, solid = tmp_path / "o.png", tmp_path / "s.png"
            subprocess.run(["magick", "-size", "240x120", "xc:gray(30)", "-fill", "gray(150)", "-draw", "rectangle 105,45 135,75", str(orig)], check=True)
            subprocess.run(["magick", "-size", "240x120", "xc:black", str(solid)], check=True)
            spec = surface_compose.ZoneSpec(id="z", disc_boxes=((10, 10, 230, 110, 20, 30, 220, 90),), halo_boxes=((90, 35, 150, 85),))
            surface_compose.apply_box_fills(orig, solid, spec, 240, 120)
            data = surface_compose.gray_bytes(solid)
        self.assertGreater(data[20 * 240 + 120], 200)    # above the oval (y 20, inside the box but outside the ellipse y 30..89): wood
        self.assertLess(data[60 * 240 + 120], 60)        # the oval
        self.assertLess(data[60 * 240 + 95], 60)         # inside the oval and the halo box but beside the light square: the disc runs after the halo, so the original's
        self.assertGreater(data[60 * 240 + 15], 200)     # left of the oval's tip, inside the box: wood

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_disc_outside_its_box_or_the_image_is_an_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            solid = tmp_path / "s.png"
            subprocess.run(["magick", "-size", "100x100", "xc:black", str(solid)], check=True)
            for bad in ((10, 10, 50, 50, 5, 20, 40, 40), (10, 10, 120, 50, 20, 20, 40, 40)):
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.fill_outside_discs(solid, (bad,), solid, 100, 100)
            surface_compose.fill_outside_discs(solid, (), solid, 100, 100)    # no disc: nothing to do

    def test_load_spec_reads_disc_boxes_and_refuses_a_bad_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.json"
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "disc_boxes": [[0, 0, 100, 100, 10, 10, 90, 90]]}), encoding="utf-8")
            self.assertEqual(surface_compose.load_spec(path).disc_boxes, ((0, 0, 100, 100, 10, 10, 90, 90),))
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x"}), encoding="utf-8")
            self.assertEqual(surface_compose.load_spec(path).disc_boxes, ())
            for bad in ([[0, 0, 100, 100, 10, 10, 90]], [[0, 0, 100, 100, 10, 10, 110, 90]], [[0, 0, 100, 100, 50, 10, 50, 90]], [[0, 0, 100, 100, 10, 10, 90, 90.5]], "x"):
                path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "disc_boxes": bad}), encoding="utf-8")
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.load_spec(path)

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_a_plate_lays_dark_corner_triangles_cut_by_the_larger_ellipse_and_nothing_else(self):
        with tempfile.TemporaryDirectory() as tmp:
            image = Path(tmp) / "i.png"
            subprocess.run(["magick", "-size", "200x200", "xc:gray(100)", str(image)], check=True)
            disc = (20, 20, 180, 160, 40, 40, 160, 140)       # the ring's rim box: centre (100, 90), semi-axes 60 and 50
            plate = (40, 40, 160, 140, 70, 58)                 # the plate's rectangle and the corner ellipse (70, 58), concentric and larger than the ring
            painted = surface_compose.lay_plates(image, (plate,), (disc,), 200, 200)
            data = surface_compose.gray_bytes(image)
            self.assertEqual(surface_compose.lay_plates(image, (), (disc,), 200, 200), 0)       # no plate: nothing to do
            for bad, discs in (((40, 40, 260, 140, 70, 58), (disc,)), ((10, 10, 30, 30, 70, 58), (disc,)), (plate, ())):     # outside the image; in no disc box
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.lay_plates(image, (bad,), discs, 200, 200)
        at = lambda x, y: data[y * 200 + x]
        self.assertGreater(painted, 600)
        fill = round(100 * surface_compose.PLATE_DEPTH)
        self.assertTrue(fill - 6 <= at(46, 46) <= fill + 6, msg=str(at(46, 46)))     # a corner triangle: the dark fill (a share of the wood's luma)
        self.assertTrue(fill - 6 <= at(154, 134) <= fill + 6, msg=str(at(154, 134)))
        self.assertGreater(at(42, 45), at(46, 46) + 5)    # the thin rim 2 px inside the straight left leg is lighter than the fill
        self.assertLess(at(42, 45), 100)                  # and darker than the wood
        self.assertEqual(at(42, 90), 100)                 # the middle of the plate's side: no plate (the corner ellipse is wider than the rectangle there)
        self.assertEqual(at(100, 41), 100)                # the middle of its top: none either
        self.assertEqual(at(60, 80), 100)                 # inside the ring: untouched
        self.assertEqual(at(30, 30), 100)                 # outside the rectangle: untouched
        self.assertEqual(at(39, 45), 100)                 # the leg is hard: one pixel outside it is the wood
        self.assertTrue(fill - 6 <= at(40, 45) <= fill + 6, msg=str(at(40, 45)))     # the leg's outermost pixel is plate
        edge = [at(x, y) for y in range(44, 62) for x in range(44, 66) if fill + 4 < at(x, y) < 97]
        self.assertTrue(edge, msg="the arc is anti-aliased: a partly covered pixel between the fill and the wood")

    def test_load_spec_reads_plate_boxes_and_refuses_a_bad_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.json"
            disc = [0, 0, 120, 120, 10, 10, 110, 110]          # ring semi-axes 50 and 50
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "disc_boxes": [disc], "plate_boxes": [[10, 10, 110, 110, 70, 60]]}), encoding="utf-8")
            self.assertEqual(surface_compose.load_spec(path).plate_boxes, ((10, 10, 110, 110, 70, 60),))
            # a 4-number entry, an empty box, no list, a plate outside every disc box, a corner ellipse not larger than the ring, a non-positive axis
            for bad in ([[10, 10, 110, 110]], [[50, 0, 50, 100, 70, 60]], "x", [[10, 10, 130, 110, 70, 60]], [[10, 10, 110, 110, 50, 60]], [[10, 10, 110, 110, 70, 0]]):
                path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "disc_boxes": [disc], "plate_boxes": bad}), encoding="utf-8")
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.load_spec(path)
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "plate_boxes": [[10, 10, 110, 110, 70, 60]]}), encoding="utf-8")      # no disc box at all
            with self.assertRaises(surface_compose.ComposeError):
                surface_compose.load_spec(path)

    def test_the_inventory_plates_lie_inside_their_disc_boxes(self):
        spec = surface_compose.load_spec(Path(__file__).parent / "zone_specs" / "ctx-inventory.json")
        self.assertEqual(len(spec.plate_boxes), 2)
        for plate in spec.plate_boxes:
            self.assertTrue(any(d[0] <= plate[0] and d[1] <= plate[1] and plate[2] <= d[2] and plate[3] <= d[3] for d in spec.disc_boxes), msg=str(plate))

    def test_the_committed_specs_load_and_their_discs_lie_in_the_master(self):
        for path in sorted((Path(__file__).parent / "zone_specs").glob("ctx-*.json")):
            spec = surface_compose.load_spec(path)
            for box in spec.halo_boxes + tuple(p[:4] for p in spec.part_boxes) + tuple(d[:4] for d in spec.disc_boxes):
                self.assertTrue(box[2] <= 2880 and box[3] <= 2160, msg=f"{path.name} {box}")

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_remove_islands_drops_a_leak_and_a_bay_joined_by_a_thin_channel_and_keeps_the_wood(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            src, out = tmp_path / "w.png", tmp_path / "w2.png"
            # white wood, a black metal block with a 40 x 30 white bay inside joined to the wood by a 3 px channel, and a white 30 x 30 leak inside a second black block
            subprocess.run(["magick", "-size", "400x200", "xc:white", "-fill", "black", "-draw", "rectangle 20,20 120,120", "-fill", "white", "-draw", "rectangle 50,50 90,80",
                            "-draw", "rectangle 88,60 120,62", "-fill", "black", "-draw", "rectangle 200,20 320,150", "-fill", "white", "-draw", "rectangle 240,60 270,90", str(src)], check=True)
            surface_compose.remove_islands(src, out, 700)
            data = surface_compose.gray_bytes(out)
        at = lambda x, y: data[y * 400 + x]
        self.assertLess(at(70, 65), 60)       # the bay: the original's pixels again
        self.assertLess(at(255, 75), 60)      # the leak: dropped
        self.assertGreater(at(350, 180), 200)  # the wood: kept
        self.assertGreater(at(10, 10), 200)

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_remove_islands_leaves_the_feathered_edge_of_the_wood_alone(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            src, out = tmp_path / "w.png", tmp_path / "w2.png"
            subprocess.run(["magick", "-size", "200x100", "xc:white", "-fill", "black", "-draw", "rectangle 80,0 199,99", "-blur", "0x3", str(src)], check=True)
            before = surface_compose.gray_bytes(src)
            surface_compose.remove_islands(src, out, 700)
            after = surface_compose.gray_bytes(out)
        row = 50 * 200
        self.assertEqual(list(before[row + 70:row + 90]), list(after[row + 70:row + 90]))   # the soft edge: no hard step at 50 %
        surface_compose.remove_islands(src, out, 0)                                         # off: returns at once

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_fill_between_items_with_an_item_and_a_box_at_the_image_corner(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, solid, out = tmp_path / "o.png", tmp_path / "s.png", tmp_path / "f.png"
            subprocess.run(["magick", "-size", "120x80", "xc:gray(60)", "-fill", "gray(200)", "-draw", "rectangle 0,0 10,10", str(orig)], check=True)
            subprocess.run(["magick", "-size", "120x80", "xc:black", str(solid)], check=True)
            surface_compose.fill_between_items(orig, solid, ((0, 0, 100, 70),), out, 120, 80)
            data = surface_compose.gray_bytes(out)
        self.assertLess(data[5 * 120 + 5], 60)        # the corner item stays the original's
        self.assertGreater(data[50 * 120 + 60], 200)  # wood elsewhere in the box

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_fill_between_items_puts_wood_between_letters_and_keeps_the_items_and_the_outside(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, solid, out = tmp_path / "o.png", tmp_path / "s.png", tmp_path / "f.png"
            # panel wood (luma 60) with a bright letter block and a black field in the fill box (20,20)-(180,100); the same wood outside the box
            subprocess.run(["magick", "-size", "240x120", "xc:gray(60)", "-fill", "gray(200)", "-draw", "rectangle 40,40 52,60", "-fill", "black", "-draw", "rectangle 120,40 160,70", str(orig)], check=True)
            subprocess.run(["magick", "-size", "240x120", "xc:black", str(solid)], check=True)
            surface_compose.fill_between_items(orig, solid, ((20, 20, 180, 100),), out, 240, 120)
            data = surface_compose.gray_bytes(out)
        at = lambda x, y: data[y * 240 + x]
        self.assertGreater(at(90, 30), 200)    # wood between the items, inside the box: the Codex wood now
        self.assertLess(at(46, 50), 60)        # the letter: the original's
        self.assertLess(at(140, 55), 60)       # the field: the original's
        self.assertLess(at(10, 10), 60)        # outside the box: untouched
        self.assertLess(at(200, 50), 60)

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_original_dots_count_only_inside_the_bands(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            orig, wood, work = tmp_path / "o.png", tmp_path / "w.png", tmp_path / "work"
            work.mkdir()
            draw = []
            for y in (20, 100):
                for x in range(20, 300, 25):
                    draw += ["-draw", f"point {x},{y}"]
            subprocess.run(["magick", "-size", "320x120", "xc:gray(128)", "-fill", "gray(60)", *draw, str(orig)], check=True)
            subprocess.run(["magick", "-size", "320x120", "xc:white", str(wood)], check=True)
            _, everywhere = surface_compose.original_dots(orig, wood, work, 320, 120)
            _, banded = surface_compose.original_dots(orig, wood, work, 320, 120, ((0, 50),))
            _, none = surface_compose.original_dots(orig, wood, work, 320, 120, ((60, 70),))
        self.assertGreater(everywhere, banded)
        self.assertGreater(banded, 0)
        self.assertEqual(none, 0)

    def test_the_inventory_spec_is_the_prototype_and_a_spec_defaults_to_no_chip(self):
        self.assertEqual((surface_compose.INVENTORY.screen, surface_compose.INVENTORY.joins, surface_compose.INVENTORY.bevel_band, surface_compose.INVENTORY.chip_boxes),
                         ("ctx-inventory", (148, 1588), (0, 148), ((1900, 0, 1930, 18),)))     # the prototype's literal values, as of ARC-UI-3104
        self.assertEqual(surface_compose.ZoneSpec(id="x").chip_boxes, ())

    def test_erase_box_at_the_image_corner_uses_the_clipped_ring_and_not_its_own_interior(self):
        image = self._flat(8, 8, 90)
        for y in range(0, 3):
            for x in range(0, 3):
                image.data[(y * 8 + x) * 4:(y * 8 + x) * 4 + 3] = bytes((255, 255, 255))     # box interior, bigger than the clipped ring
        for x in range(3, 5):
            for y in range(0, 5):
                image.data[(y * 8 + x) * 4:(y * 8 + x) * 4 + 3] = bytes((40, 40, 40))        # ring column x=3,4 dark, row y=3,4 stays 90
        surface_compose.erase_boxes(image, ((0, 0, 3, 3),))
        self.assertEqual(image.pixel(1, 1)[:3], (40, 40, 40))        # median of the ring cells {x=3,4 for y=0..4: 10 cells of 40; y=3,4 for x=0..2: 6 cells of 90}

    def test_erase_boxes_fill_with_the_median_of_the_ring_and_stay_inside(self):
        image = self._flat(20, 10, 80)
        for y in range(3, 7):
            for x in range(8, 12):
                image.data[(y * 20 + x) * 4:(y * 20 + x) * 4 + 3] = bytes((250, 250, 250))      # baked text
        self.assertEqual(surface_compose.erase_boxes(image, ((8, 3, 12, 7),)), 16)
        self.assertEqual(image.pixel(9, 4)[:3], (80, 80, 80))
        self.assertEqual(image.pixel(7, 4)[:3], (80, 80, 80))                                   # the ring is not touched
        with self.assertRaises(surface_compose.ComposeError):
            surface_compose.erase_boxes(image, ((15, 5, 25, 9),))

    # ARC-UI-3165: text boxes (text, digits and counters over a ground), paper boxes, wood boxes and the committed zone specs

    def test_load_spec_reads_the_text_paper_and_wood_boxes(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.json"
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x", "text_boxes": [[1, 2, 30, 40]], "paper_boxes": [[5, 6, 70, 80]], "wood_boxes": [[9, 9, 20, 20]]}), encoding="utf-8")
            spec = surface_compose.load_spec(path)
            self.assertEqual((spec.text_boxes, spec.paper_boxes, spec.wood_boxes), (((1, 2, 30, 40),), ((5, 6, 70, 80),), ((9, 9, 20, 20),)))
            path.write_text(json.dumps({"id": "ctx-x", "screen": "ctx-x"}), encoding="utf-8")
            spec = surface_compose.load_spec(path)
            self.assertEqual((spec.text_boxes, spec.paper_boxes, spec.wood_boxes), ((), (), ()))
            for bad in ({"text_boxes": [[5, 5, 5, 9]]}, {"paper_boxes": None}, {"wood_boxes": [[1, 2, 3]]}, {"text_boxes": [[True, 0, 3, 3]]}):
                path.write_text(json.dumps({"id": "a", "screen": "s", **bad}), encoding="utf-8")
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.load_spec(path)

    @staticmethod
    def _paint(image: "pngio.Image", x0: int, y0: int, x1: int, y1: int, rgb) -> None:
        for y in range(y0, y1):
            for x in range(x0, x1):
                image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4] = bytes((*rgb, 255))

    def test_erase_text_takes_the_text_out_of_a_black_field_and_leaves_the_ring_and_the_rest(self):
        image = self._flat(120, 60, 4)
        self._paint(image, 0, 0, 120, 8, (200, 160, 60))                        # a brass rim above the field, outside the box
        for x0 in (30, 44, 58):                                                  # three thick digits, 8 x 20 px
            self._paint(image, x0, 20, x0 + 8, 40, (250, 250, 250))
        before_rim = image.pixel(60, 3)
        replaced = surface_compose.erase_text(image, ((20, 15, 80, 45),))
        self.assertGreater(replaced, 3 * 8 * 20)                                 # the digits and their halo
        for x, y in ((33, 30), (47, 22), (61, 39), (20, 15), (79, 44)):
            self.assertLessEqual(max(image.pixel(x, y)[:3]), 6, (x, y))          # the ground (black field) is back, up to its own grain
        self.assertEqual(image.pixel(60, 3), before_rim)                         # outside the box nothing changes

    def test_erase_text_keeps_the_shading_of_the_ground(self):
        image = self._flat(100, 50, 0)
        for y in range(50):
            for x in range(100):
                self._paint(image, x, y, x + 1, y + 1, (40 + x, 40 + x, 40 + x))   # a horizontal gradient: 40 at the left, 139 at the right
        self._paint(image, 40, 15, 52, 35, (255, 255, 255))
        surface_compose.erase_text(image, ((30, 10, 62, 40),))
        for x in (36, 46, 56):
            self.assertAlmostEqual(image.pixel(x, 25)[0], 40 + x, delta=6)       # the mean of the ground around: the gradient continues through the box

    def test_erase_text_refuses_a_box_outside_and_a_box_that_is_no_text_on_a_ground(self):
        image = self._flat(40, 40, 10)
        with self.assertRaises(surface_compose.ComposeError):
            surface_compose.erase_text(image, ((30, 30, 50, 45),))
        self._paint(image, 10, 10, 30, 30, (250, 250, 250))
        with self.assertRaises(surface_compose.ComposeError):                    # the box is a picture, not text: 100 % differs from the ring
            surface_compose.erase_text(image, ((10, 10, 30, 30),))

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_erase_text_in_paper_mode_removes_blue_ink_from_a_shaded_page_and_not_the_shading(self):
        image = self._flat(160, 90, 0)
        for y in range(90):
            for x in range(160):
                self._paint(image, x, y, x + 1, y + 1, (200 - x // 2, 190 - x // 2, 160 - x // 2))    # a page darkening toward the right, 120 px of it in the box
        for x0 in range(30, 120, 14):
            self._paint(image, x0, 30, x0 + 5, 56, (10, 10, 170))                # blue strokes (bright in blue only)
        self.assertLess(image.pixel(32, 40)[0], 20)
        replaced = surface_compose.erase_text(image, ((20, 20, 135, 70),), paper=True)
        self.assertGreater(replaced, 7 * 5 * 26)
        for x in (32, 60, 88, 116):
            r, g, b = image.pixel(x, 40)[:3]
            self.assertAlmostEqual(r, 200 - x // 2, delta=8, msg=str((x, r)))
            self.assertAlmostEqual(b, 160 - x // 2, delta=8, msg=str((x, b)))     # no blue left, and the shading is the page's

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_force_wood_makes_the_weight_one_in_the_box_and_leaves_the_rest(self):
        with tempfile.TemporaryDirectory() as tmp:
            solid = Path(tmp) / "w.png"
            subprocess.run(["magick", "-size", "100x60", "xc:black", str(solid)], check=True)
            surface_compose.force_wood(solid, ((30, 10, 70, 50),), solid, 100, 60)
            data = pngio.read(solid)
            self.assertGreater(data.pixel(50, 30)[0], 250)                       # the middle of the box is wood
            self.assertLess(data.pixel(5, 5)[0], 5)                              # far from it nothing is
            with self.assertRaises(surface_compose.ComposeError):
                surface_compose.force_wood(solid, ((90, 10, 120, 50),), solid, 100, 60)

    def test_tone_masks_exclude_the_text_and_paper_boxes_in_layout_units_and_only_when_there_are_some(self):
        self.assertIsNone(surface_compose.tone_masks_for(surface_compose.ZoneSpec(id="a", screen="ctx-inventory")))
        spec = surface_compose.ZoneSpec(id="a", screen="ctx-reading", paper_boxes=((800, 340, 1560, 1388),), text_boxes=((3, 7, 40, 80),))
        masks = surface_compose.tone_masks_for(spec)
        exclude = masks["screens"]["ctx-reading"]["exclude"]
        self.assertIn([222, 94, 434, 386], exclude)                          # 800 / 3.6 = 222.2 down, 340 / 3.6 = 94.4 down, 1560 / 3.6 = 433.3 up, 1388 / 3.6 = 385.6 up
        self.assertIn([0, 1, 12, 23], exclude)
        self.assertNotIn([222, 94, 434, 386], surface_compose.tone_check.load_masks()["screens"]["ctx-reading"]["exclude"])   # the file's own masks are not changed

    def test_erase_text_at_the_image_corner_and_with_a_halo_that_leaves_little_ground(self):
        image = self._flat(30, 20, 5)
        self._paint(image, 0, 0, 10, 14, (240, 240, 240))                       # a thick glyph touching the top left corner of the image: no ring above or left of the box
        self.assertGreater(surface_compose.erase_text(image, ((0, 0, 14, 18),)), 10 * 14)
        self.assertLessEqual(max(image.pixel(4, 6)[:3]), 8)
        tight = self._flat(24, 24, 6)
        self._paint(tight, 6, 6, 18, 18, (250, 250, 250))                        # a glyph that fills most of a small box: the widest halo would leave no ground, a narrower one is taken
        surface_compose.erase_text(tight, ((4, 4, 20, 20),))
        self.assertLessEqual(max(tight.pixel(12, 12)[:3]), 8)

    def test_compose_refuses_a_box_outside_the_image_before_any_work(self):
        spec = surface_compose.ZoneSpec(id="a", screen="ctx-inventory", text_boxes=((10, 10, 5000, 50),))
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            pngio.write(root / "o.png", self._flat(40, 40, 9))
            with self.assertRaises(surface_compose.ComposeError):
                surface_compose.compose(root / "o.png", root / "o.png", root / "out.png", root / "work", spec=spec)
            self.assertFalse((root / "out.png").exists())

    def test_the_committed_zone_specs_load_and_their_boxes_lie_in_the_window_zone(self):
        folder = Path(__file__).parent / "zone_specs"
        names = sorted(p.name for p in folder.glob("*.json"))
        self.assertEqual(names, ["ctx-character.json", "ctx-inventory.json", "ctx-logbook.json", "ctx-reading.json", "ctx-schematic.json"])
        for name in names:
            spec = surface_compose.load_spec(folder / name)
            self.assertEqual(spec.id + ".json", name)
            for kind in ("text_boxes", "paper_boxes", "wood_boxes", "erase_boxes"):
                for x0, y0, x1, y1 in getattr(spec, kind):
                    self.assertTrue(0 <= x0 < x1 <= 2880 and 148 <= y0 < y1 <= 1588, (name, kind, (x0, y0, x1, y1)))    # inside the window zone of the 2880 x 2160 master
        self.assertEqual(len(surface_compose.load_spec(folder / "ctx-character.json").wood_boxes), 8)       # the eight stat names
        self.assertTrue(surface_compose.load_spec(folder / "ctx-logbook.json").paper_boxes and surface_compose.load_spec(folder / "ctx-reading.json").paper_boxes)

    def test_kit_parts_are_laid_over_with_alpha_and_recorded(self):
        with tempfile.TemporaryDirectory() as tmp:
            part = Path(tmp) / "plate.png"
            pngio.write(part, pngio.Image(2, 1, bytearray(bytes((200, 100, 0, 255)) + bytes((0, 0, 0, 0)))))
            image = self._flat(6, 4, 50)
            records = surface_compose.lay_kit_parts(image, ({"id": "plate.round_button", "file": "plate.png", "x": 3, "y": 2},), Path(tmp))
            self.assertEqual(image.pixel(3, 2)[:3], (200, 100, 0))                              # opaque pixel replaces the stand-in
            self.assertEqual(image.pixel(4, 2)[:3], (50, 50, 50))                               # alpha 0 leaves it
            self.assertEqual(records[0]["id"], "plate.round_button")
            self.assertEqual((records[0]["size"], records[0]["at"]), ([2, 1], [3, 2]))
            self.assertEqual(len(records[0]["sha256"]), 64)
            with self.assertRaises(surface_compose.ComposeError):
                surface_compose.lay_kit_parts(image, ({"id": "p", "file": str(part), "x": 5, "y": 2},))      # leaves the image
            with self.assertRaises(surface_compose.ComposeError):
                surface_compose.lay_kit_parts(image, ({"id": "p", "file": "missing.png", "x": 0, "y": 0},), Path(tmp))

    def test_half_covered_kit_part_edge_is_blended(self):
        with tempfile.TemporaryDirectory() as tmp:
            part = Path(tmp) / "edge.png"
            pngio.write(part, pngio.Image(1, 1, bytearray(bytes((200, 200, 200, 128)))))
            image = self._flat(1, 1, 0)
            surface_compose.lay_kit_parts(image, ({"id": "e", "file": str(part), "x": 0, "y": 0},))
            self.assertEqual(image.pixel(0, 0)[0], 100)

    def test_no_measured_join_is_no_passed_seam_gate(self):
        self.assertFalse(surface_compose.evaluate_gates([], {"pass": True, "failed": []})["passed"])

    def test_main_exit_codes_error_two_gate_failure_one_pass_zero(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            bad = root / "bad.json"
            bad.write_text("[1]", encoding="utf-8")
            argv = ["build", "--original", "o.png", "--a", "a.png", "--out", str(root / "out.png"), "--work", str(root / "w"), "--provenance", str(root / "rec.json")]
            self.assertEqual(surface_compose.main(argv + ["--spec", str(bad)]), 2)                  # a wrong spec: an error, not a traceback
            saved = surface_compose.build
            try:
                for passed, code in ((False, 1), (True, 0)):
                    record = {"zone": "z", "seams": [], "wood": {"after": {"mean_l": 1.0, "std_l": 1.0}, "target": {"mean_l": 1.0, "std_l": 1.0}}, "counts": {"erased_pixels": 0, "text_pixels": 0},
                              "kit_parts": [], "gates": {"passed": passed, "seam_target": 1.8, "seams_within_swi_ver_5": True, "tone_pass": passed, "tone_failed": []}}
                    surface_compose.build = lambda *a, **k: record
                    self.assertEqual(surface_compose.main(argv), code)
            finally:
                surface_compose.build = saved

    def test_gates_need_every_seam_within_the_target_and_the_tone_check(self):
        tone = {"pass": True, "failed": []}
        ok = surface_compose.evaluate_gates([{"join": "y=148", "ratio": 0.96}, {"join": "y=1588", "ratio": 1.7}], tone)
        self.assertTrue(ok["passed"])
        self.assertFalse(ok["seams_within_swi_ver_5"])                                          # 1.7 is over 1.5 but under the owner's target of 1.8
        seam = surface_compose.evaluate_gates([{"join": "y=148", "ratio": 1.85}], tone)
        self.assertFalse(seam["passed"])
        self.assertFalse(seam["seams_pass"])
        failed_tone = surface_compose.evaluate_gates([{"join": "y=148", "ratio": 1.0}], {"pass": False, "failed": ["wood"]})
        self.assertFalse(failed_tone["passed"])
        self.assertEqual(failed_tone["tone_failed"], ["wood"])

    def test_provenance_record_holds_hashes_numbers_and_gates_but_no_pixels(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for name in ("o.png", "a.png", "r.png"):
                (root / name).write_bytes(name.encode())
            stats = {"mean_l": 20.0, "std_l": 4.0, "mean_chroma": 9.0}
            info = {"speck_components": 3, "dots_kept": 4, "bevel_lines": 2, "chip_pixels": 0, "erased_pixels": 16, "text_pixels": 7, "kit_parts": [{"id": "p"}], "wood_target": stats,
                    "wood_before": stats, "wood_after": stats, "fit": {"mean_original": 20.0}}
            tone = {"pass": True, "failed": [], "regions": {"wood": {"pass": True, "values": {"mean_l_delta": 0.1}}, "brass": {"absent": 3}}}
            when = datetime.datetime(2026, 11, 24, 12, 0, tzinfo=datetime.timezone.utc)
            record = surface_compose.provenance_record(surface_compose.ZoneSpec(id="ctx-x", screen="ctx-hud"), root / "o.png", root / "a.png", root / "r.png", info,
                                                       [{"join": "y=148", "ratio": 1.0}], tone, "lighten", when)
            json.dumps(record)                                                                  # serialisable
            self.assertEqual(record["schema"], surface_compose.SCHEMA)
            self.assertEqual(record["date"], "2026-11-24T12:00:00Z")
            self.assertEqual(record["inputs"]["original"]["sha256"], surface_compose.sha256_file(root / "o.png"))
            self.assertEqual(record["counts"]["erased_pixels"], 16)
            self.assertEqual(record["counts"]["text_pixels"], 7)
            self.assertNotIn("brass", record["tone"]["regions"])                                # an absent region is left out
            self.assertTrue(record["gates"]["passed"])
            self.assertEqual(record["spec"]["screen"], "ctx-hud")


if __name__ == "__main__":
    unittest.main()
