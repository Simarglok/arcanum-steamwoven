"""ARC-UI-3058: tests of the glyph tone tool on synthetic images (no art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import glyph_tone, pngio, tone_check

DOME = (40, 50, 30)
BRIGHT = (200, 150, 60)
CENTRE = (19.5, 19.5)


def solid(w: int, h: int, color: tuple[int, int, int], alpha: int = 255) -> pngio.Image:
    return pngio.Image(w, h, bytearray(bytes((*color, alpha)) * (w * h)))


def frame(glyph_color_a, glyph_color_b, dither: bool) -> pngio.Image:
    """A 156 x 156 frame (4 px per unit): dome, a 10-unit square glyph in the middle, dithered (a checker of two colours per original pixel, 4 x 4 px) or not."""
    image = solid(156, 156, DOME)
    for y in range(78 - 20, 78 + 20):
        for x in range(78 - 20, 78 + 20):
            color = glyph_color_a if (not dither or (x // 4 + y // 4) % 2 == 0) else glyph_color_b
            o = (y * 156 + x) * 4
            image.data[o:o + 3] = bytes(color)
    return image


def master(color: tuple[int, int, int]) -> pngio.Image:
    """The glyph master: a 36 px square (10 units at 3.6 px per unit), fully opaque, on a transparent margin."""
    image = solid(40, 40, (0, 0, 0), 0)
    for y in range(2, 38):
        for x in range(2, 38):
            o = (y * 40 + x) * 4
            image.data[o:o + 4] = bytes((*color, 255))
    return image


def lightness(image: pngio.Image) -> float:
    return glyph_tone.master_mean(image)[0]


class GlyphToneTest(unittest.TestCase):
    def test_dithered_glyph_target_is_the_bright_half(self) -> None:
        original = frame(BRIGHT, DOME, dither=True)
        bright_l = tone_check._pixel_lab(*BRIGHT)[0]
        dome_l = tone_check._pixel_lab(*DOME)[0]
        report = glyph_tone.measure(original, master((80, 60, 30)), CENTRE)
        self.assertAlmostEqual(report["original_glyph"][0], bright_l, delta=1.0)
        mean_report = glyph_tone.measure(original, master((80, 60, 30)), CENTRE, stat="mean")
        self.assertAlmostEqual(mean_report["original_glyph"][0], (bright_l + dome_l) / 2, delta=1.5)
        self.assertAlmostEqual(report["original_dome"][0], dome_l, delta=0.5)

    def test_fit_moves_the_master_to_the_original_and_keeps_alpha(self) -> None:
        original = frame(BRIGHT, BRIGHT, dither=False)
        source = master((80, 60, 30))
        fitted, report = glyph_tone.fit(original, source, CENTRE)
        self.assertAlmostEqual(lightness(fitted), tone_check._pixel_lab(*BRIGHT)[0], delta=1.0)
        self.assertEqual([fitted.data[i] for i in range(3, len(fitted.data), 4)], [source.data[i] for i in range(3, len(source.data), 4)])
        self.assertEqual(fitted.data[3], 0)  # the transparent margin stays transparent
        self.assertIn("applied_shift", report)

    def test_strength_scales_the_shift(self) -> None:
        original = frame(BRIGHT, BRIGHT, dither=False)
        source = master((80, 60, 30))
        half, _ = glyph_tone.fit(original, source, CENTRE, strength=0.5)
        full, _ = glyph_tone.fit(original, source, CENTRE)
        start = lightness(source)
        self.assertAlmostEqual(lightness(half) - start, (lightness(full) - start) / 2, delta=1.0)

    def test_relative_to_the_plate(self) -> None:
        original = frame(BRIGHT, BRIGHT, dither=False)
        plate = solid(156, 156, (20, 30, 15))  # a darker plate: the glyph keeps the original's step above the dome
        report = glyph_tone.measure(original, master((80, 60, 30)), CENTRE, plate=plate)
        step = report["original_glyph"][0] - report["original_dome"][0]
        self.assertAlmostEqual(report["target_glyph"][0] - report["plate_dome"][0], step, delta=0.01)
        self.assertLess(report["target_glyph"][0], report["original_glyph"][0])

    def test_report_compares_frames(self) -> None:
        original = frame(BRIGHT, BRIGHT, dither=False)
        same = glyph_tone.report_frame(original, original, master((80, 60, 30)), CENTRE)
        self.assertAlmostEqual(same["delta_glyph_l"], 0.0, places=6)
        dim = frame((90, 70, 40), (90, 70, 40), dither=False)
        worse = glyph_tone.report_frame(original, dim, master((80, 60, 30)), CENTRE)
        self.assertLess(worse["delta_glyph_l"], -10)

    def test_frame_size_must_fit_the_grid(self) -> None:
        with self.assertRaises(glyph_tone.ToneError):
            glyph_tone.reduce_to_grid(solid(100, 100, DOME))

    def test_centre_must_parse(self) -> None:
        self.assertEqual(glyph_tone.parse_centre("19.5,20"), (19.5, 20.0))
        with self.assertRaises(glyph_tone.ToneError):
            glyph_tone.parse_centre("19.5")

    def test_cli_fit_and_errors(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            pngio.write(root / "o.png", frame(BRIGHT, BRIGHT, dither=False))
            pngio.write(root / "g.png", master((80, 60, 30)))
            out = io.StringIO()
            with redirect_stdout(out):
                code = glyph_tone.main(["fit", str(root / "o.png"), str(root / "g.png"), str(root / "r.png"), "--centre", "19.5,19.5", "--json"])
            self.assertEqual(code, 0)
            self.assertIn("applied_shift", json.loads(out.getvalue()))
            self.assertTrue((root / "r.png").exists())
            err = io.StringIO()
            with redirect_stderr(err):
                self.assertEqual(glyph_tone.main(["measure", str(root / "missing.png"), str(root / "g.png"), "--centre", "19.5,19.5"]), 2)
                self.assertEqual(glyph_tone.main(["fit", str(root / "o.png"), str(root / "g.png"), str(root / "x.png"), "--centre", "19.5,19.5", "--strength", "2"]), 2)


if __name__ == "__main__":
    unittest.main()
