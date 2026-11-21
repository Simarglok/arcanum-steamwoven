"""ARC-RES-2457: tests of the pure parts of the method A composition (dot rows, components, tone fit numbers) on synthetic data; no ImageMagick, no art."""
from __future__ import annotations

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


if __name__ == "__main__":
    unittest.main()
