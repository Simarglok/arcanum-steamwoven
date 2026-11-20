"""ARC-RES-2455: tests of the tone check on synthetic images (no art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import pngio, tone_check


def banded(w: int, h: int, colors: list[tuple[int, int, int]], noise: int = 0) -> pngio.Image:
    """Vertical colour bands; `noise` adds a +-noise checkerboard (texture without a change of the mean)."""
    row = bytearray()
    band = w // len(colors)
    for x in range(w):
        r, g, b = colors[min(x // band, len(colors) - 1)]
        row += bytes((r, g, b, 255))
    rows = bytearray()
    for y in range(h):
        line = bytearray(row)
        if noise:
            for x in range(w):
                d = noise if (x + y) % 2 else -noise
                for c in range(3):
                    line[x * 4 + c] = max(0, min(255, line[x * 4 + c] + d))
        rows += line
    return pngio.Image(w, h, rows)


BASE = [(60, 40, 30), (110, 80, 50), (30, 30, 40), (90, 70, 60)]


class ToneCheckTest(unittest.TestCase):
    def report(self, original: pngio.Image, result: pngio.Image) -> dict:
        return tone_check.compare_labs(tone_check.blurred_lab(original), tone_check.blurred_lab(result))

    def test_identical_and_texture_only_pass(self) -> None:
        original = banded(800, 600, BASE)
        same = self.report(original, original)
        self.assertTrue(same["pass"], same)
        self.assertLess(same["values"]["mean_delta_e"], 0.01)
        textured = self.report(original, banded(800, 600, BASE, noise=12))  # fine texture is blurred away
        self.assertTrue(textured["pass"], textured)

    def test_larger_result_is_reduced_first(self) -> None:
        original = banded(800, 600, BASE)
        big = banded(1448, 1086, BASE)
        report = self.report(original, big)
        self.assertTrue(report["pass"], report)

    def test_brighter_and_more_saturated_fail_with_the_right_flags(self) -> None:
        original = banded(800, 600, BASE)
        brighter = self.report(original, banded(800, 600, [(c[0] + 40, c[1] + 40, c[2] + 40) for c in BASE]))
        self.assertIn("mean_l_delta", brighter["failed"])
        self.assertFalse(brighter["pass"])
        vivid = self.report(original, banded(800, 600, [(110, 60, 20), (190, 120, 30), (30, 30, 60), (150, 90, 50)]))
        self.assertIn("mean_chroma_ratio", vivid["failed"])

    def test_contrast_gain_fails(self) -> None:
        original = banded(800, 600, BASE)
        harsh = self.report(original, banded(800, 600, [(20, 10, 10), (170, 130, 80), (5, 5, 15), (140, 110, 90)]))
        self.assertIn("contrast_ratio", harsh["failed"])

    SCREEN = {"strips": False, "exclude": [[500, 100, 700, 300]], "materials": {"wood": {"class": "wood", "zone": [[0, 0, 400, 600]]}, "paper": {"class": "paper", "zone": [[400, 0, 800, 600]]}}}
    WOOD, PAPER = (80, 58, 42), (200, 180, 140)

    def screen_image(self, wood, paper, patch=None) -> pngio.Image:
        image = banded(800, 600, [wood, paper])
        if patch:  # a changed block (x0, y0, x1, y1, colour)
            for y in range(patch[1], patch[3]):
                for x in range(patch[0], patch[2]):
                    image.data[(y * 800 + x) * 4:(y * 800 + x) * 4 + 3] = bytes(patch[4])
        return image

    def test_masks_pick_the_material_by_class_zone_and_exclusion(self) -> None:
        lab = tone_check.to_lab(tone_check.reduce_to_800x600(self.screen_image(self.WOOD, self.PAPER)))
        masks = tone_check.material_masks(self.SCREEN, [], lab)
        self.assertEqual(sum(masks["wood"]), 400 * 600)
        self.assertEqual(sum(masks["paper"]), 400 * 600 - 200 * 200)  # the excluded block is out
        self.assertEqual(masks["wood"][0 * 800 + 450], 0.0)

    def test_per_material_report_and_content_is_ignored(self) -> None:
        original = self.screen_image(self.WOOD, self.PAPER)
        screen = lambda result: tone_check.compare_screen(original, result, self.SCREEN, [])
        same = screen(original)
        self.assertTrue(same["pass"], same)
        self.assertEqual(sorted(same["regions"]), ["all", "paper", "wood"])
        content = screen(self.screen_image(self.WOOD, self.PAPER, (500, 100, 700, 300, (10, 10, 10))))  # text/picture in the excluded block only
        self.assertTrue(content["pass"], content)
        bright_wood = screen(self.screen_image((140, 100, 70), self.PAPER))
        self.assertIn("wood", bright_wood["failed"])
        self.assertTrue(bright_wood["regions"]["paper"]["pass"])
        self.assertFalse(bright_wood["pass"])

    def test_missing_material_is_reported_absent_not_failed(self) -> None:
        original = self.screen_image(self.WOOD, self.PAPER)
        brass_only = {"strips": False, "materials": {"brass": {"class": "brass"}}}
        report = tone_check.compare_screen(original, original, brass_only, [])
        self.assertIn("absent", report["regions"]["brass"])
        self.assertTrue(report["pass"])

    def test_masked_blur_ignores_pixels_outside_the_mask(self) -> None:
        mask = [1.0 if x % 2 == 0 else 0.0 for _ in range(600) for x in range(800)]  # every other column is material
        lab = [(50.0, 0.0, 0.0) if m else (100.0, 30.0, 30.0) for m in mask]  # the rest is bright and coloured
        coverage = tone_check.box_blur(tone_check._half(mask), tone_check.HALF_RADIUS, tone_check.HALF_W, tone_check.HALF_H)
        blurred = tone_check.masked_blur(lab, mask, coverage)
        for cell in (0, 5000, 100000):
            self.assertAlmostEqual(blurred[cell][0], 50.0, places=3)
            self.assertAlmostEqual(blurred[cell][1], 0.0, places=3)

    def test_fit_matches_the_means_and_reports_convergence(self) -> None:
        screen = {"strips": False, "materials": {"paper": {"class": "paper", "zone": [[0, 0, 800, 600]]}}}
        original = banded(800, 600, [(200, 190, 160)])
        result = banded(800, 600, [(240, 215, 150)])  # brighter and more yellow
        fitted, info = tone_check.fit_material(original, result, screen, [], "paper")
        self.assertTrue(info["converged"], info)
        self.assertTrue(tone_check.compare_screen(original, fitted, screen, [])["pass"])
        self.assertEqual((fitted.w, fitted.h), (800, 600))
        with self.assertRaises(tone_check.ToneError):  # a grey result has no chroma to scale
            tone_check.fit_material(original, banded(800, 600, [(200, 200, 200)]), screen, [], "paper")
        with self.assertRaises(tone_check.ToneError):
            tone_check.fit_material(original, result, {"strips": False, "materials": {"brass": {"class": "brass"}}}, [], "brass")

    def test_zone_fit_reaches_non_class_pixels_inside_the_zone(self) -> None:
        screen = {"strips": False, "materials": {"paper": {"class": "paper", "zone": [[0, 0, 800, 600]]}, "brass": {"class": "brass"}}}
        stripe = (20, 20, 20)  # text-like pixels: not paper by colour
        original = self.screen_image((200, 190, 160), (200, 190, 160), (100, 0, 140, 600, stripe))
        result = self.screen_image((240, 215, 150), (240, 215, 150), (100, 0, 140, 600, stripe))
        fitted, info = tone_check.fit_material(original, result, screen, [], "paper")
        before = bytes(result.data[(300 * 800 + 120) * 4:(300 * 800 + 120) * 4 + 3])
        after = bytes(fitted.data[(300 * 800 + 120) * 4:(300 * 800 + 120) * 4 + 3])
        self.assertNotEqual(before, after)  # the zone weight covers the stripe; a colour-class weight would leave it
        self.assertTrue(info["converged"], info)

    def test_zone_fit_leaves_another_materials_pixels_in_the_zone_alone(self) -> None:
        screen = {"strips": False, "materials": {"paper": {"class": "paper", "zone": [[0, 0, 800, 600]]}, "brass": {"class": "brass"}}}
        patch = (100, 0, 140, 600, (150, 110, 20))  # brass-class colour inside the paper zone
        original = self.screen_image((200, 190, 160), (200, 190, 160), patch)
        result = self.screen_image((240, 215, 150), (240, 215, 150), patch)
        fitted, _ = tone_check.fit_material(original, result, screen, [], "paper")
        i = (300 * 800 + 120) * 4
        self.assertEqual(bytes(result.data[i:i + 3]), bytes(fitted.data[i:i + 3]))

    def test_batch_needs_every_screen_and_reports_exit_2(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            empty = Path(tmp)
            err = io.StringIO()
            with redirect_stdout(io.StringIO()), redirect_stderr(err):
                self.assertEqual(tone_check.main(["batch", "--originals", str(empty), "--results", str(empty)]), 2)
                self.assertEqual(tone_check.main(["batch", "--originals", str(empty), "--results", str(empty), "--ids", "ctx-nope"]), 2)
            self.assertIn("TONE ERROR", err.getvalue())

    def test_masks_file_matches_the_library_and_the_classes(self) -> None:
        masks = tone_check.load_masks()
        library = json.loads((Path(tone_check.__file__).resolve().parent / "prompts" / "library.json").read_text(encoding="utf-8"))
        self.assertEqual(sorted(masks["screens"]), sorted(library["context_screens"]))
        rects = list(masks["strip_counters"])
        for spec in masks["screens"].values():
            rects += spec.get("exclude", [])
            for material in spec["materials"].values():
                self.assertIn(material["class"], tone_check.CLASSES)
                rects += material.get("zone", []) + material.get("not_zone", [])
        for x0, y0, x1, y1 in rects:
            self.assertTrue(0 <= x0 < x1 <= 800 and 0 <= y0 < y1 <= 600, (x0, y0, x1, y1))

    def test_wrong_aspect_and_cli_exit_codes(self) -> None:
        with self.assertRaises(tone_check.ToneError):
            tone_check.reduce_to_800x600(banded(800, 800, BASE))
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            pngio.write(tmp_path / "a.png", banded(800, 600, BASE))
            pngio.write(tmp_path / "same.png", banded(800, 600, BASE))
            pngio.write(tmp_path / "bright.png", banded(800, 600, [(c[0] + 60, c[1] + 60, c[2] + 60) for c in BASE]))
            out, err = io.StringIO(), io.StringIO()
            with redirect_stdout(out), redirect_stderr(err):
                self.assertEqual(tone_check.main(["compare", str(tmp_path / "a.png"), str(tmp_path / "same.png")]), 0)
                self.assertEqual(tone_check.main(["compare", str(tmp_path / "a.png"), str(tmp_path / "bright.png")]), 1)
                self.assertEqual(tone_check.main(["compare", str(tmp_path / "a.png"), str(tmp_path / "missing.png")]), 2)
            self.assertIn("PASS same", out.getvalue())
            self.assertIn("FAIL bright", out.getvalue())


if __name__ == "__main__":
    unittest.main()
