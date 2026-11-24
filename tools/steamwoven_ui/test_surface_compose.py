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
                        {"id": "a", "screen": "s", "bevel_band": [9, 3]}):
                path.write_text(json.dumps(bad), encoding="utf-8")
                with self.assertRaises(surface_compose.ComposeError, msg=str(bad)):
                    surface_compose.load_spec(path)

    def test_the_inventory_spec_is_the_prototype_and_a_spec_defaults_to_no_chip(self):
        self.assertEqual((surface_compose.INVENTORY.screen, surface_compose.INVENTORY.joins, surface_compose.INVENTORY.chip_boxes),
                         ("ctx-inventory", surface_compose.JOINS, surface_compose.CHIP_BOXES))
        self.assertEqual(surface_compose.ZoneSpec(id="x").chip_boxes, ())

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
            info = {"speck_components": 3, "dots_kept": 4, "bevel_lines": 2, "chip_pixels": 0, "erased_pixels": 16, "kit_parts": [{"id": "p"}], "wood_target": stats,
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
            self.assertNotIn("brass", record["tone"]["regions"])                                # an absent region is left out
            self.assertTrue(record["gates"]["passed"])
            self.assertEqual(record["spec"]["screen"], "ctx-hud")


if __name__ == "__main__":
    unittest.main()
