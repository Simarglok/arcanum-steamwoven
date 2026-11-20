"""ARC-RES-2457: tests of the surface metrics on synthetic images (no art)."""
from __future__ import annotations

import io
import random
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
import tempfile

from . import pngio, surface_metrics


def from_luma(w: int, h: int, fn) -> pngio.Image:
    data = bytearray()
    for y in range(h):
        for x in range(w):
            v = max(0, min(255, int(fn(x, y))))
            data += bytes((v, v, v, 255))
    return pngio.Image(w, h, data)


def noise_image(w: int, h: int, seed: int = 1) -> pngio.Image:
    rng = random.Random(seed)
    return from_luma(w, h, lambda x, y: 128 + rng.randint(-40, 40))


class SurfaceMetricsTests(unittest.TestCase):
    def test_reduce_plane_averages_blocks(self):
        plane = [0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0]  # 4 x 2
        out, w, h = surface_metrics.reduce_plane(plane, 4, 2, 2)
        self.assertEqual((w, h), (2, 1))
        self.assertEqual(out, [25.0, 45.0])
        with self.assertRaises(surface_metrics.MetricError):
            surface_metrics.reduce_plane(plane, 4, 2, 3)

    def test_sharpness_flat_is_zero_and_edges_are_not(self):
        flat = from_luma(30, 30, lambda x, y: 100)
        self.assertEqual(surface_metrics.sharpness(flat)["x1"]["mean_gradient"], 0.0)
        edge = from_luma(30, 30, lambda x, y: 0 if x < 15 else 200)
        self.assertGreater(surface_metrics.sharpness(edge)["x1"]["mean_gradient"], 0.0)

    def test_blur_lowers_fine_detail_more_than_x1_3(self):
        fine = from_luma(60, 60, lambda x, y: 200 if (x // 2) % 2 else 0)   # two-pixel stripes: mostly gone at x1/3
        report = surface_metrics.sharpness(fine)
        self.assertGreater(report["x1"]["mean_gradient"], report["x1_3"]["mean_gradient"])
        self.assertLess(report["ratio_third"], 0.5)
        coarse = from_luma(60, 60, lambda x, y: 200 if (x // 12 + y // 12) % 2 else 0)  # wide blocks: survives x1/3
        self.assertGreater(surface_metrics.sharpness(coarse)["ratio_third"], 0.5)

    def test_repetition_finds_a_tiled_texture_not_noise(self):
        tile = noise_image(64, 64, 7)
        w, h = 256, 128
        tiled = pngio.Image(w, h, bytearray(b"".join(bytes(tile.data[((y % 64) * 64 + (x % 64)) * 4:((y % 64) * 64 + (x % 64)) * 4 + 4]) for y in range(h) for x in range(w))))
        rep = surface_metrics.repetition(tiled, factor=2)
        self.assertGreater(rep["x"]["peak"], 0.5)
        self.assertEqual(rep["x"]["lag_px"] % 64, 0)
        fresh = surface_metrics.repetition(noise_image(w, h, 11), factor=2)
        self.assertLess(fresh["peak"], 0.2)

    def test_repetition_short_axis_has_no_reading(self):
        rep = surface_metrics.repetition(noise_image(256, 16), factor=2)   # 8 reduced rows
        self.assertIsNone(rep["y"]["peak"])
        self.assertIsNotNone(rep["x"]["peak"])

    def test_join_seam_smooth_and_jump(self):
        smooth = noise_image(64, 64)
        self.assertLess(surface_metrics.join_seam(smooth, "y", 32), 1.5)
        jump = from_luma(64, 64, lambda x, y: 20 if y < 32 else 220)
        self.assertGreaterEqual(surface_metrics.join_seam(jump, "y", 32), 1e9)
        both = from_luma(64, 64, lambda x, y: (random.Random(y).randint(0, 20)) + (0 if y < 32 else 150))
        self.assertGreater(surface_metrics.join_seam(both, "y", 32), 5.0)
        vert = from_luma(64, 64, lambda x, y: 20 if x < 40 else 220)
        self.assertGreaterEqual(surface_metrics.join_seam(vert, "x", 40), 1e9)

    def test_join_seam_refuses_a_bad_join(self):
        image = noise_image(16, 16)
        for axis, pos in (("y", 0), ("y", 16), ("x", 99), ("z", 4)):
            with self.assertRaises(surface_metrics.MetricError):
                surface_metrics.join_seam(image, axis, pos)

    def test_mask_limits_sharpness_and_repetition_to_the_masked_part(self):
        image = from_luma(64, 64, lambda x, y: (200 if (x // 3) % 2 else 0) if x < 32 else 90)   # detail on the left, flat on the right
        left = from_luma(64, 64, lambda x, y: 255 if x < 32 else 0)
        right = from_luma(64, 64, lambda x, y: 0 if x < 44 else 255)   # clear of the detail's blur
        self.assertGreater(surface_metrics.sharpness(image, left)["x1"]["mean_gradient"], 0.0)
        self.assertEqual(surface_metrics.sharpness(image, right)["x1"]["mean_gradient"], 0.0)
        self.assertEqual(surface_metrics.sharpness(image, left)["x1"]["pixels"], 30 * 62)   # the inner pixels whose neighbours are all counted
        self.assertTrue(surface_metrics.measure(image, mask=left)["masked"])
        self.assertIsNone(surface_metrics.repetition(image, factor=2, mask=right)["peak"])   # only flat cells: no signal

    def test_cli_measures_files_and_refuses_a_bad_join(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "a.png"
            pngio.write(path, noise_image(96, 64))
            out = io.StringIO()
            with redirect_stdout(out):
                code = surface_metrics.main([str(path), "--join", "y=32"])
            self.assertEqual(code, 0)
            self.assertIn("seams y=32", out.getvalue())
            err = io.StringIO()
            with redirect_stderr(err):
                self.assertEqual(surface_metrics.main([str(Path(tmp) / "missing.png")]), 2)
            with self.assertRaises(SystemExit), redirect_stderr(io.StringIO()):
                surface_metrics.main([str(path), "--join", "q=3"])


if __name__ == "__main__":
    unittest.main()
